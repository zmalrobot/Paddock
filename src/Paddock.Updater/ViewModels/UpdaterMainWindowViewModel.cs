using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paddock.Updater.ViewModels;

public partial class UpdaterMainWindowViewModel : ObservableObject
{
    private readonly int _pid;
    private readonly string _downloadUrl;
    private readonly string _targetDir;
    private readonly string _version;
    private readonly string _launchExe;
    private readonly string? _localZipPath;

    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _targetVersion = "0.0.0";

    [ObservableProperty]
    private string _currentStepTitle = "Inizializzazione...";

    [ObservableProperty]
    private string _currentStepDetail = string.Empty;

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private bool _hasError = false;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _canRetry = false;

    public event Action? RequestClose;

    public UpdaterMainWindowViewModel(
        int pid,
        string downloadUrl,
        string targetDir,
        string version,
        string launchExe,
        string? localZipPath = null,
        HttpClient? httpClient = null)
    {
        _pid = pid;
        _downloadUrl = downloadUrl;
        _targetDir = targetDir;
        _version = version;
        _launchExe = launchExe;
        _localZipPath = localZipPath;
        _httpClient = httpClient ?? new HttpClient();

        TargetVersion = !string.IsNullOrWhiteSpace(_version) ? _version : "Nuova Versione";
    }

    public static UpdaterMainWindowViewModel FromArgs(string[] args)
    {
        int pid = 0;
        string downloadUrl = string.Empty;
        string targetDir = AppContext.BaseDirectory;
        string version = string.Empty;
        string launchExe = OperatingSystem.IsWindows() ? "Paddock.UI.exe" : "Paddock.UI";
        string? localZip = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out pid);
            }
            else if (arg.Equals("--download-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                downloadUrl = args[++i];
            }
            else if (arg.Equals("--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                targetDir = args[++i];
            }
            else if (arg.Equals("--version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                version = args[++i];
            }
            else if (arg.Equals("--launch", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                launchExe = args[++i];
            }
            else if (arg.Equals("--zip", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                localZip = args[++i];
            }
        }

        return new UpdaterMainWindowViewModel(pid, downloadUrl, targetDir, version, launchExe, localZip);
    }

    public async Task RunUpdatePipelineAsync()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        HasError = false;
        ErrorMessage = null;
        CanRetry = false;

        string? downloadedZip = null;

        try
        {
            // 1. Attesa chiusura processo Paddock precedente
            CurrentStepTitle = "Chiusura della sessione precedente...";
            CurrentStepDetail = "Rilascio dei file in corso...";
            IsIndeterminate = true;
            await WaitForProcessExitAsync(_pid, token);

            // 2. Download o recupero archivio zip
            if (!string.IsNullOrWhiteSpace(_localZipPath) && File.Exists(_localZipPath))
            {
                downloadedZip = _localZipPath;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_downloadUrl))
                {
                    throw new InvalidOperationException("Nessun URL di download o file ZIP locale specificato.");
                }

                CurrentStepTitle = $"Download di Paddock v{TargetVersion}...";
                IsIndeterminate = false;
                downloadedZip = await DownloadZipAsync(_downloadUrl, token);
            }

            // 3. Estrazione e sovrascrittura file
            CurrentStepTitle = "Estrazione e aggiornamento dei file...";
            CurrentStepDetail = "Installazione della nuova versione...";
            IsIndeterminate = true;
            await ExtractAndApplyZipAsync(downloadedZip, _targetDir, token);

            // 4. Riavvio applicazione
            CurrentStepTitle = "Riavvio di Paddock in corso...";
            CurrentStepDetail = "Aggiornamento completato con successo!";
            IsIndeterminate = false;
            ProgressValue = 100;

            await Task.Delay(800, token);
            RelaunchPaddockAndExit();
        }
        catch (OperationCanceledException)
        {
            CurrentStepTitle = "Aggiornamento annullato.";
            CurrentStepDetail = string.Empty;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Errore durante l'aggiornamento: {ex.Message}";
            CurrentStepTitle = "Aggiornamento non riuscito";
            CurrentStepDetail = "È possibile riprovare o chiudere.";
            CanRetry = true;
            IsIndeterminate = false;
        }
        finally
        {
            // Pulizia file temporaneo se è stato scaricato
            if (downloadedZip != null && downloadedZip != _localZipPath && File.Exists(downloadedZip))
            {
                try { File.Delete(downloadedZip); } catch { }
            }
        }
    }

    private static async Task WaitForProcessExitAsync(int pid, CancellationToken token)
    {
        if (pid <= 0) return;

        try
        {
            var proc = Process.GetProcessById(pid);
            var stopwatch = Stopwatch.StartNew();

            while (!proc.HasExited && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(250, token);
            }

            if (!proc.HasExited)
            {
                proc.Kill();
                await Task.Delay(250, token);
            }
        }
        catch (ArgumentException)
        {
            // Il processo non esiste già più
        }
        catch (InvalidOperationException)
        {
            // Il processo è già terminato
        }
    }

    private async Task<string> DownloadZipAsync(string url, CancellationToken token)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"Paddock_Update_{Guid.NewGuid():N}.zip");

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);

        var buffer = new byte[64 * 1024];
        long totalRead = 0;
        int read;
        var stopwatch = Stopwatch.StartNew();

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), token);
            totalRead += read;

            if (totalBytes > 0)
            {
                var percent = (double)totalRead / totalBytes * 100.0;
                ProgressValue = Math.Min(100, percent);

                var speedMb = totalRead / (1024.0 * 1024.0) / Math.Max(0.1, stopwatch.Elapsed.TotalSeconds);
                var downloadedMb = totalRead / (1024.0 * 1024.0);
                var totalMb = totalBytes / (1024.0 * 1024.0);

                CurrentStepDetail = $"{downloadedMb:F1} MB di {totalMb:F1} MB ({percent:F0}%) • {speedMb:F1} MB/s";
            }
            else
            {
                var downloadedMb = totalRead / (1024.0 * 1024.0);
                CurrentStepDetail = $"{downloadedMb:F1} MB scaricati";
            }
        }

        return tempFile;
    }

    private static async Task ExtractAndApplyZipAsync(string zipPath, string targetDir, CancellationToken token)
    {
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);

            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name))
                {
                    // Cartella
                    var dirPath = Path.Combine(targetDir, entry.FullName);
                    Directory.CreateDirectory(dirPath);
                    continue;
                }

                var destinationPath = Path.Combine(targetDir, entry.FullName);
                var parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                // Retry a 3 tentativi per gestire eventuali ritardi nel rilascio file del SO
                int attempts = 0;
                while (attempts < 3)
                {
                    try
                    {
                        entry.ExtractToFile(destinationPath, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempts < 2)
                    {
                        attempts++;
                        Thread.Sleep(300);
                    }
                }
            }
        }, token);
    }

    private void RelaunchPaddockAndExit()
    {
        var exePath = Path.Combine(_targetDir, _launchExe);
        if (!File.Exists(exePath))
        {
            // Fallback se nome specificato non trovato
            var fallback = OperatingSystem.IsWindows()
                ? Path.Combine(_targetDir, "Paddock.UI.exe")
                : Path.Combine(_targetDir, "Paddock.UI");
            if (File.Exists(fallback))
            {
                exePath = fallback;
            }
        }

        if (File.Exists(exePath))
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _targetDir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        Environment.Exit(0);
    }

    [RelayCommand]
    public async Task RetryAsync()
    {
        await RunUpdatePipelineAsync();
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
        RequestClose?.Invoke();
        Environment.Exit(1);
    }
}
