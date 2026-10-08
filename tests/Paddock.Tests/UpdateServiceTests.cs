using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Paddock.Core.Interfaces;
using Paddock.Infrastructure.Services;
using Paddock.UI.ViewModels;
using Paddock.Updater.ViewModels;
using Xunit;

namespace Paddock.Tests;

public class UpdateServiceTests
{
    private const string SampleReleasesJson = """
    [
      {
        "tag_name": "v0.5.1",
        "name": "Paddock v0.5.1 (Pre-release)",
        "body": "Novità versione 0.5.1: correzioni bug e miglioramenti.",
        "draft": false,
        "prerelease": true,
        "published_at": "2026-10-08T12:00:00Z",
        "assets": [
          {
            "name": "Paddock-0.5.1-windows-x64.zip",
            "size": 75000000,
            "browser_download_url": "https://github.com/zmalrobot/Paddock/releases/download/v0.5.1/Paddock-0.5.1-windows-x64.zip"
          },
          {
            "name": "Paddock-0.5.1-linux-x64.zip",
            "size": 45000000,
            "browser_download_url": "https://github.com/zmalrobot/Paddock/releases/download/v0.5.1/Paddock-0.5.1-linux-x64.zip"
          }
        ]
      },
      {
        "tag_name": "v0.5.0",
        "name": "Paddock v0.5.0",
        "body": "Versione iniziale 0.5.0.",
        "draft": false,
        "prerelease": false,
        "published_at": "2026-10-07T16:00:00Z",
        "assets": [
          {
            "name": "Paddock-0.5.0-windows-x64.zip",
            "size": 76900000,
            "browser_download_url": "https://github.com/zmalrobot/Paddock/releases/download/v0.5.0/Paddock-0.5.0-windows-x64.zip"
          }
        ]
      }
    ]
    """;

