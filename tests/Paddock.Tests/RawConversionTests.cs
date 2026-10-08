using FluentAssertions;
using Moq;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Infrastructure.ImageProcessing;
using Paddock.Infrastructure.Storage;
using Paddock.UI.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Paddock.Tests;

public class RawConversionTests : IDisposable
{
    private readonly string _tempDir;

    public RawConversionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"paddock_raw_conv_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignora errori di pulizia
        }
    }

    private string CreateSynthesizedCanonCr2File(int jpegWidth = 400, int jpegHeight = 300)
    {
        var cr2Path = Path.Combine(_tempDir, $"TEST_{Guid.NewGuid():N}.CR2");

        byte[] jpegPayload;
        using (var img = new Image<Rgba32>(jpegWidth, jpegHeight))
        {
            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
            jpegPayload = ms.ToArray();
        }

        using var fs = new FileStream(cr2Path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(fs);

        writer.Write((ushort)0x4949); // "II" Little Endian
        writer.Write((ushort)42);     // Magic TIFF
        writer.Write((uint)0x00000010); // Offset a IFD0
        writer.Write((ushort)0x5243); // "CR"
        writer.Write((ushort)0x0002); // Versione 2
        writer.Write((uint)0);        // Reserved

        // IFD0
        long ifd0Pos = fs.Position;
        writer.Write((ushort)1);
        writer.Write((ushort)0x0100); // ImageWidth
        writer.Write((ushort)4);
        writer.Write((uint)1);
        writer.Write((uint)jpegWidth);

        long ifd3Pos = ifd0Pos + 2 + 12 + 4;
        writer.Write((uint)ifd3Pos);

        // IFD3 (Preview)
        long jpegPayloadOffset = ifd3Pos + 2 + (12 * 2) + 4;
        writer.Write((ushort)2);
        
        writer.Write((ushort)0x0111); // StripOffsets
        writer.Write((ushort)4);
        writer.Write((uint)1);
        writer.Write((uint)jpegPayloadOffset);

        writer.Write((ushort)0x0117); // StripByteCounts
        writer.Write((ushort)4);
        writer.Write((uint)1);
        writer.Write((uint)jpegPayload.Length);

        writer.Write((uint)0); // End of IFDs
        writer.Write(jpegPayload);
        writer.Flush();

        return cr2Path;
    }

    [Fact]
    public async Task ConvertRawToJpegAsync_ValidRaw_ExtractsAndConvertsToJpeg_WithColorCorrection()
    {
        // Arrange
        var mockMeta = new Mock<IMetadataService>();
        var service = new ImageSharpProcessingService(mockMeta.Object);
        var rawPath = CreateSynthesizedCanonCr2File(300, 200);
        var destJpeg = Path.Combine(_tempDir, "OUTPUT_CONVERTED.jpg");

        var options = new RawConversionOptions
        {
            AutoRotate = true,
            ApplyColorCorrection = true,
            Watermark = new WatermarkOptions { Enabled = false },
            Metadata = new MetadataOptions { InjectPhotographer = false }
        };

        // Act
        var result = await service.ConvertRawToJpegAsync(rawPath, destJpeg, options);

        // Assert
        result.Should().BeTrue();
        File.Exists(destJpeg).Should().BeTrue();
        new FileInfo(destJpeg).Length.Should().BeGreaterThan(100);

        // Verifica che sia un'immagine valida
        using var loaded = await Image.LoadAsync(destJpeg);
        loaded.Width.Should().Be(300);
        loaded.Height.Should().Be(200);
    }

    [Fact]
    public async Task ConvertRawToJpegAsync_NonExistentRaw_ThrowsFileNotFoundException()
    {
        // Arrange
        var service = new ImageSharpProcessingService();
        var rawPath = Path.Combine(_tempDir, "NON_EXISTENT.CR2");
        var destJpeg = Path.Combine(_tempDir, "OUTPUT.jpg");
        var options = new RawConversionOptions();

        // Act
        var act = async () => await service.ConvertRawToJpegAsync(rawPath, destJpeg, options);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task RawConversionDialogViewModel_ScanningAndCandidateDetection()
    {
        // Arrange
        var evento = new Evento { Id = Guid.NewGuid(), NomeEvento = "Test Event", CartellaDestinazioneRoot = _tempDir };
        var atleta = new Atleta { Id = Guid.NewGuid(), EventoId = evento.Id, Nome = "Mario", Cognome = "Rossi", NumeroPettorale = "44" };
        var disciplina = new Disciplina { Id = Guid.NewGuid(), EventoId = evento.Id, NomeDisciplina = "MX1" };

        var mockExcel = new Mock<IExcelRepository>();
        mockExcel.Setup(x => x.GetResolvedBasePathAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tempDir);
        mockExcel.Setup(x => x.GetDisciplineByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Disciplina> { disciplina });

        var fileOrg = new FileOrganizationService();
        var mockImg = new Mock<IImageProcessingService>();

        // Crea file RAW sul disco
        var rawPath = CreateSynthesizedCanonCr2File(200, 150);
        var rawFileName = Path.GetFileName(rawPath);

        // Simula che il RAW sia registrato in Foto, ma senza JPEG
        var rawFoto = new Foto
        {
            Id = Guid.NewGuid(),
            EventoId = evento.Id,
            AtletaId = atleta.Id,
            DisciplinaId = disciplina.Id,
            Formato = "RAW",
            PathRelativo = Path.GetRelativePath(_tempDir, rawPath),
            NomeFileOriginale = rawFileName
        };

        mockExcel.Setup(x => x.GetFotoByEventoAsync(evento.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Foto> { rawFoto });

        var vm = new RawConversionDialogViewModel(evento, atleta, mockExcel.Object, fileOrg, mockImg.Object);

        // Act
        await vm.InitializeAndScanAsync();

        // Assert
        vm.TotalCandidateCount.Should().Be(1);
        vm.SelectedCandidateCount.Should().Be(1);
        vm.CanConvert.Should().BeTrue();
        vm.SelectAll.Should().BeTrue();
        vm.RawCandidates.First().FileName.Should().Be(rawFileName);
    }

    [Fact]
    public async Task RawConversionDialogViewModel_SelectAllToggle_WorksAsExpected()
    {
        // Arrange
        var evento = new Evento { Id = Guid.NewGuid(), NomeEvento = "Test Event" };
        var atleta = new Atleta { Id = Guid.NewGuid(), Nome = "Luigi", Cognome = "Verdi" };
        var mockExcel = new Mock<IExcelRepository>();
        var fileOrg = new FileOrganizationService();
        var mockImg = new Mock<IImageProcessingService>();

        var vm = new RawConversionDialogViewModel(evento, atleta, mockExcel.Object, fileOrg, mockImg.Object);
        vm.RawCandidates.Add(new RawCandidateItemViewModel { FileName = "IMG_01.CR2", IsSelected = true });
        vm.RawCandidates.Add(new RawCandidateItemViewModel { FileName = "IMG_02.CR2", IsSelected = true });

        // Act - Deseleziona tutti
        vm.SelectAll = false;

        // Assert
        vm.SelectedCandidateCount.Should().Be(0);
        vm.CanConvert.Should().BeFalse();
        vm.RawCandidates.All(c => !c.IsSelected).Should().BeTrue();

        // Act - Riseleziona tutti
        vm.ToggleSelectAllCommand.Execute(null);

        // Assert
        vm.SelectAll.Should().BeTrue();
        vm.SelectedCandidateCount.Should().Be(2);
        vm.CanConvert.Should().BeTrue();
        vm.RawCandidates.All(c => c.IsSelected).Should().BeTrue();
    }

    [Fact]
    public async Task RawConversionDialogViewModel_ConvertAsync_ConvertsAndAddsToExcelBatch()
    {
        // Arrange
        var evento = new Evento { Id = Guid.NewGuid(), NomeEvento = "GranPremio", CartellaDestinazioneRoot = _tempDir };
        var atleta = new Atleta { Id = Guid.NewGuid(), EventoId = evento.Id, Nome = "Marco", Cognome = "Bianchi", NumeroPettorale = "10" };
        var disciplina = new Disciplina { Id = Guid.NewGuid(), EventoId = evento.Id, NomeDisciplina = "Moto3" };

        var mockExcel = new Mock<IExcelRepository>();
        mockExcel.Setup(x => x.GetResolvedBasePathAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tempDir);
        mockExcel.Setup(x => x.GetDisciplineByEventoAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Disciplina> { disciplina });

        var fileOrg = new FileOrganizationService();
        var mockImg = new Mock<IImageProcessingService>();

        var rawFile = CreateSynthesizedCanonCr2File(100, 100);
        var baseName = Path.GetFileNameWithoutExtension(rawFile);

        mockImg.Setup(x => x.ConvertRawToJpegAsync(
                rawFile,
                It.IsAny<string>(),
                It.IsAny<RawConversionOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, RawConversionOptions, CancellationToken>((src, dst, opt, ct) =>
            {
                // Crea file simulato JPEG di output
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.WriteAllText(dst, "FAKE_JPEG_CONTENT");
                return Task.FromResult(true);
            });

        var vm = new RawConversionDialogViewModel(evento, atleta, mockExcel.Object, fileOrg, mockImg.Object);
        var candidate = new RawCandidateItemViewModel
        {
            RawFilePath = rawFile,
            FileName = Path.GetFileName(rawFile),
            BaseFileName = baseName,
            Disciplina = disciplina,
            IsSelected = true
        };
        vm.RawCandidates.Add(candidate);

        // Act
        await vm.ConvertCommand.ExecuteAsync(null);

        // Assert
        vm.IsCompleted.Should().BeTrue();
        vm.PhotosConverted.Should().BeTrue();
        vm.ProcessedCount.Should().Be(1);
        vm.RawCandidates.Count.Should().Be(0); // Rimosso dopo conversione riuscita

        mockExcel.Verify(x => x.AddFotoBatchAsync(
            It.Is<IEnumerable<Foto>>(list => list.Any(f => f.Formato == "JPEG" && f.NomeFileOriginale == Path.GetFileName(rawFile))),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

