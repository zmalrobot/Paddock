using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Models;

namespace Paddock.Core.Interfaces;

public class LockContentionEventArgs : EventArgs
{
    public string FilePath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public bool IsRetrying { get; set; }
}

public interface IExcelRepository
{
    string DatabaseFilePath { get; set; }
    event EventHandler<LockContentionEventArgs>? LockContentionDetected;

    Task EnsureDatabaseInitializedAsync(CancellationToken cancellationToken = default);
    Task SwitchDatabaseAsync(string newFilePath, CancellationToken cancellationToken = default);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default);
    Task<string?> GetBasePathAsync(CancellationToken cancellationToken = default);
    Task SetBasePathAsync(string newBasePath, CancellationToken cancellationToken = default);
    Task UpdateAllEventRootsAsync(string newBasePath, CancellationToken cancellationToken = default);

    Task<List<Evento>> GetEventiAsync(CancellationToken cancellationToken = default);
    Task<Evento?> GetEventoByIdAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task UpsertEventoAsync(Evento evento, CancellationToken cancellationToken = default);
    Task DeleteEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<List<Disciplina>> GetDisciplineByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task UpsertDisciplinaAsync(Disciplina disciplina, CancellationToken cancellationToken = default);
    Task DeleteDisciplinaAsync(Guid disciplinaId, CancellationToken cancellationToken = default);

    Task<List<Atleta>> GetAtletiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task UpsertAtletaAsync(Atleta atleta, CancellationToken cancellationToken = default);
    Task DeleteAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default);

    Task<List<Foto>> GetFotoByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task<List<Foto>> GetFotoByAtletaAsync(Guid atletaId, CancellationToken cancellationToken = default);
    Task AddFotoBatchAsync(IEnumerable<Foto> fotoList, CancellationToken cancellationToken = default);
    Task DeleteFotoAsync(Guid fotoId, CancellationToken cancellationToken = default);

    // Catalogo Prezzi
    Task<List<PrezzoCatalogoItem>> GetCatalogoPrezziAsync(CancellationToken cancellationToken = default);
    Task SaveCatalogoPrezziAsync(IEnumerable<PrezzoCatalogoItem> items, CancellationToken cancellationToken = default);
    Task UpsertPrezzoCatalogoItemAsync(PrezzoCatalogoItem item, CancellationToken cancellationToken = default);
    Task DeletePrezzoCatalogoItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    // Acquisti Foto
    Task<List<AcquistoFoto>> GetAcquistiByEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);
    Task UpsertAcquistoFotoAsync(AcquistoFoto acquisto, CancellationToken cancellationToken = default);
    Task DeleteAcquistoFotoAsync(Guid acquistoId, CancellationToken cancellationToken = default);

    // Caricamento Consolidato Single-Pass
    Task<EventDataBundle> GetEventDataBundleAsync(Guid eventoId, CancellationToken cancellationToken = default);
}

public interface IAppPreferencesService
{
    string? LastDatabasePath { get; set; }
    List<string> RecentDatabases { get; }
    bool AutoOpenLastDatabase { get; set; }
    void AddRecentDatabase(string path);

    // Watermark & Metadati di Default
    bool DefaultWatermarkEnabled { get; set; }
    string? DefaultWatermarkImagePath { get; set; }
    float DefaultWatermarkOpacity { get; set; }
    WatermarkPosition DefaultWatermarkPosition { get; set; }
    float DefaultWatermarkScalePercent { get; set; }
    string DefaultPhotographerName { get; set; }
    string DefaultCopyrightNotice { get; set; }
    bool DefaultAutoRotate { get; set; }

    void Load();
    Task LoadAsync();
    Task SaveAsync();
}

public interface IFileOrganizationService
{
    bool IsRawFormat(string extension);
    string GetRelativePhotoPath(string eventName, Atleta atleta, Disciplina disciplina, string extension, string fileName);
    string GetDestinationDirectory(string rootPath, string eventName, Atleta atleta, Disciplina disciplina, string extension);
    string GetPremiazioniRelativePath(string eventName, string fileName);
    string GetPremiazioniDestinationDirectory(string rootPath, string eventName);
    Task<(string destinationPath, string relativePath, string md5Hash, long fileSizeBytes)> CopyFileOrganizedAsync(
        string sourceFilePath,
        string rootPath,
        string eventName,
        Atleta? atleta,
        Disciplina? disciplina,
        string? customFileName = null,
        bool isPremiazione = false,
        CancellationToken cancellationToken = default);

    Task DeleteEventFilesOnDiskAsync(string rootPath, string eventName, CancellationToken cancellationToken = default);
}

public interface IImageProcessingService
{
    Task<bool> AutoRotateImageAsync(string imagePath, CancellationToken cancellationToken = default);
    Task<byte[]> ExtractRawPreviewAsync(string rawFilePath, CancellationToken cancellationToken = default);
    Task ApplyWatermarkAsync(string sourceImagePath, string destinationImagePath, WatermarkOptions options, CancellationToken cancellationToken = default);
    Task<byte[]> GenerateThumbnailAsync(string imagePath, int maxWidth = 260, int maxHeight = 260, CancellationToken cancellationToken = default);
    Task<byte[]> GenerateWatermarkPreviewJpegAsync(
        string? sampleImagePath,
        WatermarkOptions options,
        int previewWidth = 640,
        int previewHeight = 426,
        CancellationToken cancellationToken = default);
}

public interface IMetadataService
{
    bool IsExifToolAvailable { get; }
    string? ExifToolPath { get; set; }
    Task<int?> ExtractOrientationAsync(string filePath, CancellationToken cancellationToken = default);
    Task<DateTime?> ExtractCaptureDateAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> WritePhotographerMetadataAsync(string filePath, string photographerName, string copyright, CancellationToken cancellationToken = default);
}

public class RemovableDriveInfo
{
    public string RootDirectory { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long AvailableFreeSpace { get; set; }
    public string DcimPath { get; set; } = string.Empty;
    public bool HasDcimFolder => !string.IsNullOrEmpty(DcimPath) && Directory.Exists(DcimPath);

    public string DisplayName => string.IsNullOrWhiteSpace(VolumeLabel)
        ? RootDirectory
        : $"{VolumeLabel} ({RootDirectory})";
}

public interface ISdCardWatcherService : IDisposable
{
    event EventHandler<List<RemovableDriveInfo>>? RemovableDrivesChanged;
    List<RemovableDriveInfo> GetCurrentRemovableDrives();
    void StartWatching();
    void StopWatching();
}

public interface IIngestionPipelineService : IDisposable
{
    event EventHandler<IngestionProgressReport>? JobProgressUpdated;
    event EventHandler<IngestionProgressReport>? JobCompleted;
    IReadOnlyDictionary<Guid, IngestionProgressReport> ActiveJobs { get; }

    Task<Guid> EnqueueJobAsync(IngestionJobRequest request, CancellationToken cancellationToken = default);
    void CancelJob(Guid jobId);
    void CancelAllJobs();
}

