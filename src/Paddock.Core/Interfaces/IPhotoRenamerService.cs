using System;
using System.Collections.Generic;

namespace Paddock.Core.Interfaces;

public class PhotoRenamingMetadata
{
    public string? CameraModel { get; set; }
    public DateTime? DateTimeOriginal { get; set; }
    public string? SubSecOriginal { get; set; }
    public string? OriginalNumber { get; set; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(CameraModel) &&
        DateTimeOriginal.HasValue &&
        !string.IsNullOrWhiteSpace(OriginalNumber);

    public string? GenerateRootName()
    {
        if (!IsValid) return null;
        var dateStr = DateTimeOriginal!.Value.ToString("yyyyMMdd_HHmmss");
        var subSecStr = !string.IsNullOrWhiteSpace(SubSecOriginal) ? SubSecOriginal : "00";
        return $"{CameraModel}_{dateStr}_{subSecStr}_{OriginalNumber}";
    }
}

public interface IPhotoRenamerService
{
    PhotoRenamingMetadata? ExtractMetadata(string filePath);
    string? ComputeRenamedRoot(IEnumerable<string> fileGroup);
    string GetRenamedFileName(string sourceFilePath, string? rootName);
}
