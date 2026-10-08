using System.Security.Cryptography;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.Infrastructure.Storage;

public class FileOrganizationService : IFileOrganizationService
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".rw2", ".orf", ".pef", ".srw"
    };

    public bool IsRawFormat(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return RawExtensions.Contains(ext);
    }

    public string GetRelativePhotoPath(string eventName, Atleta atleta, Disciplina disciplina, string extension, string fileName)
    {
        var sanitizedEvent = SanitizeFolderName(eventName);
        var atletaFolder = atleta.NomeCartellaSanitizzato;
        var discFolder = SanitizeFolderName(string.IsNullOrWhiteSpace(disciplina.NomeDisciplina) ? "Generale" : disciplina.NomeDisciplina);
        var subFolder = IsRawFormat(extension) ? "Raw" : "Jpeg";

        return Path.Combine(sanitizedEvent, atletaFolder, discFolder, subFolder, fileName);
    }

    public string GetDestinationDirectory(string rootPath, string eventName, Atleta atleta, Disciplina disciplina, string extension)
    {
        var sanitizedEvent = SanitizeFolderName(eventName);
        var atletaFolder = atleta.NomeCartellaSanitizzato;
        var discFolder = SanitizeFolderName(string.IsNullOrWhiteSpace(disciplina.NomeDisciplina) ? "Generale" : disciplina.NomeDisciplina);
        var subFolder = IsRawFormat(extension) ? "Raw" : "Jpeg";

        return Path.Combine(rootPath, sanitizedEvent, atletaFolder, discFolder, subFolder);
    }

    public string GetPremiazioniRelativePath(string eventName, string fileName)
    {
        var sanitizedEvent = SanitizeFolderName(eventName);
        return Path.Combine(sanitizedEvent, "Premiazioni", fileName);
    }

    public string GetPremiazioniDestinationDirectory(string rootPath, string eventName)
    {
        var sanitizedEvent = SanitizeFolderName(eventName);
        return Path.Combine(rootPath, sanitizedEvent, "Premiazioni");
    }

    public async Task<(string destinationPath, string relativePath, string md5Hash, long fileSizeBytes)> CopyFileOrganizedAsync(
        string sourceFilePath,
        string rootPath,
        string eventName,
        Atleta? atleta,
        Disciplina? disciplina,
        string? customFileName = null,
        bool isPremiazione = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("File sorgente non trovato", sourceFilePath);
        }

        var ext = Path.GetExtension(sourceFilePath);
        var fileName = !string.IsNullOrWhiteSpace(customFileName) ? customFileName : Path.GetFileName(sourceFilePath);
        var targetDir = isPremiazione
            ? GetPremiazioniDestinationDirectory(rootPath, eventName)
            : GetDestinationDirectory(rootPath, eventName, atleta!, disciplina!, ext);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var targetFilePath = Path.Combine(targetDir, fileName);

        // Se esiste già un file omonimo con dimensione o hash differente, rinomina con suffisso
        if (File.Exists(targetFilePath))
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var counter = 1;
            while (File.Exists(targetFilePath))
            {
                var candidate = $"{baseName}_{counter}{ext}";
                targetFilePath = Path.Combine(targetDir, candidate);
                counter++;
            }
        }

        // Calcolo del percorso relativo puro conforme
        var finalFileName = Path.GetFileName(targetFilePath);
        var relativePath = isPremiazione
            ? GetPremiazioniRelativePath(eventName, finalFileName)
            : GetRelativePhotoPath(eventName, atleta!, disciplina!, ext, finalFileName);

        // Copia asincrona con calcolo contestuale dell'MD5
        string md5Hash;
        long totalBytes;

        using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 128 * 1024, useAsync: true))
        using (var targetStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 128 * 1024, useAsync: true))
        using (var md5 = MD5.Create())
        using (var cryptoStream = new CryptoStream(targetStream, md5, CryptoStreamMode.Write))
        {
            await sourceStream.CopyToAsync(cryptoStream, cancellationToken).ConfigureAwait(false);
            await cryptoStream.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
            
            md5Hash = Convert.ToHexString(md5.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
            totalBytes = targetStream.Position;
        }

        return (targetFilePath, relativePath, md5Hash, totalBytes);
    }

    public Task DeleteEventFilesOnDiskAsync(string rootPath, string eventName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var sanitizedEvent = SanitizeFolderName(eventName);
            var targetDir = Path.Combine(rootPath, sanitizedEvent);

            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
        }, cancellationToken);
    }

    public string SanitizeFolderName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Generale";
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(raw.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Generale" : sanitized;
    }
}

