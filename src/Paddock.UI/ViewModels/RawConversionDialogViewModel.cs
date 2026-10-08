using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class RawCandidateItemViewModel : ObservableObject
{
    public string RawFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string BaseFileName { get; set; } = string.Empty;
    public Disciplina Disciplina { get; set; } = null!;
    public Foto? FotoEntity { get; set; }
    public long FileSizeBytes { get; set; }
    public string DisplaySize => FormatBytes(FileSizeBytes);

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private Bitmap? _thumbnailBitmap;

    [ObservableProperty]
    private bool _isThumbnailLoading;

    public async Task LoadThumbnailAsync(IImageProcessingService? imageService)
    {
        if (ThumbnailBitmap != null || imageService == null || !File.Exists(RawFilePath))
        {
            return;
        }

        try
        {
            IsThumbnailLoading = true;
            var thumbBytes = await imageService.GenerateThumbnailAsync(RawFilePath, 120, 120).ConfigureAwait(false);
            if (thumbBytes != null && thumbBytes.Length > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream(thumbBytes);
                        ThumbnailBitmap = new Bitmap(ms);
                    }
                    catch
                    {
                        // Fallback silenzioso
                    }
                });
            }
        }
        catch
        {
            // Ignora errori generazione anteprima
        }
        finally
        {
            IsThumbnailLoading = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}

public partial class RawConversionDialogViewModel : ViewModelBase
{
    private readonly IExcelRepository _excelRepo;
    private readonly IFileOrganizationService _fileOrgService;
    private readonly IImageProcessingService? _imageService;
    private readonly IMetadataService? _metadataService;
    private readonly IAppPreferencesService? _prefsService;
    private CancellationTokenSource? _cts;

    public Evento Evento { get; }
    public Atleta Atleta { get; }

    public ObservableCollection<RawCandidateItemViewModel> RawCandidates { get; } = new();

    [ObservableProperty]
    private int _totalCandidateCount;

    [ObservableProperty]
    private int _selectedCandidateCount;

    [ObservableProperty]
    private bool _selectAll = true;

    [ObservableProperty]
    private bool _canConvert;

    [ObservableProperty]
    private bool _isScanning = true;

    [ObservableProperty]
    private string _scanMessage = "Ricerca foto RAW mancanti in corso...";

    // Opzioni Ingestione / Elaborazione
    [ObservableProperty]
    private bool _autoRotate = true;

    [ObservableProperty]
    private bool _applyColorCorrection = true;

    [ObservableProperty]
    private bool _watermarkEnabled;

    [ObservableProperty]
    private string? _watermarkImagePath;

    [ObservableProperty]
    private float _watermarkOpacity = 0.65f;

    [ObservableProperty]
    private WatermarkPosition _watermarkPosition = WatermarkPosition.BottomRight;

    [ObservableProperty]
    private float _watermarkScalePercent = 0.20f;

    [ObservableProperty]
    private bool _injectMetadata = true;

    [ObservableProperty]
    private string _photographerName = string.Empty;

    [ObservableProperty]
    private string _copyrightNotice = string.Empty;

    public List<WatermarkPosition> AvailablePositions { get; } = Enum.GetValues<WatermarkPosition>().ToList();

    // Telemetria e Progresso in Tempo Reale
    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private int _processedCount;

    [ObservableProperty]
    private int _totalToProcess;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string? _currentProcessingFile;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isCompleted;

    public bool PhotosConverted { get; private set; }

    public event Action? RequestClose;

