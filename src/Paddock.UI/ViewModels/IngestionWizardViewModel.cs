using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class IngestionWizardViewModel : ViewModelBase
{
    private readonly ISdCardWatcherService _sdCardWatcher;
    private readonly IAppPreferencesService? _prefsService;
    private readonly IImageProcessingService? _imageService;

    public Evento Evento { get; }

    [ObservableProperty]
    private string _sourceDirectory = string.Empty;

    [ObservableProperty]
    private Atleta? _selectedAtleta;

    [ObservableProperty]
    private Disciplina? _selectedDisciplina;

    [ObservableProperty]
    private bool _watermarkEnabled = false;

    [ObservableProperty]
    private string? _watermarkImagePath;

    [ObservableProperty]
    private float _watermarkOpacity = 0.65f;

    [ObservableProperty]
    private WatermarkPosition _watermarkPosition = WatermarkPosition.BottomRight;

    [ObservableProperty]
    private float _watermarkScalePercent = 0.20f;

    [ObservableProperty]
    private Bitmap? _watermarkPreviewBitmap;

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private bool _injectMetadata = true;

    [ObservableProperty]
    private string _photographerName = string.Empty;

    [ObservableProperty]
    private string _copyrightNotice = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<Atleta> Atleti { get; } = new();
    public ObservableCollection<Disciplina> Discipline { get; } = new();
    public ObservableCollection<RemovableDriveInfo> DetectedDrives { get; } = new();
    public List<WatermarkPosition> AvailablePositions { get; } = Enum.GetValues<WatermarkPosition>().ToList();

    public event Action<IngestionJobRequest?>? RequestClose;

    public IngestionWizardViewModel(
        Evento evento,
        IEnumerable<Atleta> atleti,
        IEnumerable<Disciplina> discipline,
        ISdCardWatcherService sdCardWatcher,
        IAppPreferencesService? prefsService = null,
        IImageProcessingService? imageService = null)
    {
        Evento = evento;
        _sdCardWatcher = sdCardWatcher;
        _prefsService = prefsService;
        _imageService = imageService;

        foreach (var a in atleti) Atleti.Add(a);
        foreach (var d in discipline) Discipline.Add(d);

        SelectedAtleta = Atleti.FirstOrDefault();
        SelectedDisciplina = Discipline.FirstOrDefault();

        if (_prefsService != null)
        {
            WatermarkEnabled = _prefsService.DefaultWatermarkEnabled;
            WatermarkImagePath = _prefsService.DefaultWatermarkImagePath;
            WatermarkOpacity = _prefsService.DefaultWatermarkOpacity > 0 ? _prefsService.DefaultWatermarkOpacity : 0.65f;
            WatermarkPosition = _prefsService.DefaultWatermarkPosition;
            WatermarkScalePercent = _prefsService.DefaultWatermarkScalePercent > 0 ? _prefsService.DefaultWatermarkScalePercent : 0.20f;
            PhotographerName = _prefsService.DefaultPhotographerName ?? string.Empty;
            CopyrightNotice = _prefsService.DefaultCopyrightNotice ?? string.Empty;
        }

        RefreshDrives();
        _sdCardWatcher.RemovableDrivesChanged += (s, drives) => RefreshDrives();

        _ = UpdateWatermarkPreviewAsync();
    }

    private void RefreshDrives()
    {
        DetectedDrives.Clear();
        var drives = _sdCardWatcher.GetCurrentRemovableDrives();
        if (drives != null)
        {
            foreach (var d in drives)
            {
                DetectedDrives.Add(d);
            }
        }

        if (string.IsNullOrEmpty(SourceDirectory) && DetectedDrives.Count > 0)
        {
            SourceDirectory = DetectedDrives[0].DcimPath;
        }
    }

    [RelayCommand]
    private void SelectDrive(RemovableDriveInfo drive)
    {
        SourceDirectory = drive.DcimPath;
    }

    public async Task UpdateWatermarkPreviewAsync()
    {
        if (_imageService == null) return;

        try
        {
            IsPreviewLoading = true;
            var options = new WatermarkOptions
            {
                Enabled = WatermarkEnabled,
                WatermarkImagePath = WatermarkImagePath,
                Opacity = WatermarkOpacity,
                Position = WatermarkPosition,
                ScalePercent = WatermarkScalePercent
            };

            string? samplePhotoPath = null;
            if (!string.IsNullOrWhiteSpace(SourceDirectory) && Directory.Exists(SourceDirectory))
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png" };
                try
                {
                    samplePhotoPath = Directory.EnumerateFiles(SourceDirectory, "*.*", SearchOption.AllDirectories)
                        .FirstOrDefault(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                }
                catch
                {
                    // Fallback to null
                }
            }

            var bytes = await _imageService.GenerateWatermarkPreviewJpegAsync(samplePhotoPath, options);
            if (bytes != null && bytes.Length > 0)
            {
                using var ms = new MemoryStream(bytes);
                WatermarkPreviewBitmap = new Bitmap(ms);
            }
        }
        catch
        {
            // Ignora errori rendering anteprima
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    partial void OnWatermarkEnabledChanged(bool value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnWatermarkImagePathChanged(string? value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnWatermarkOpacityChanged(float value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnWatermarkPositionChanged(WatermarkPosition value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnWatermarkScalePercentChanged(float value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnSourceDirectoryChanged(string value) => _ = UpdateWatermarkPreviewAsync();

    [RelayCommand]
    private void StartIngestion()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory) || !Directory.Exists(SourceDirectory))
        {
            ErrorMessage = "Seleziona una cartella sorgente o scheda SD valida.";
            return;
        }

        if (SelectedAtleta == null)
        {
            ErrorMessage = "Seleziona l'atleta target per questa importazione.";
            return;
        }

        if (SelectedDisciplina == null)
        {
            ErrorMessage = "Seleziona la disciplina sportiva target.";
            return;
        }

        var request = new IngestionJobRequest
        {
            SourceDirectory = SourceDirectory,
            EventoTarget = Evento,
            AtletaTarget = SelectedAtleta,
            DisciplinaTarget = SelectedDisciplina,
            Watermark = new WatermarkOptions
            {
                Enabled = WatermarkEnabled,
                WatermarkImagePath = WatermarkImagePath,
                Opacity = WatermarkOpacity,
                Position = WatermarkPosition,
                ScalePercent = WatermarkScalePercent
            },
            Metadata = new MetadataOptions
            {
                InjectPhotographer = InjectMetadata,
                PhotographerName = PhotographerName.Trim(),
                CopyrightNotice = CopyrightNotice.Trim()
            }
        };

        RequestClose?.Invoke(request);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(null);
    }
}
