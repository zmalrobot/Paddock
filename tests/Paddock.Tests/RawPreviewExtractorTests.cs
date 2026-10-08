using Avalonia.Media;
using FluentAssertions;
using Moq;
using Paddock.Core.DTOs;
using Paddock.Core.Models;
using Paddock.Infrastructure.ImageProcessing;
using Paddock.UI.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Paddock.Tests;

public class RawPreviewExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public RawPreviewExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Paddock_RawTests_" + Guid.NewGuid().ToString("N"));
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
        catch { }
    }

    /// <summary>
    /// Crea un file simulato Canon .CR2 con struttura IFD valida conforme alle specifiche TIFF/CR2,
    /// contenente un payload JPEG reale generato in memoria.
    /// </summary>
    private string CreateSynthesizedCanonCr2File(int jpegWidth = 400, int jpegHeight = 300)
    {
        var cr2Path = Path.Combine(_tempDir, $"TEST_{Guid.NewGuid():N}.CR2");

        // 1. Genera payload JPEG valido
        byte[] jpegPayload;
        using (var img = new Image<Rgba32>(jpegWidth, jpegHeight))
        {
            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
            jpegPayload = ms.ToArray();
        }

        using var fs = new FileStream(cr2Path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(fs);

        // Header TIFF (16 byte)
        writer.Write((ushort)0x4949); // "II" Little Endian
        writer.Write((ushort)42);     // Magic TIFF
        writer.Write((uint)0x00000010); // Offset a IFD0 (subito dopo l'header)
        writer.Write((ushort)0x5243); // "CR"
        writer.Write((ushort)0x0002); // Versione 2
        writer.Write((uint)0);        // Reserved

        // Posizione 16 (0x10): IFD0
        // Scriviamo IFD0 con 1 entry (es. ImageWidth) e NextIFD che punta a IFD3
        long ifd0Pos = fs.Position;
        writer.Write((ushort)1);      // 1 entry
        writer.Write((ushort)0x0100); // Tag ImageWidth
        writer.Write((ushort)4);      // Type LONG
        writer.Write((uint)1);        // Count 1
        writer.Write((uint)jpegWidth);// Value
        
        long ifd3Pos = ifd0Pos + 2 + 12 + 4; // Subito dopo IFD0
        writer.Write((uint)ifd3Pos);  // NextIFD = IFD3

        // IFD3 (Preview IFD): contiene StripOffsets (0x0111) e StripByteCounts (0x0117)
        long jpegPayloadOffset = ifd3Pos + 2 + (12 * 2) + 4; // Subito dopo IFD3
        writer.Write((ushort)2);      // 2 entries
        
        // Entry 1: StripOffsets (0x0111)
        writer.Write((ushort)0x0111);
        writer.Write((ushort)4);      // LONG
        writer.Write((uint)1);        // Count 1
        writer.Write((uint)jpegPayloadOffset); // Value = Offset al JPEG

        // Entry 2: StripByteCounts (0x0117)
        writer.Write((ushort)0x0117);
        writer.Write((ushort)4);      // LONG
        writer.Write((uint)1);        // Count 1
        writer.Write((uint)jpegPayload.Length); // Value = Lunghezza JPEG

        writer.Write((uint)0);        // NextIFD = 0 (fine catena)

        // Payload JPEG
        writer.Write(jpegPayload);
        writer.Flush();

        return cr2Path;
    }

    [Fact]
    public async Task ExtractEmbeddedJpegAsync_FromSynthesizedCr2_ExtractsValidJpegBytes()
    {
        var cr2Path = CreateSynthesizedCanonCr2File(600, 400);

        var extracted = await RawPreviewExtractor.ExtractEmbeddedJpegAsync(cr2Path);

        extracted.Should().NotBeNull();
        extracted.Length.Should().BeGreaterThan(1000);
        extracted[0].Should().Be(0xFF);
        extracted[1].Should().Be(0xD8); // SOI JPEG

        // Verifica che i byte estratti siano decodificabili da ImageSharp
        using var loaded = Image.Load<Rgba32>(extracted);
        loaded.Width.Should().Be(600);
        loaded.Height.Should().Be(400);
    }

    [Fact]
    public async Task ImageSharpProcessingService_GenerateThumbnailAsync_GeneratesThumbnailForCr2()
    {
        var cr2Path = CreateSynthesizedCanonCr2File(800, 600);
        var service = new ImageSharpProcessingService();

        var thumbnailBytes = await service.GenerateThumbnailAsync(cr2Path, 260, 160);

        thumbnailBytes.Should().NotBeNull();
        thumbnailBytes.Length.Should().BeGreaterThan(500);

        using var thumb = Image.Load<Rgba32>(thumbnailBytes);
        thumb.Width.Should().BeLessThanOrEqualTo(260);
        thumb.Height.Should().BeLessThanOrEqualTo(160);
    }

    [Fact]
    public async Task ImageSharpProcessingService_ExtractRawPreviewAsync_ReturnsFullResolutionJpeg()
    {
        var cr2Path = CreateSynthesizedCanonCr2File(1200, 800);
        var service = new ImageSharpProcessingService();

        var previewBytes = await service.ExtractRawPreviewAsync(cr2Path);

        previewBytes.Should().NotBeNull();
        previewBytes.Length.Should().BeGreaterThan(1000);

        using var preview = Image.Load<Rgba32>(previewBytes);
        preview.Width.Should().Be(1200);
        preview.Height.Should().Be(800);
    }

    [Fact]
    public async Task PhotoViewerViewModel_LoadsRawPhoto_AndDisplaysBitmap()
    {
        var cr2Path = CreateSynthesizedCanonCr2File(500, 350);
        var service = new ImageSharpProcessingService();

        var foto = new Foto
        {
            Id = Guid.NewGuid(),
            NomeFileOriginale = Path.GetFileName(cr2Path),
            PathRelativo = cr2Path,
            Formato = "RAW"
        };
        var item = new PhotoItemViewModel(foto, cr2Path);

        using var vm = new PhotoViewerViewModel(new[] { item }, 0, service);
        var mockImage = new Mock<IImage>().Object;
        bool streamReceivedHasData = false;
        vm.BitmapStreamLoader = stream =>
        {
            if (stream.Length > 1000) streamReceivedHasData = true;
            return mockImage;
        };

        await vm.LoadPhotoAtCurrentIndexAsync();

        streamReceivedHasData.Should().BeTrue();
        vm.CurrentBitmap.Should().BeSameAs(mockImage);
        vm.StatusMessage.Should().BeNull();
    }

    [Fact]
    public async Task SlideshowWindowViewModel_LoadsRawPhoto_WithoutSkipping()
    {
        var cr2Path = CreateSynthesizedCanonCr2File(640, 480);
        var service = new ImageSharpProcessingService();
        var atletaId = Guid.NewGuid();

        var config = new SlideshowConfig
        {
            EventoId = Guid.NewGuid(),
            SelectedAtletiIds = new List<Guid> { atletaId },
            IncludeRaw = true,
            IncludeJpegPng = false,
            DurationSeconds = 2
        };

        var mockRepo = new Moq.Mock<Paddock.Core.Interfaces.IExcelRepository>();
        mockRepo.Setup(r => r.GetBasePathAsync(Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(_tempDir);
        mockRepo.Setup(r => r.GetFotoByEventoAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Foto>
            {
                new Foto
                {
                    Id = Guid.NewGuid(),
                    AtletaId = atletaId,
                    NomeFileOriginale = Path.GetFileName(cr2Path),
                    PathRelativo = Path.GetFileName(cr2Path),
                    Formato = "RAW"
                }
            });
        mockRepo.Setup(r => r.GetAtletiByEventoAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Atleta> { new Atleta { Id = atletaId, Nome = "Test", Cognome = "Runner" } });
        mockRepo.Setup(r => r.GetDisciplineByEventoAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Disciplina>());

        var vm = new SlideshowWindowViewModel(config, mockRepo.Object, service);
        var mockImage = new Mock<IImage>().Object;
        bool streamReceivedHasData = false;
        vm.BitmapStreamLoader = stream =>
        {
            if (stream.Length > 1000) streamReceivedHasData = true;
            return mockImage;
        };

        // Avvia lo slideshow che carica le foto e visualizza la prima
        await vm.StartAsync();

        streamReceivedHasData.Should().BeTrue();
        vm.CurrentImage.Should().BeSameAs(mockImage);
        vm.CurrentPhotoNameText.Should().Be(Path.GetFileName(cr2Path));
    }

    [Theory]
    [InlineData("sample.cr2", true)]
    [InlineData("SAMPLE.CR2", true)]
    [InlineData("camera.cr3", true)]
    [InlineData("nikon.nef", true)]
    [InlineData("sony.arw", true)]
    [InlineData("adobe.dng", true)]
    [InlineData("fuji.raf", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("photo.png", false)]
    [InlineData("notes.txt", false)]
    [InlineData("", false)]
    public void IsRawFormat_CorrectlyIdentifiesExtensions(string filename, bool expected)
    {
        RawPreviewExtractor.IsRawFormat(filename).Should().Be(expected);
    }

    [Fact]
    public async Task ExtractEmbeddedJpegAsync_WithNonExistentOrCorruptFile_ReturnsEmptyGracefully()
    {
        var nonExistent = Path.Combine(_tempDir, "not_existing.cr2");
        var result = await RawPreviewExtractor.ExtractEmbeddedJpegAsync(nonExistent);
        result.Should().BeEmpty();

        var corruptFile = Path.Combine(_tempDir, "corrupt.cr2");
        await File.WriteAllBytesAsync(corruptFile, new byte[] { 0x01, 0x02, 0x03, 0x04 });
        var corruptResult = await RawPreviewExtractor.ExtractEmbeddedJpegAsync(corruptFile);
        corruptResult.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractEmbeddedJpegAsync_StreamScannerFallback_FindsEmbeddedJpegWithoutIFD()
    {
        var rawPath = Path.Combine(_tempDir, "no_ifd.cr2");

        // Crea un payload JPEG valido circondato da byte casuali senza header TIFF/CR2
        byte[] jpegPayload;
        using (var img = new Image<Rgba32>(320, 240))
        {
            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms);
            jpegPayload = ms.ToArray();
        }

        var headerPadding = new byte[256];
        var trailerPadding = new byte[128];
        new Random().NextBytes(headerPadding);
        new Random().NextBytes(trailerPadding);

        using (var fs = new FileStream(rawPath, FileMode.Create, FileAccess.Write))
        {
            fs.Write(headerPadding);
            fs.Write(jpegPayload);
            fs.Write(trailerPadding);
        }

        var extracted = await RawPreviewExtractor.ExtractEmbeddedJpegAsync(rawPath);
        extracted.Should().NotBeNull();
        extracted.Length.Should().Be(jpegPayload.Length);
        extracted[0].Should().Be(0xFF);
        extracted[1].Should().Be(0xD8);
    }
}
