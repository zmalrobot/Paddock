using FluentAssertions;
using Paddock.Core.Interfaces;
using Paddock.Infrastructure.Metadata;
using Xunit;

namespace Paddock.Tests;

public class PhotoRenamerServiceTests
{
    [Theory]
    [InlineData("Canon EOS 600D", "EOS600D")]
    [InlineData("Canon EOS 100D", "EOS100D")]
    [InlineData("Canon EOS-1D X Mark III", "EOS-1DXMarkIII")]
    [InlineData("canon eos rebel t3i", "eosrebelt3i")]
    [InlineData("NIKON D750", "NIKOND750")]
    [InlineData("SONY ILCE-7M3", "SONYILCE-7M3")]
    [InlineData("Canon", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CleanCameraModel_RemovesCanonAndSpaces_Accurately(string? raw, string? expected)
    {
        var result = PhotoRenamerService.CleanCameraModel(raw);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("04", "04")]
    [InlineData("4", "04")]
    [InlineData("42", "42")]
    [InlineData("420", "42")]
    [InlineData("", "00")]
    [InlineData("   ", "00")]
    [InlineData(null, "00")]
    [InlineData("abc", "00")]
    public void FormatSubSec_ReturnsTwoDigitCentiseconds_WithFallback00(string? raw, string expected)
    {
        var result = PhotoRenamerService.FormatSubSec(raw);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("IMG_4589", "4589")]
    [InlineData("_MG_4589", "4589")]
    [InlineData("DSC_0042", "0042")]
    [InlineData("DSC0123", "0123")]
    [InlineData("IMG_4589_1", "1")]
    [InlineData("4589", "4589")]
    [InlineData("IMG_ABC", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractOriginalNumber_ExtractsTrailingDigits_Accurately(string? baseName, string? expected)
    {
        var result = PhotoRenamerService.ExtractOriginalNumber(baseName);
        result.Should().Be(expected);
    }

    [Fact]
    public void PhotoRenamingMetadata_GeneratesExpectedPattern_WhenValid()
    {
        var meta = new PhotoRenamingMetadata
        {
            CameraModel = "EOS600D",
            DateTimeOriginal = new DateTime(2026, 10, 7, 15, 30, 22),
            SubSecOriginal = "04",
            OriginalNumber = "4589"
        };

        meta.IsValid.Should().BeTrue();
        var root = meta.GenerateRootName();
        root.Should().Be("EOS600D_20261007_153022_04_4589");

        var service = new PhotoRenamerService();
        var rawName = service.GetRenamedFileName(@"C:\SD\DCIM\IMG_4589.CR2", root);
        var jpgName = service.GetRenamedFileName(@"C:\SD\DCIM\IMG_4589.JPG", root);

        rawName.Should().Be("EOS600D_20261007_153022_04_4589.CR2");
        jpgName.Should().Be("EOS600D_20261007_153022_04_4589.JPG");
    }

    [Fact]
    public void PhotoRenamingMetadata_ReturnsNullAndKeepsOriginalName_WhenMetadataMissing()
    {
        var service = new PhotoRenamerService();

        // 1. Missing CameraModel
        var meta1 = new PhotoRenamingMetadata
        {
            CameraModel = null,
            DateTimeOriginal = new DateTime(2026, 10, 7, 15, 30, 22),
            OriginalNumber = "4589"
        };
        meta1.IsValid.Should().BeFalse();
        meta1.GenerateRootName().Should().BeNull();

        // 2. Missing DateTimeOriginal
        var meta2 = new PhotoRenamingMetadata
        {
            CameraModel = "EOS600D",
            DateTimeOriginal = null,
            OriginalNumber = "4589"
        };
        meta2.IsValid.Should().BeFalse();
        meta2.GenerateRootName().Should().BeNull();

        // 3. Missing OriginalNumber
        var meta3 = new PhotoRenamingMetadata
        {
            CameraModel = "EOS600D",
            DateTimeOriginal = new DateTime(2026, 10, 7, 15, 30, 22),
            OriginalNumber = null
        };
        meta3.IsValid.Should().BeFalse();
        meta3.GenerateRootName().Should().BeNull();

        // 4. Fallback on GetRenamedFileName
        var fallbackRaw = service.GetRenamedFileName(@"C:\SD\DCIM\IMG_4589.CR2", null);
        var fallbackJpg = service.GetRenamedFileName(@"C:\SD\DCIM\IMG_4589.JPG", null);

        fallbackRaw.Should().Be("IMG_4589.CR2");
        fallbackJpg.Should().Be("IMG_4589.JPG");
    }

    [Fact]
    public void SubSecOriginal_DefaultsTo00_WhenNullOrEmpty()
    {
        var meta = new PhotoRenamingMetadata
        {
            CameraModel = "EOS100D",
            DateTimeOriginal = new DateTime(2026, 10, 7, 9, 15, 0),
            SubSecOriginal = null, // fallback su "00"
            OriginalNumber = "0042"
        };

        meta.IsValid.Should().BeTrue();
        var root = meta.GenerateRootName();
        root.Should().Be("EOS100D_20261007_091500_00_0042");
    }
}
