using System.Diagnostics;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Paddock.Core.Interfaces;

namespace Paddock.Infrastructure.Metadata;

public class ExifToolMetadataService : IMetadataService
{
    private string? _customExifToolPath;
    private string? _resolvedExifToolPath;
    private bool? _isAvailable;

    public string? ExifToolPath
    {
        get => _customExifToolPath ?? _resolvedExifToolPath;
        set
        {
            _customExifToolPath = value;
            _isAvailable = null; // Forza ricalcolo
            ResolveExifToolExecutable();
        }
    }

    public bool IsExifToolAvailable
    {
        get
        {
            if (!_isAvailable.HasValue)
            {
                ResolveExifToolExecutable();
            }
            return _isAvailable ?? false;
        }
    }

    public ExifToolMetadataService()
    {
        ResolveExifToolExecutable();
    }

    private void ResolveExifToolExecutable()
    {
        if (!string.IsNullOrEmpty(_customExifToolPath) && File.Exists(_customExifToolPath))
        {
            _resolvedExifToolPath = _customExifToolPath;
            _isAvailable = true;
            return;
        }

        // Cerca nella cartella dell'applicazione
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localExifToolWin = Path.Combine(appDir, "exiftool.exe");
        var localExifToolUnix = Path.Combine(appDir, "exiftool");

        if (File.Exists(localExifToolWin))
        {
            _resolvedExifToolPath = localExifToolWin;
            _isAvailable = true;
            return;
        }
        if (File.Exists(localExifToolUnix))
        {
            _resolvedExifToolPath = localExifToolUnix;
            _isAvailable = true;
            return;
        }

        // Cerca nel PATH di sistema
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var candWin = Path.Combine(p.Trim(), "exiftool.exe");
            var candUnix = Path.Combine(p.Trim(), "exiftool");
            if (File.Exists(candWin))
            {
                _resolvedExifToolPath = candWin;
                _isAvailable = true;
                return;
            }
            if (File.Exists(candUnix))
            {
                _resolvedExifToolPath = candUnix;
                _isAvailable = true;
                return;
            }
        }

        _resolvedExifToolPath = null;
        _isAvailable = false;
    }

    public async Task<DateTime?> ExtractCaptureDateAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return null;

        return await Task.Run(() =>
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (subIfdDirectory != null && subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateOriginal))
                {
                    return (DateTime?)dateOriginal;
                }

                var ifd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                if (ifd0Directory != null && ifd0Directory.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var date))
                {
                    return (DateTime?)date;
                }
            }
            catch
            {
                // Fallback: se la lettura metadati fallisce o formato non supportato da ImageMetadataReader
            }

            try
            {
                return File.GetLastWriteTime(filePath);
            }
            catch
            {
                return null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> WritePhotographerMetadataAsync(
        string filePath,
        string photographerName,
        string copyright,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(photographerName)) return true;
        if (!File.Exists(filePath)) return false;

        if (!IsExifToolAvailable || string.IsNullOrEmpty(_resolvedExifToolPath))
        {
            // ExifTool non disponibile sul sistema host
            return false;
        }

        return await Task.Run(async () =>
        {
            try
            {
                var copyrightText = string.IsNullOrWhiteSpace(copyright)
                    ? $"Copyright (c) {DateTime.Now.Year} {photographerName}"
                    : copyright;

                var psi = new ProcessStartInfo
                {
                    FileName = _resolvedExifToolPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // Parametri safe per scrittura in-place senza creare duplicati .original
                psi.ArgumentList.Add("-overwrite_original");
                psi.ArgumentList.Add($"-Artist={photographerName}");
                psi.ArgumentList.Add($"-by-line={photographerName}");
                psi.ArgumentList.Add($"-XMP:Creator={photographerName}");
                psi.ArgumentList.Add($"-Copyright={copyrightText}");
                psi.ArgumentList.Add($"-XMP:Rights={copyrightText}");
                psi.ArgumentList.Add(filePath);

                using var process = Process.Start(psi);
                if (process == null) return false;

                using var registration = cancellationToken.Register(() =>
                {
                    try { process.Kill(); } catch { }
                });

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}

