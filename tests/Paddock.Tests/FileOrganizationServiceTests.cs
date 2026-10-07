using FluentAssertions;
using Paddock.Core.Models;
using Paddock.Infrastructure.Storage;
using Xunit;

namespace Paddock.Tests;

public class FileOrganizationServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileOrganizationService _service;

    public FileOrganizationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Paddock_OrgTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _service = new FileOrganizationService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData(".CR2", true)]
    [InlineData(".cr3", true)]
    [InlineData(".NEF", true)]
    [InlineData(".ARW", true)]
    [InlineData(".DNG", true)]
    [InlineData(".RAF", true)]
    [InlineData(".jpg", false)]
    [InlineData(".jpeg", false)]
    [InlineData(".PNG", false)]
    public void IsRawFormat_CorrectlyIdentifiesExtensions(string ext, bool expectedRaw)
    {
        _service.IsRawFormat(ext).Should().Be(expectedRaw);
    }

    [Fact]
    public void GetDestinationDirectory_BuildsCorrectTreeStructure()
    {
        // Arrange
        var atleta = new Atleta
        {
            NumeroPettorale = "42",
            Cognome = "Rossi",
            Nome = "Mario"
        };
        var disciplina = new Disciplina { NomeDisciplina = "Slalom Gigante" };

        // Act - JPEG
        var jpegDir = _service.GetDestinationDirectory(_tempRoot, "Coppa del Mondo 2026", atleta, disciplina, ".jpg");
        // Act - RAW
        var rawDir = _service.GetDestinationDirectory(_tempRoot, "Coppa del Mondo 2026", atleta, disciplina, ".cr3");

        // Assert
        jpegDir.Should().EndWith(Path.Combine("Coppa del Mondo 2026", "42_Rossi_Mario", "Slalom Gigante", "Jpeg"));
        rawDir.Should().EndWith(Path.Combine("Coppa del Mondo 2026", "42_Rossi_Mario", "Slalom Gigante", "Raw"));
    }

    [Fact]
    public async Task CopyFileOrganizedAsync_CopiesFileAndCalculatesMd5()
    {
        // Arrange
        var sourceFile = Path.Combine(_tempRoot, "source_sample.jpg");
        var content = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }; // Basic JPEG header
        await File.WriteAllBytesAsync(sourceFile, content);

        var atleta = new Atleta { NumeroPettorale = "7", Nome = "Luca", Cognome = "Bianchi" };
        var disciplina = new Disciplina { NomeDisciplina = "Corsa 100m" };

        // Act
        var (destPath, relPath, md5, size) = await _service.CopyFileOrganizedAsync(
            sourceFile,
            _tempRoot,
            "Meeting Atletica",
            atleta,
            disciplina);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        size.Should().Be(content.Length);
        md5.Should().NotBeNullOrWhiteSpace();
        destPath.Should().Contain(Path.Combine("Meeting Atletica", "7_Bianchi_Luca", "Corsa 100m", "Jpeg"));
        relPath.Should().Be(Path.Combine("Meeting Atletica", "7_Bianchi_Luca", "Corsa 100m", "Jpeg", "source_sample.jpg"));
    }
}