    public RawConversionDialogViewModel(
        Evento evento,
        Atleta atleta,
        IExcelRepository excelRepo,
        IFileOrganizationService fileOrgService,
        IImageProcessingService? imageService = null,
        IMetadataService? metadataService = null,
        IAppPreferencesService? prefsService = null)
    {
        Evento = evento;
        Atleta = atleta;
        _excelRepo = excelRepo;
        _fileOrgService = fileOrgService;
        _imageService = imageService;
        _metadataService = metadataService;
        _prefsService = prefsService;

        // Inizializza preferenze predefinite
        if (_prefsService != null)
        {
            AutoRotate = _prefsService.DefaultAutoRotate;
            WatermarkEnabled = _prefsService.DefaultWatermarkEnabled;
            WatermarkImagePath = _prefsService.DefaultWatermarkImagePath;
            WatermarkOpacity = _prefsService.DefaultWatermarkOpacity > 0 ? _prefsService.DefaultWatermarkOpacity : 0.65f;
            WatermarkPosition = _prefsService.DefaultWatermarkPosition;
            WatermarkScalePercent = _prefsService.DefaultWatermarkScalePercent > 0 ? _prefsService.DefaultWatermarkScalePercent : 0.20f;
            PhotographerName = _prefsService.DefaultPhotographerName ?? string.Empty;
            CopyrightNotice = _prefsService.DefaultCopyrightNotice ?? string.Empty;
        }

        RawCandidates.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (RawCandidateItemViewModel item in e.NewItems)
                {
                    item.PropertyChanged += (sender, args) =>
                    {
                        if (args.PropertyName == nameof(RawCandidateItemViewModel.IsSelected))
                        {
                            UpdateSelectionState();
                        }
                    };
                }
            }
            UpdateSelectionState();
        };
    }

    public async Task InitializeAndScanAsync()
    {
        IsScanning = true;
        ScanMessage = "Scansione file RAW privi di JPEG corrispondente...";
        RawCandidates.Clear();

        try
        {
            var resolvedBasePath = await _excelRepo.GetResolvedBasePathAsync();
            if (string.IsNullOrWhiteSpace(resolvedBasePath))
            {
                resolvedBasePath = (await _excelRepo.GetBasePathAsync()) ?? string.Empty;
            }

            var allPhotos = (await _excelRepo.GetFotoByEventoAsync(Evento.Id))
                .Where(f => f.AtletaId == Atleta.Id && !f.IsPremiazione)
                .ToList();

            var disciplines = (await _excelRepo.GetDisciplineByEventoAsync(Evento.Id)).ToList();

            var rawPhotos = allPhotos.Where(f => f.IsRaw).ToList();
            var existingJpegBaseNames = new HashSet<string>(
                allPhotos.Where(f => !f.IsRaw).Select(j => Path.GetFileNameWithoutExtension(j.PathRelativo)),
                StringComparer.OrdinalIgnoreCase);

            var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Analisi file RAW registrati nel database Excel
            foreach (var raw in rawPhotos)
            {
                var baseName = Path.GetFileNameWithoutExtension(raw.PathRelativo);
                var fullRawPath = Path.IsPathRooted(raw.PathRelativo)
                    ? raw.PathRelativo
                    : Path.Combine(resolvedBasePath, raw.PathRelativo);

                if (!File.Exists(fullRawPath))
                {
                    continue;
                }

                foundPaths.Add(fullRawPath);

                var disciplina = disciplines.FirstOrDefault(d => d.Id == raw.DisciplinaId)
                    ?? new Disciplina { EventoId = Evento.Id, NomeDisciplina = "Generale" };

                var expectedJpegDir = _fileOrgService.GetDestinationDirectory(
                    resolvedBasePath, Evento.NomeEvento, Atleta, disciplina, ".jpg");

                var expectedJpeg1 = Path.Combine(expectedJpegDir, baseName + ".jpg");
                var expectedJpeg2 = Path.Combine(expectedJpegDir, baseName + ".JPG");
                var expectedJpeg3 = Path.Combine(expectedJpegDir, baseName + ".jpeg");

                bool jpegOnDisk = File.Exists(expectedJpeg1) || File.Exists(expectedJpeg2) || File.Exists(expectedJpeg3);
                bool jpegInDb = existingJpegBaseNames.Contains(baseName);

                if (!jpegOnDisk || !jpegInDb)
                {
                    var item = new RawCandidateItemViewModel
                    {
                        RawFilePath = fullRawPath,
                        FileName = Path.GetFileName(fullRawPath),
                        BaseFileName = baseName,
                        Disciplina = disciplina,
                        FotoEntity = raw,
                        FileSizeBytes = new FileInfo(fullRawPath).Length,
                        IsSelected = true
                    };
                    item.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(RawCandidateItemViewModel.IsSelected))
                        {
                            UpdateSelectionState();
                        }
                    };
                    RawCandidates.Add(item);
                }
            }

            // 2. Analisi ricorsiva cartelle su disco (nel caso ci siano file RAW presenti nella cartella atleta ma non ancora in Excel)
            var sanitizedEvent = _fileOrgService.SanitizeFolderName(Evento.NomeEvento);
            var athleteDir = Path.Combine(resolvedBasePath, sanitizedEvent, Atleta.NomeCartellaSanitizzato);

            if (Directory.Exists(athleteDir))
            {
                var diskRawFiles = Directory.EnumerateFiles(athleteDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => _fileOrgService.IsRawFormat(Path.GetExtension(f)))
                    .ToList();

                foreach (var diskRaw in diskRawFiles)
                {
                    if (foundPaths.Contains(diskRaw))
                    {
                        continue;
                    }

                    var baseName = Path.GetFileNameWithoutExtension(diskRaw);
                    var dir = Path.GetDirectoryName(diskRaw) ?? string.Empty;
                    var parentDir = Path.GetDirectoryName(dir) ?? string.Empty;
                    var dirName = Path.GetFileName(dir);

                    if (dirName.Equals("Raw", StringComparison.OrdinalIgnoreCase))
                    {
                        var siblingJpeg1 = Path.Combine(parentDir, "Jpeg", baseName + ".jpg");
                        var siblingJpeg2 = Path.Combine(parentDir, "Jpeg", baseName + ".JPG");

                        if (!File.Exists(siblingJpeg1) && !File.Exists(siblingJpeg2) && !existingJpegBaseNames.Contains(baseName))
                        {
                            var discName = Path.GetFileName(parentDir);
                            var disciplina = disciplines.FirstOrDefault(d => d.NomeDisciplina.Equals(discName, StringComparison.OrdinalIgnoreCase))
                                ?? new Disciplina { EventoId = Evento.Id, NomeDisciplina = string.IsNullOrWhiteSpace(discName) ? "Generale" : discName };

                            var item = new RawCandidateItemViewModel
                            {
                                RawFilePath = diskRaw,
                                FileName = Path.GetFileName(diskRaw),
                                BaseFileName = baseName,
                                Disciplina = disciplina,
                                FotoEntity = null,
                                FileSizeBytes = new FileInfo(diskRaw).Length,
                                IsSelected = true
                            };
                            item.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName == nameof(RawCandidateItemViewModel.IsSelected))
                                {
                                    UpdateSelectionState();
                                }
                            };
                            RawCandidates.Add(item);
                        }
                    }
                }
            }

            UpdateSelectionState();

            // Avvio caricamento miniature in background
            foreach (var candidate in RawCandidates)
            {
                _ = candidate.LoadThumbnailAsync(_imageService);
            }
        }
        catch (Exception ex)
        {
            ScanMessage = $"Errore durante la scansione: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool _isUpdatingSelection;

    private void UpdateSelectionState()
    {
        _isUpdatingSelection = true;
        try
        {
            SelectedCandidateCount = RawCandidates.Count(c => c.IsSelected);
            TotalCandidateCount = RawCandidates.Count;
            CanConvert = SelectedCandidateCount > 0 && !IsProcessing;
            SelectAll = TotalCandidateCount > 0 && SelectedCandidateCount == TotalCandidateCount;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    partial void OnSelectAllChanged(bool value)
    {
        if (_isUpdatingSelection) return;
        foreach (var candidate in RawCandidates)
        {
            candidate.IsSelected = value;
        }
        UpdateSelectionState();
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        SelectAll = !SelectAll;
    }

    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (!CanConvert || IsProcessing || _imageService == null)
        {
            return;
        }

        var itemsToConvert = RawCandidates.Where(c => c.IsSelected).ToList();
        if (itemsToConvert.Count == 0)
        {
            return;
        }

        IsProcessing = true;
        CanConvert = false;
        IsCompleted = false;
        TotalToProcess = itemsToConvert.Count;
        ProcessedCount = 0;
        ProgressPercentage = 0.0;
        StatusMessage = $"Inizio conversione di {TotalToProcess} foto RAW...";

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var resolvedBasePath = await _excelRepo.GetResolvedBasePathAsync();
        if (string.IsNullOrWhiteSpace(resolvedBasePath))
        {
            resolvedBasePath = (await _excelRepo.GetBasePathAsync()) ?? string.Empty;
        }

        var options = new RawConversionOptions
        {
            AutoRotate = AutoRotate,
            ApplyColorCorrection = ApplyColorCorrection,
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
                PhotographerName = PhotographerName,
                CopyrightNotice = CopyrightNotice
            }
        };

        var completedPhotos = new List<Foto>();

        try
        {
            for (int i = 0; i < itemsToConvert.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var item = itemsToConvert[i];
                CurrentProcessingFile = item.FileName;
                StatusMessage = $"Conversione {i + 1} di {TotalToProcess}: {item.FileName}...";

                var destDir = _fileOrgService.GetDestinationDirectory(
                    resolvedBasePath, Evento.NomeEvento, Atleta, item.Disciplina, ".jpg");

                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                var destJpegPath = Path.Combine(destDir, item.BaseFileName + ".jpg");

                // Esegue la conversione con correzione colore e opzioni
                var converted = await _imageService.ConvertRawToJpegAsync(item.RawFilePath, destJpegPath, options, token).ConfigureAwait(false);

                if (converted && File.Exists(destJpegPath))
                {
                    var fileInfo = new FileInfo(destJpegPath);
                    var md5 = ComputeFileMd5(destJpegPath);

                    DateTime? captureDate = null;
                    if (_metadataService != null)
                    {
                        captureDate = await _metadataService.ExtractCaptureDateAsync(destJpegPath, token).ConfigureAwait(false);
                        if (captureDate == null && File.Exists(item.RawFilePath))
                        {
                            captureDate = await _metadataService.ExtractCaptureDateAsync(item.RawFilePath, token).ConfigureAwait(false);
                        }
                    }

                    if (captureDate == null && item.FotoEntity != null)
                    {
                        captureDate = item.FotoEntity.DataScatto;
                    }

                    var relPath = _fileOrgService.GetRelativePhotoPath(
                        Evento.NomeEvento, Atleta, item.Disciplina, ".jpg", Path.GetFileName(destJpegPath));

                    var newFoto = new Foto
                    {
                        EventoId = Evento.Id,
                        AtletaId = Atleta.Id,
                        DisciplinaId = item.Disciplina.Id,
                        IsPremiazione = false,
                        NomeFileOriginale = item.FileName,
                        PathRelativo = relPath,
                        Formato = "JPEG",
                        DataScatto = captureDate,
                        Fotografo = InjectMetadata && !string.IsNullOrWhiteSpace(PhotographerName) ? PhotographerName : null,
                        WatermarkApplicato = WatermarkEnabled,
                        DimensioneByte = fileInfo.Length,
                        HashMd5 = md5
                    };

                    completedPhotos.Add(newFoto);
                }

                ProcessedCount = i + 1;
                ProgressPercentage = (ProcessedCount / (double)TotalToProcess) * 100.0;
            }

            // Salvataggio batch nel database Excel
            if (completedPhotos.Count > 0)
            {
                StatusMessage = "Salvataggio foto nel database Excel...";
                await _excelRepo.AddFotoBatchAsync(completedPhotos, token).ConfigureAwait(false);
                PhotosConverted = true;
            }

            StatusMessage = $"Conversione completata con successo! {completedPhotos.Count} file JPEG generati.";
            IsCompleted = true;

            // Rimuove gli elementi elaborati dalla lista visualizzata
            SafeDispatch(() =>
            {
                foreach (var item in itemsToConvert)
                {
                    RawCandidates.Remove(item);
                }
                UpdateSelectionState();
            });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operazione annullata dall'utente.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore durante la conversione: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            UpdateSelectionState();
        }
    }

    [RelayCommand]
    private void Close()
    {
        if (IsProcessing && _cts != null)
        {
            _cts.Cancel();
        }
        RequestClose?.Invoke();
    }

    private static void SafeDispatch(Action action)
    {
        if (Avalonia.Application.Current == null)
        {
            action();
            return;
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }
        catch
        {
            action();
        }
    }

    private static string ComputeFileMd5(string filePath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}
