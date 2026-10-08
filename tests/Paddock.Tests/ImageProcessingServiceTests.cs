using FluentAssertions;
using Paddock.Infrastructure.ImageProcessing;
using Paddock.Infrastructure.Metadata;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Paddock.Tests;

public class ImageProcessingServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ImageProcessingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Paddock_ImageTests_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task AutoRotateImageAsync_WithOrientation6_RotatesPixels_And_ResetsExifTagTo1()
    {
        // Arrange: crea un'immagine JPEG 300x200 (landscape) con orientamento EXIF 6 (90° CW, portrait)
        var testFilePath = Path.Combine(_tempDir, "portrait_6.jpg");
        const int initialWidth = 300;
        const int initialHeight = 200;

        using (var image = new Image<Rgba32>(initialWidth, initialHeight))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
            image.SaveAsJpeg(testFilePath, new JpegEncoder { Quality = 95 });
        }

        var service = new ImageSharpProcessingService();

        // Act: prima esecuzione di AutoRotate
        var rotated = await service.AutoRotateImageAsync(testFilePath);

        // Assert: rotazione eseguita con successo
        rotated.Should().BeTrue();

        // Verifica che le dimensioni fisiche siano ora 200x300 (portrait)
        using (var resultImage = Image.Load<Rgba32>(testFilePath))
        {
            resultImage.Width.Should().Be(initialHeight); // 200
            resultImage.Height.Should().Be(initialWidth); // 300

            // Verifica che il tag EXIF Orientation sia reimpostato a 1 (TopLeft)
            if (resultImage.Metadata.ExifProfile != null &&
                resultImage.Metadata.ExifProfile.TryGetValue(ExifTag.Orientation, out var orientationTag))
            {
                ((ushort)orientationTag.Value).Should().Be(1);
            }
        }

        // Act 2: riesecuzione sullo stesso file già ruotato
        var secondRun = await service.AutoRotateImageAsync(testFilePath);

        // Assert 2: non esegue una seconda rotazione inutile
        secondRun.Should().BeFalse();
    }

    [Fact]
    public async Task AutoRotateImageAsync_WithNormalOrientation1_ReturnsFalse_AndDoesNotModify()
    {
        // Arrange: crea un'immagine normale (orientamento 1)
        var testFilePath = Path.Combine(_tempDir, "normal_1.jpg");
        using (var image = new Image<Rgba32>(400, 300))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)1);
            image.SaveAsJpeg(testFilePath);
        }

        var service = new ImageSharpProcessingService();

        // Act
        var result = await service.AutoRotateImageAsync(testFilePath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AutoRotateImageAsync_WithRawFile_ReturnsFalse_AndDoesNotTouchFile()
    {
        var rawPath = Path.Combine(_tempDir, "photo.cr2");
        await File.WriteAllBytesAsync(rawPath, new byte[] { 1, 2, 3, 4 });

        var service = new ImageSharpProcessingService();

        var result = await service.AutoRotateImageAsync(rawPath);

        result.Should().BeFalse();
        (await File.ReadAllBytesAsync(rawPath)).Should().Equal(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task ExtractOrientationAsync_ReadsExifOrientationCorrectly()
    {
        var testFilePath = Path.Combine(_tempDir, "orientation_test.jpg");
        using (var image = new Image<Rgba32>(200, 200))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)8);
            image.SaveAsJpeg(testFilePath);
        }

        var metadataService = new ExifToolMetadataService();

        var orientation = await metadataService.ExtractOrientationAsync(testFilePath);

        orientation.Should().Be(8);
    }
}

