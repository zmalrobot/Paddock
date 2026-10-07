using Paddock.Core.Enums;
using Paddock.Core.Models;

namespace Paddock.Core.DTOs;

public class WatermarkOptions
{
    public bool Enabled { get; set; } = false;
    public string? WatermarkImagePath { get; set; }
    public string? WatermarkText { get; set; }
    public float Opacity { get; set; } = 0.65f; // 0.0 - 1.0 (65%)
    public float ScalePercent { get; set; } = 0.20f; // 20% della dimensione dell'immagine
    public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;
    public int MarginPixels { get; set; } = 32;
}

public class MetadataOptions
{
    public bool InjectPhotographer { get; set; } = true;
    public string PhotographerName { get; set; } = string.Empty;
    public string CopyrightNotice { get; set; } = string.Empty;
    public string? ExifToolCustomPath { get; set; }
}

public class IngestionJobRequest
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public string SourceDirectory { get; set; } = string.Empty;
    public string? BasePath { get; set; }
    public Evento EventoTarget { get; set; } = null!;
    public Atleta AtletaTarget { get; set; } = null!;
    public Disciplina DisciplinaTarget { get; set; } = null!;
    public WatermarkOptions Watermark { get; set; } = new();
    public MetadataOptions Metadata { get; set; } = new();
    public bool RecursiveScan { get; set; } = true;
}

public class IngestionProgressReport
{
    public Guid JobId { get; set; }
    public Guid? EventoId { get; set; }
    public Guid? AtletaId { get; set; }
    public IngestionStatus Status { get; set; } = IngestionStatus.Running;
    public string SourceName { get; set; } = string.Empty;
    public string TargetDescription { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public long TotalBytes { get; set; }
    public long ProcessedBytes { get; set; }
    public double ProgressPercentage => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100.0 : 0.0;
    public double SpeedBytesPerSecond { get; set; }
    public double SpeedMegaBytesPerSecond => SpeedBytesPerSecond / (1024.0 * 1024.0);
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public string? CurrentProcessingFile { get; set; }
    public string? StatusMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
    public Exception? Exception { get; set; }
}