    [Theory]
    [InlineData("0.5.1", "0.5.0", 1)]
    [InlineData("0.5.0", "0.5.1", -1)]
    [InlineData("v0.5.0", "0.5.0", 0)]
    [InlineData("V0.5.0", "0.5.0", 0)]
    [InlineData("0.6.0", "0.5.9", 1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    [InlineData("0.5.0", "0.5.0-rc1", 1)]
    [InlineData("0.5.0+build123", "0.5.0", 0)]
    public void CompareVersions_OrdersSemVerCorrectly(string v1, string v2, int expectedSign)
    {
        var result = GitHubUpdateService.CompareVersions(v1, v2);
        Math.Sign(result).Should().Be(expectedSign);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNewerReleaseExists_ReturnsUpdateAvailableTrue()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(SampleReleasesJson)
            });

        using var client = new HttpClient(mockHandler.Object);
        var service = new GitHubUpdateService(client);

        var updateInfo = await service.CheckForUpdatesAsync("0.5.0", includePrerelease: true);

        updateInfo.IsUpdateAvailable.Should().BeTrue();
        updateInfo.NewVersion.Should().Be("0.5.1");
        updateInfo.ReleaseTag.Should().Be("v0.5.1");
        updateInfo.DownloadUrl.Should().NotBeNullOrWhiteSpace();
        updateInfo.AssetFileName.Should().Contain(".zip");
        updateInfo.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenCurrentVersionIsLatest_ReturnsUpdateAvailableFalse()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(SampleReleasesJson)
            });

        using var client = new HttpClient(mockHandler.Object);
        var service = new GitHubUpdateService(client);

        var updateInfo = await service.CheckForUpdatesAsync("0.5.1", includePrerelease: true);

        updateInfo.IsUpdateAvailable.Should().BeFalse();
        updateInfo.NewVersion.Should().Be("0.5.1");
        updateInfo.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNetworkFails_ReturnsSafeErrorMessageWithoutCrashing()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Rete non disponibile"));

        using var client = new HttpClient(mockHandler.Object);
        var service = new GitHubUpdateService(client);

        var updateInfo = await service.CheckForUpdatesAsync("0.5.0");

        updateInfo.IsUpdateAvailable.Should().BeFalse();
        updateInfo.ErrorMessage.Should().NotBeNull();
        updateInfo.ErrorMessage.Should().Contain("Rete non disponibile");
    }

    [Fact]
    public async Task SettingsViewModel_CheckForUpdatesCommand_UpdatesStateCorrectly()
    {
        var mockRepo = new Mock<IExcelRepository>();
        var mockPrefs = new Mock<IAppPreferencesService>();
        mockPrefs.Setup(p => p.CheckUpdatesOnStartup).Returns(true);

        var mockUpdateService = new Mock<IUpdateService>();
        mockUpdateService.Setup(s => s.CheckForUpdatesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateInfo
            {
                CurrentVersion = "0.5.0",
                NewVersion = "0.5.1",
                ReleaseName = "Paddock v0.5.1",
                ReleaseNotes = "Bugfix",
                IsUpdateAvailable = true,
                DownloadUrl = "https://example.com/paddock.zip"
            });

        var vm = new SettingsViewModel(mockRepo.Object, mockPrefs.Object, null, mockUpdateService.Object);

        vm.IsUpdateAvailable.Should().BeFalse();
        vm.UpdateStatusMessage.Should().BeNull();

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        vm.IsUpdateAvailable.Should().BeTrue();
        vm.AvailableUpdateInfo.Should().NotBeNull();
        vm.AvailableUpdateInfo!.NewVersion.Should().Be("0.5.1");
        vm.UpdateStatusMessage.Should().Contain("0.5.1");
        vm.UpdateStatusIsError.Should().BeFalse();
        vm.UpdateStatusIsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SettingsViewModel_CheckUpdatesOnStartup_PersistsToPreferences()
    {
        var mockRepo = new Mock<IExcelRepository>();
        var mockPrefs = new Mock<IAppPreferencesService>();
        mockPrefs.SetupProperty(p => p.CheckUpdatesOnStartup, true);

        var vm = new SettingsViewModel(mockRepo.Object, mockPrefs.Object);
        vm.CheckUpdatesOnStartup.Should().BeTrue();

        vm.CheckUpdatesOnStartup = false;

        mockPrefs.VerifySet(p => p.CheckUpdatesOnStartup = false, Times.Once);
        mockPrefs.Verify(p => p.SaveAsync(), Times.Once);
    }

    [Fact]
    public void UpdaterMainWindowViewModel_FromArgs_ParsesAllArgumentsCorrectly()
    {
        var args = new[]
        {
            "--pid", "4567",
            "--download-url", "https://example.com/download.zip",
            "--target", @"C:\PaddockApp",
            "--version", "0.5.2",
            "--launch", "Paddock.UI.exe",
            "--zip", @"C:\temp\local.zip"
        };

        var vm = UpdaterMainWindowViewModel.FromArgs(args);

        vm.TargetVersion.Should().Be("0.5.2");
        vm.HasError.Should().BeFalse();
        vm.ProgressValue.Should().Be(0);
    }

    [Fact]
    public void UpdaterMainWindowViewModel_FromArgs_HandlesWindowsTrailingBackslashEscapedArgs_RecoversTargetAndVersion()
    {
        // Simula l'effetto del parsing Windows CLI quando --target "C:\App\" ha il trailing backslash che fa l'escape delle virgolette:
        // Windows produce un token 'C:\App" --version ' e il valore successivo '0.5.7'
        var args = new[]
        {
            "--pid", "1234",
            "--download-url", "https://example.com/download.zip",
            "--target", @"C:\PaddockApp\"" --version ",
            "0.5.7",
            "--launch", "Paddock.UI.exe"
        };

        var vm = UpdaterMainWindowViewModel.FromArgs(args);

        // Deve aver recuperato la versione correttamente
        vm.TargetVersion.Should().Be("0.5.7");
        vm.HasError.Should().BeFalse();
    }

    [Fact]
    public void UpdaterMainWindowViewModel_FromArgs_CleansTrailingSlashesAndQuotes()
    {
        var args = new[]
        {
            "--pid", "100",
            "--target", "\"C:\\PaddockPortable\\\"",
            "--version", "\"0.5.7\""
        };

        var vm = UpdaterMainWindowViewModel.FromArgs(args);

        vm.TargetVersion.Should().Be("0.5.7");
        vm.HasError.Should().BeFalse();
    }
}
