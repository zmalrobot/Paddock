using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Paddock.Core.Interfaces;

namespace Paddock.Infrastructure.Services;

public class GitHubUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private const string GitHubApiUrl = "https://api.github.com/repos/zmalrobot/Paddock/releases";

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Paddock-App", "1.0"));
        }
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(
        string currentVersion,
        bool includePrerelease = true,
        CancellationToken cancellationToken = default)
    {
        var result = new UpdateInfo
        {
            CurrentVersion = currentVersion,
            IsUpdateAvailable = false
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"Errore risposta GitHub: {(int)response.StatusCode} {response.ReasonPhrase}";
                return result;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(
                jsonStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken).ConfigureAwait(false);

            if (releases == null || releases.Count == 0)
            {
                result.ErrorMessage = "Nessun rilascio trovato sul repository GitHub.";
                return result;
            }

            // Filtra solo le release valide (non bozze/draft, e se includePrerelease=false esclude prerelease)
            var candidates = releases
                .Where(r => !r.Draft && (includePrerelease || !r.Prerelease))
                .ToList();

            if (candidates.Count == 0)
            {
                result.ErrorMessage = "Nessun rilascio pubblico disponibile.";
                return result;
            }

            // Trova la release con la versione più alta
            GitHubReleaseDto? latestRelease = null;
            string latestVerStr = "0.0.0";

            foreach (var rel in candidates)
            {
                var relVer = NormalizeVersionString(rel.TagName);
                if (latestRelease == null || CompareVersions(relVer, latestVerStr) > 0)
                {
                    latestRelease = rel;
                    latestVerStr = relVer;
                }
            }

            if (latestRelease == null)
            {
                return result;
            }

            var isNewer = CompareVersions(latestVerStr, currentVersion) > 0;

            result.ReleaseTag = latestRelease.TagName;
            result.NewVersion = latestVerStr;
            result.ReleaseName = !string.IsNullOrWhiteSpace(latestRelease.Name) ? latestRelease.Name : latestRelease.TagName;
            result.ReleaseNotes = latestRelease.Body ?? string.Empty;
            result.PublishedAt = latestRelease.PublishedAt;
            result.IsPrerelease = latestRelease.Prerelease;
            result.IsUpdateAvailable = isNewer;

            // Individua l'asset zip compatibile con la piattaforma corrente
            var expectedAssetKeyword = OperatingSystem.IsWindows() ? "windows-x64" : "linux-x64";
            var matchingAsset = latestRelease.Assets?
                .FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                     a.Name.Contains(expectedAssetKeyword, StringComparison.OrdinalIgnoreCase));

            if (matchingAsset != null)
            {
                result.DownloadUrl = matchingAsset.BrowserDownloadUrl;
                result.AssetFileName = matchingAsset.Name;
                result.AssetSizeBytes = matchingAsset.Size;
            }
            else if (latestRelease.Assets != null && latestRelease.Assets.Count > 0)
            {
                // Fallback su primo archivio zip
                var fallbackZip = latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                if (fallbackZip != null)
                {
                    result.DownloadUrl = fallbackZip.BrowserDownloadUrl;
                    result.AssetFileName = fallbackZip.Name;
                    result.AssetSizeBytes = fallbackZip.Size;
                }
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Controllo aggiornamenti annullato.";
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Impossibile verificare gli aggiornamenti: {ex.Message}";
            return result;
        }
    }

    public async Task LaunchUpdaterAndExitAsync(
        UpdateInfo updateInfo,
        string targetAppDir,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
        {
            throw new InvalidOperationException("URL di download dell'aggiornamento non disponibile.");
        }

        var updaterFileName = OperatingSystem.IsWindows() ? "Paddock.Updater.exe" : "Paddock.Updater";
        var mainExeFileName = OperatingSystem.IsWindows() ? "Paddock.UI.exe" : "Paddock.UI";

        var updaterSourcePath = Path.Combine(targetAppDir, updaterFileName);

        // Directory di staging temporanea per evitare che Paddock.Updater si blocchi su disco
        var tempStagingDir = Path.Combine(Path.GetTempPath(), "Paddock_Updater_Staging");
        Directory.CreateDirectory(tempStagingDir);
        var stagedUpdaterPath = Path.Combine(tempStagingDir, updaterFileName);

        if (File.Exists(updaterSourcePath))
        {
            File.Copy(updaterSourcePath, stagedUpdaterPath, overwrite: true);

            // Copia eventuali file di runtime condivisi nella stessa directory se non single-file
            foreach (var file in Directory.EnumerateFiles(targetAppDir, "*.dll"))
            {
                var dest = Path.Combine(tempStagingDir, Path.GetFileName(file));
                try { File.Copy(file, dest, overwrite: true); } catch { }
            }
        }
        else if (!File.Exists(stagedUpdaterPath))
        {
            throw new FileNotFoundException($"L'eseguibile dell'utility di aggiornamento '{updaterFileName}' non è stato trovato in '{targetAppDir}'.");
        }

        var arguments = $"--pid {Environment.ProcessId} " +
                        $"--download-url \"{updateInfo.DownloadUrl}\" " +
                        $"--target \"{targetAppDir}\" " +
                        $"--version \"{updateInfo.NewVersion}\" " +
                        $"--launch \"{mainExeFileName}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = stagedUpdaterPath,
            Arguments = arguments,
            UseShellExecute = true
        };

        Process.Start(startInfo);

        // Chiude l'applicazione Paddock per rilasciare i file-lock di Windows
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        Environment.Exit(0);
    }

    public static string NormalizeVersionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0.0.0";
        var trimmed = raw.Trim().TrimStart('v', 'V');
        var plusIndex = trimmed.IndexOf('+');
        if (plusIndex >= 0) trimmed = trimmed[..plusIndex];
        return trimmed;
    }

    public static int CompareVersions(string v1, string v2)
    {
        var norm1 = NormalizeVersionString(v1);
        var norm2 = NormalizeVersionString(v2);

        var (ver1, pre1) = SplitVersionAndPre(norm1);
        var (ver2, pre2) = SplitVersionAndPre(norm2);

        var cmp = ver1.CompareTo(ver2);
        if (cmp != 0) return cmp;

        if (string.IsNullOrEmpty(pre1) && !string.IsNullOrEmpty(pre2)) return 1;
        if (!string.IsNullOrEmpty(pre1) && string.IsNullOrEmpty(pre2)) return -1;

        return string.Compare(pre1, pre2, StringComparison.OrdinalIgnoreCase);
    }

    private static (Version Version, string PreRelease) SplitVersionAndPre(string str)
    {
        var dashIndex = str.IndexOf('-');
        var pre = string.Empty;
        var verPart = str;

        if (dashIndex >= 0)
        {
            verPart = str[..dashIndex];
            pre = str[(dashIndex + 1)..];
        }

        var parts = verPart.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out var maj) ? maj : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out var bld) ? bld : 0;

        return (new Version(major, minor, build), pre);
    }

    public class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto>? Assets { get; set; }
    }

    public class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
