using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Paddock.Core.Interfaces;

namespace Paddock.Infrastructure.Metadata;

public class PhotoRenamerService : IPhotoRenamerService
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".rw2", ".orf", ".pef", ".srw"
    };

    public PhotoRenamingMetadata? ExtractMetadata(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var baseName = Path.GetFileNameWithoutExtension(filePath);
        var originalNumber = ExtractOriginalNumber(baseName);
        if (string.IsNullOrWhiteSpace(originalNumber))
        {
            // Metadato obbligatorio mancante (cifre finali nel nome originale)
            return null;
        }

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // 1. CameraModel: da Exif.Image.Model (rimuovi "Canon" e spazi)
            var ifd0Dir = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var rawModel = ifd0Dir?.GetString(ExifDirectoryBase.TagModel);
            if (string.IsNullOrWhiteSpace(rawModel))
            {
                var modelTag = directories.SelectMany(d => d.Tags)
                    .FirstOrDefault(t => t.Type == ExifDirectoryBase.TagModel);
                rawModel = modelTag?.Description;
            }

            var cleanedModel = CleanCameraModel(rawModel);
            if (string.IsNullOrWhiteSpace(cleanedModel))
            {
                return null;
            }

            // 2. Data/Ora: da Exif.Photo.DateTimeOriginal
            var subIfdDir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            DateTime? dtOriginal = null;

            if (subIfdDir != null && subIfdDir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var parsedDt))
            {
                dtOriginal = parsedDt;
            }
            else
            {
                var dtTag = directories.SelectMany(d => d.Tags)
                    .FirstOrDefault(t => t.Type == ExifDirectoryBase.TagDateTimeOriginal);

                if (dtTag != null && !string.IsNullOrWhiteSpace(dtTag.Description))
                {
                    if (DateTime.TryParseExact(dtTag.Description, new[]
                        {
                            "yyyy:MM:dd HH:mm:ss",
                            "yyyy-MM-dd HH:mm:ss",
                            "yyyy/MM/dd HH:mm:ss"
                        },
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dtParsed))
                    {
                        dtOriginal = dtParsed;
                    }
                }
            }

            if (!dtOriginal.HasValue)
            {
                return null;
            }

            // 3. SubSec: da Exif.Photo.SubSecTimeOriginal (fallback "00")
            var rawSubSec = subIfdDir?.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal);
            if (string.IsNullOrWhiteSpace(rawSubSec))
            {
                var subSecTag = directories.SelectMany(d => d.Tags)
                    .FirstOrDefault(t => t.Type == ExifDirectoryBase.TagSubsecondTimeOriginal);
                rawSubSec = subSecTag?.Description;
            }

            var subSec = FormatSubSec(rawSubSec);

            return new PhotoRenamingMetadata
            {
                CameraModel = cleanedModel,
                DateTimeOriginal = dtOriginal,
                SubSecOriginal = subSec,
                OriginalNumber = originalNumber
            };
        }
        catch
        {
            // Se ci sono errori durante la lettura, ritorna null per mantenere il nome originale
            return null;
        }
    }

    public string? ComputeRenamedRoot(IEnumerable<string> fileGroup)
    {
        if (fileGroup == null) return null;

        var list = fileGroup.ToList();
        if (list.Count == 0) return null;

        // Ordina privilegiando i file non-RAW (JPEG) per massima velocità e affidabilità EXIF standard
        var ordered = list.OrderBy(f => IsRawExtension(Path.GetExtension(f)) ? 1 : 0);

        foreach (var file in ordered)
        {
            try
            {
                var meta = ExtractMetadata(file);
                if (meta != null && meta.IsValid)
                {
                    return meta.GenerateRootName();
                }
            }
            catch
            {
                // Fallback sul prossimo file del gruppo
            }
        }

        return null;
    }

    public string GetRenamedFileName(string sourceFilePath, string? rootName)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            return string.Empty;

        var originalFileName = Path.GetFileName(sourceFilePath);
        if (string.IsNullOrWhiteSpace(rootName))
        {
            return originalFileName;
        }

        var ext = Path.GetExtension(sourceFilePath);
        return $"{rootName}{ext}";
    }

    public static string? CleanCameraModel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Rimuovi "Canon" (case-insensitive)
        var withoutCanon = Regex.Replace(raw, "Canon", "", RegexOptions.IgnoreCase);

        // Rimuovi tutti gli spazi bianchi
        var noSpaces = Regex.Replace(withoutCanon, @"\s+", "");

        return string.IsNullOrWhiteSpace(noSpaces) ? null : noSpaces;
    }

    public static string FormatSubSec(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "00";

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) return "00";

        if (digits.Length == 1)
        {
            return digits.PadLeft(2, '0'); // Es. "4" -> "04"
        }

        return digits.Substring(0, 2); // Es. "04", o "420" -> "42"
    }

    public static string? ExtractOriginalNumber(string? baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return null;

        var match = Regex.Match(baseName, @"(\d+)$");
        return match.Success ? match.Value : null;
    }

    private static bool IsRawExtension(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return false;
        var formatted = ext.StartsWith('.') ? ext : "." + ext;
        return RawExtensions.Contains(formatted);
    }
}
