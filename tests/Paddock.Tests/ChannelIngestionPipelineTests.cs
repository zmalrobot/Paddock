using FluentAssertions;
using Moq;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Infrastructure.Ingestion;
using Paddock.Infrastructure.Storage;
using Xunit;

namespace Paddock.Tests;

public class ChannelIngestionPipelineTests : IDisposable
{
    private readonly string _testTempDir;
    private readonly string _sourceDir;
    private readonly string _destDir;

    public ChannelIngestionPipelineTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), "Paddock_PipeTests_" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_testTempDir, "SD_Card_DCIM");
        _destDir = Path.Combine(_testTempDir, "PhotoLibraryRoot");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task Pipeline_ProcessesFilesInParallel_And_BatchesToExcel()
    {
        // Arrange - Crea 5 finti file di foto (.jpg e .cr3)
        for (int i = 1; i <= 5; i++)
        {
            var ext = i % 2 == 0 ? ".cr3" : ".jpg";
            var filePath = Path.Combine(_sourceDir, $"IMG_{i:D4}{ext}");
            await File.WriteAllBytesAsync(filePath, new byte[] { 1, 2, 3, 4, (byte)i });
        }

        var fileOrg = new FileOrganizationService();
        var mockImageService = new Mock<IImageProcessingService>();
        var mockMetadataService = new Mock<IMetadataService>();
        mockMetadataService.Setup(m => m.ExtractCaptureDateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.Now);
        mockMetadataService.Setup(m => m.WritePhotographerMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockExcelRepo = new Mock<IExcelRepository>();
        List<Foto> savedBatch = new();
        mockExcelRepo.Setup(e => e.AddFotoBatchAsync(It.IsAny<IEnumerable<Foto>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Foto>, CancellationToken>((batch, ct) => savedBatch.AddRange(batch))
            .Returns(Task.CompletedTask);

        var pipeline = new ChannelIngestionPipelineService(
            fileOrg,
            mockImageService.Object,
            mockMetadataService.Object,
            mockExcelRepo.Object);

        var evento = new Evento
        {
            NomeEvento = "Triathlon Sprint",
            CartellaDestinazioneRoot = _destDir
        };
        var atleta = new Atleta
        {
            NumeroPettorale = "88",
            Nome = "Giorgio",
            Cognome = "Verdi"
        };
        var disciplina = new Disciplina { NomeDisciplina = "Corsa" };

        var request = new IngestionJobRequest
        {
            SourceDirectory = _sourceDir,
            EventoTarget = evento,
            AtletaTarget = atleta,
            DisciplinaTarget = disciplina,
            Metadata = new MetadataOptions { PhotographerName = "ProPhotographer" },
            Watermark = new WatermarkOptions { Enabled = false }
        };

        var completedTcs = new TaskCompletionSource<IngestionProgressReport>();
        pipeline.JobCompleted += (sender, report) => completedTcs.TrySetResult(report);

        // Act
        var jobId = await pipeline.EnqueueJobAsync(request);
        var finalReport = await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        finalReport.Should().NotBeNull();
        finalReport.Status.Should().Be(IngestionStatus.Completed);
        finalReport.ProcessedFiles.Should().Be(5);
        finalReport.TotalFiles.Should().Be(5);

        // Verifica che ExcelRepo abbia ricevuto il batch
        savedBatch.Should().HaveCount(5);
        savedBatch.Should().Contain(f => f.Formato == "RAW");
        savedBatch.Should().Contain(f => f.Formato == "JPEG");
        savedBatch.All(f => f.Fotografo == "ProPhotographer").Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_CanBeCancelled_Gracefully()
    {
        // Arrange
        for (int i = 1; i <= 20; i++)
        {
            var filePath = Path.Combine(_sourceDir, $"IMG_BULK_{i:D4}.jpg");
            await File.WriteAllBytesAsync(filePath, new byte[1024 * 50]);
        }

        var fileOrg = new FileOrganizationService();
        var mockImageService = new Mock<IImageProcessingService>();
        var mockMetadataService = new Mock<IMetadataService>();
        var mockExcelRepo = new Mock<IExcelRepository>();

        var pipeline = new ChannelIngestionPipelineService(
            fileOrg,
            mockImageService.Object,
            mockMetadataService.Object,
            mockExcelRepo.Object);

        var request = new IngestionJobRequest
        {
            SourceDirectory = _sourceDir,
            EventoTarget = new Evento { NomeEvento = "Corsa", CartellaDestinazioneRoot = _destDir },
            AtletaTarget = new Atleta { NumeroPettorale = "1", Nome = "A", Cognome = "B" },
            DisciplinaTarget = new Disciplina { NomeDisciplina = "100m" }
        };

        var completedTcs = new TaskCompletionSource<IngestionProgressReport>();
        pipeline.JobCompleted += (sender, report) => completedTcs.TrySetResult(report);

        // Act
        var jobId = await pipeline.EnqueueJobAsync(request);
        // Annulla subito
        pipeline.CancelJob(jobId);

        var finalReport = await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        finalReport.Status.Should().BeOneOf(IngestionStatus.Cancelled, IngestionStatus.Completed);
    }

    [Fact]
    public async Task Pipeline_RawJpegPair_ReceivesSameRenamedRoot_AndDistributesToRawAndJpegFolders()
    {
        // Arrange - Crea una coppia RAW + JPEG
        var rawSource = Path.Combine(_sourceDir, "IMG_4589.CR2");
        var jpgSource = Path.Combine(_sourceDir, "IMG_4589.JPG");
        await File.WriteAllBytesAsync(rawSource, new byte[] { 10, 20, 30 });
        await File.WriteAllBytesAsync(jpgSource, new byte[] { 40, 50, 60 });

        var fileOrg = new FileOrganizationService();
        var mockImageService = new Mock<IImageProcessingService>();
        var mockMetadataService = new Mock<IMetadataService>();
        var mockExcelRepo = new Mock<IExcelRepository>();
        List<Foto> savedBatch = new();
        mockExcelRepo.Setup(e => e.AddFotoBatchAsync(It.IsAny<IEnumerable<Foto>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Foto>, CancellationToken>((batch, ct) => savedBatch.AddRange(batch))
            .Returns(Task.CompletedTask);

        var mockRenamer = new Mock<IPhotoRenamerService>();
        var expectedRoot = "EOS600D_20261007_153022_04_4589";

        mockRenamer.Setup(r => r.ComputeRenamedRoot(It.IsAny<IEnumerable<string>>()))
            .Returns(expectedRoot);

        mockRenamer.Setup(r => r.GetRenamedFileName(It.IsAny<string>(), expectedRoot))
            .Returns<string, string?>((path, root) => $"{root}{Path.GetExtension(path)}");

        var pipeline = new ChannelIngestionPipelineService(
            fileOrg,
            mockImageService.Object,
            mockMetadataService.Object,
            mockExcelRepo.Object,
            mockRenamer.Object);

        var evento = new Evento { NomeEvento = "Mtb Trophy", CartellaDestinazioneRoot = _destDir };
        var atleta = new Atleta { NumeroPettorale = "105", Nome = "Mario", Cognome = "Rossi" };
        var disciplina = new Disciplina { NomeDisciplina = "Downhill" };

        var request = new IngestionJobRequest
        {
            SourceDirectory = _sourceDir,
            EventoTarget = evento,
            AtletaTarget = atleta,
            DisciplinaTarget = disciplina
        };

        var completedTcs = new TaskCompletionSource<IngestionProgressReport>();
        pipeline.JobCompleted += (s, r) => completedTcs.TrySetResult(r);

        // Act
        await pipeline.EnqueueJobAsync(request);
        var report = await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        report.Status.Should().Be(IngestionStatus.Completed);
        report.ProcessedFiles.Should().Be(2);

        // ComputeRenamedRoot deve essere chiamato una sola volta per l'intera coppia
        mockRenamer.Verify(r => r.ComputeRenamedRoot(It.IsAny<IEnumerable<string>>()), Times.Once);

        savedBatch.Should().HaveCount(2);

        var rawFoto = savedBatch.First(f => f.Formato == "RAW");
        rawFoto.NomeFileOriginale.Should().Be("IMG_4589.CR2");
        rawFoto.PathRelativo.Should().EndWith("EOS600D_20261007_153022_04_4589.CR2");
        rawFoto.PathRelativo.Should().Contain("Raw");

        var jpgFoto = savedBatch.First(f => f.Formato == "JPEG");
        jpgFoto.NomeFileOriginale.Should().Be("IMG_4589.JPG");
        jpgFoto.PathRelativo.Should().EndWith("EOS600D_20261007_153022_04_4589.JPG");
        jpgFoto.PathRelativo.Should().Contain("Jpeg");

        // Verifica file fisici su disco
        var fullRawPath = Path.Combine(_destDir, rawFoto.PathRelativo);
        var fullJpgPath = Path.Combine(_destDir, jpgFoto.PathRelativo);
        File.Exists(fullRawPath).Should().BeTrue();
        File.Exists(fullJpgPath).Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_WithAutoRotateEnabled_InvokesAutoRotateImageAsync_OnRasterFiles()
    {
        // Arrange
        var jpgFile = Path.Combine(_sourceDir, "IMG_1001.JPG");
        var rawFile = Path.Combine(_sourceDir, "IMG_1001.CR2");
        await File.WriteAllBytesAsync(jpgFile, new byte[] { 10, 20, 30 });
        await File.WriteAllBytesAsync(rawFile, new byte[] { 40, 50, 60 });

        var fileOrg = new FileOrganizationService();
        var mockImageService = new Mock<IImageProcessingService>();
        mockImageService.Setup(i => i.AutoRotateImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockMetadata = new Mock<IMetadataService>();
        var mockExcelRepo = new Mock<IExcelRepository>();

        var pipeline = new ChannelIngestionPipelineService(
            fileOrg,
            mockImageService.Object,
            mockMetadata.Object,
            mockExcelRepo.Object);

        var request = new IngestionJobRequest
        {
            SourceDirectory = _sourceDir,
            EventoTarget = new Evento { NomeEvento = "Gara Rotazione", CartellaDestinazioneRoot = _destDir },
            AtletaTarget = new Atleta { NumeroPettorale = "1", Nome = "Mario", Cognome = "Rossi" },
            DisciplinaTarget = new Disciplina { NomeDisciplina = "Salto" },
            AutoRotate = true,
            Watermark = new WatermarkOptions { Enabled = false }
        };

        var completedTcs = new TaskCompletionSource<IngestionProgressReport>();
        pipeline.JobCompleted += (s, r) => completedTcs.TrySetResult(r);

        // Act
        await pipeline.EnqueueJobAsync(request);
        var report = await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        report.Status.Should().Be(IngestionStatus.Completed);
        // AutoRotateImageAsync deve essere invocato per il file JPEG raster, ma NON per il file RAW
        mockImageService.Verify(i => i.AutoRotateImageAsync(It.Is<string>(p => p.EndsWith(".JPG", StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()), Times.Once);
        mockImageService.Verify(i => i.AutoRotateImageAsync(It.Is<string>(p => p.EndsWith(".CR2", StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()), Times.Never);
    }
}

