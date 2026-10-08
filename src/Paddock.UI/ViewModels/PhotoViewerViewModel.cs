using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Interfaces;

namespace Paddock.UI.ViewModels;

public partial class PhotoViewerViewModel : ViewModelBase, IDisposable
{
    private readonly List<PhotoItemViewModel> _photos;
    private readonly IImageProcessingService? _imageService;
    private readonly Func<PhotoItemViewModel, Task<bool>>? _deleteCallback;
    private readonly IMetadataService? _metadataService;
    private readonly IAppPreferencesService? _prefsService;
    private readonly IExcelRepository? _excelRepo;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private PhotoItemViewModel? _currentPhoto;

    [ObservableProperty]
    private IImage? _currentBitmap;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private bool _isWatermarkDialogOpen;

    [ObservableProperty]
    private PhotoWatermarkDialogViewModel? _watermarkDialog;

    public Func<Stream, IImage>? BitmapStreamLoader { get; set; }

    public IReadOnlyList<PhotoItemViewModel> Photos => _photos;
    public int PhotosCount => _photos.Count;

    public string WindowTitle => CurrentPhoto != null
        ? $"{CurrentPhoto.NomeFile} ({CurrentIndex + 1}/{PhotosCount}) — Visore Foto Paddock"
        : "Visore Foto Paddock";

    public string CounterDisplay => PhotosCount > 0
        ? $"{CurrentIndex + 1} / {PhotosCount}"
        : "0 / 0";

    public bool CanGoPrevious => CurrentIndex > 0 && !IsDeleting && !IsWatermarkDialogOpen;
    public bool CanGoNext => CurrentIndex < _photos.Count - 1 && !IsDeleting && !IsWatermarkDialogOpen;

    public bool CanApplyWatermark
    {
        get
        {
            if (CurrentPhoto == null || CurrentPhoto.IsRaw || string.IsNullOrWhiteSpace(FullPath))
                return false;

            var ext = Path.GetExtension(FullPath)?.ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png";
        }
    }

    public string NomeFile => CurrentPhoto?.NomeFile ?? "-";
    public string AtletaDisplay => CurrentPhoto?.AtletaDisplay ?? "-";
    public string DisciplinaDisplay => CurrentPhoto?.DisciplinaDisplay ?? "-";
    public string DimensioneDisplay => CurrentPhoto?.DimensioneDisplay ?? "-";
    public string DataScattoDisplay => CurrentPhoto?.DataScattoDisplay ?? "-";
    public string Formato => CurrentPhoto?.Formato ?? "-";
    public bool WatermarkApplicato => CurrentPhoto?.WatermarkApplicato ?? false;
    public string FullPath => CurrentPhoto?.FullPath ?? string.Empty;

    public event Action? RequestClose;
    public event Action<PhotoItemViewModel>? PhotoDeleted;

    public PhotoViewerViewModel(
        IReadOnlyList<PhotoItemViewModel> photos,
        int initialIndex,
        IImageProcessingService? imageService = null,
        Func<PhotoItemViewModel, Task<bool>>? deleteCallback = null,
        IMetadataService? metadataService = null,
        IAppPreferencesService? prefsService = null,
        IExcelRepository? excelRepo = null)
    {
        _photos = photos != null ? new List<PhotoItemViewModel>(photos) : new List<PhotoItemViewModel>();
        _imageService = imageService;
        _deleteCallback = deleteCallback;
        _metadataService = metadataService;
        _prefsService = prefsService;
        _excelRepo = excelRepo;

        if (_photos.Count > 0)
        {
            _currentIndex = Math.Clamp(initialIndex, 0, _photos.Count - 1);
            _currentPhoto = _photos[_currentIndex];
            _ = LoadPhotoAtCurrentIndexAsync();
        }
        else
        {
            _currentIndex = 0;
            _currentPhoto = null;
        }

        NotifyNavigationChanged();
    }

    partial void OnCurrentIndexChanged(int value)
    {
        _ = LoadPhotoAtCurrentIndexAsync();
    }

    public async Task LoadPhotoAtCurrentIndexAsync()
    {
        if (CurrentIndex < 0 || CurrentIndex >= _photos.Count)
        {
            CurrentPhoto = null;
            var old = CurrentBitmap;
            CurrentBitmap = null;
            (old as IDisposable)?.Dispose();
            NotifyNavigationChanged();
            return;
        }

        CurrentPhoto = _photos[CurrentIndex];
        NotifyNavigationChanged();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        StatusMessage = null;

        try
        {
            var photo = CurrentPhoto;
            if (photo == null || !File.Exists(photo.FullPath))
            {
                var oldBmp = CurrentBitmap;
                CurrentBitmap = null;
                (oldBmp as IDisposable)?.Dispose();
                StatusMessage = "File immagine non trovato su disco.";
                return;
            }

            IImage? loadedBitmap = null;

            if (photo.IsRaw)
            {
                if (_imageService != null)
                {
                    var bytes = await _imageService.ExtractRawPreviewAsync(photo.FullPath, token);
                    if (bytes == null || bytes.Length == 0)
                    {
                        bytes = await _imageService.GenerateThumbnailAsync(photo.FullPath, 2560, 1600, token);
                    }

                    if (token.IsCancellationRequested) return;
                    if (bytes != null && bytes.Length > 0)
                    {
                        using var ms = new MemoryStream(bytes);
                        loadedBitmap = BitmapStreamLoader != null
                            ? BitmapStreamLoader(ms)
                            : new Bitmap(ms);
                    }
                }
            }
            else
            {
                await Task.Run(async () =>
                {
                    using var fs = new FileStream(photo.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var ms = new MemoryStream();
                    await fs.CopyToAsync(ms, token);
                    if (token.IsCancellationRequested) return;
                    ms.Position = 0;
                    loadedBitmap = BitmapStreamLoader != null
                        ? BitmapStreamLoader(ms)
                        : new Bitmap(ms);
                }, token);
            }

            if (!token.IsCancellationRequested)
            {
                var old = CurrentBitmap;
                CurrentBitmap = loadedBitmap;
                (old as IDisposable)?.Dispose();
            }
            else
            {
                (loadedBitmap as IDisposable)?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Ignorato durante scroll/cambio rapido
        }
        catch (Exception ex)
        {
            StatusMessage = $"Impossibile caricare l'immagine: {ex.Message}";
            var old = CurrentBitmap;
            CurrentBitmap = null;
            (old as IDisposable)?.Dispose();
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    public void PreviousPhoto()
    {
        if (CanGoPrevious)
        {
            CurrentIndex--;
        }
    }

    [RelayCommand]
    public void NextPhoto()
    {
        if (CanGoNext)
        {
            CurrentIndex++;
        }
    }

    [RelayCommand]
    public void OpenFileLocation()
    {
        if (!string.IsNullOrEmpty(FullPath) && File.Exists(FullPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{FullPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore apertura cartella: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    public async Task DeleteCurrentPhotoAsync()
    {
        if (CurrentPhoto == null || IsDeleting) return;

        IsDeleting = true;
        try
        {
            var photoToDelete = CurrentPhoto;
            bool deleted = false;
            if (_deleteCallback != null)
            {
                deleted = await _deleteCallback(photoToDelete);
            }

            if (deleted)
            {
                _photos.Remove(photoToDelete);
                PhotoDeleted?.Invoke(photoToDelete);

                if (_photos.Count == 0)
                {
                    RequestClose?.Invoke();
                    return;
                }

                if (CurrentIndex >= _photos.Count)
                {
                    CurrentIndex = _photos.Count - 1;
                }
                else
                {
                    await LoadPhotoAtCurrentIndexAsync();
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore eliminazione foto: {ex.Message}";
        }
        finally
        {
            IsDeleting = false;
            NotifyNavigationChanged();
        }
    }

    [RelayCommand]
    public void OpenWatermarkDialog()
    {
        if (!CanApplyWatermark || CurrentPhoto == null) return;

        WatermarkDialog = new PhotoWatermarkDialogViewModel(
            CurrentPhoto,
            _prefsService,
            _imageService,
            ApplyWatermarkAndMetadataAsync);

        WatermarkDialog.RequestClose += CloseWatermarkDialog;
        IsWatermarkDialogOpen = true;
    }

    [RelayCommand]
    public void CloseWatermarkDialog()
    {
        IsWatermarkDialogOpen = false;
        WatermarkDialog = null;
    }

    public async Task<bool> ApplyWatermarkAndMetadataAsync(
        WatermarkOptions watermarkOptions,
        MetadataOptions metadataOptions)
    {
        if (CurrentPhoto == null || !CanApplyWatermark || !File.Exists(FullPath))
            return false;

        try
        {
            var sourcePath = FullPath;
            var ext = Path.GetExtension(sourcePath);
            var tempPath = Path.Combine(Path.GetTempPath(), $".paddock_wm_work_{Guid.NewGuid()}{ext}");

            // 1. Applica Watermark (se abilitato)
            if (_imageService != null && watermarkOptions.Enabled)
            {
                await _imageService.ApplyWatermarkAsync(sourcePath, tempPath, watermarkOptions);
            }
            else
            {
                File.Copy(sourcePath, tempPath, overwrite: true);
            }

            // 2. Inietta Metadati (se abilitati)
            if (metadataOptions.InjectPhotographer && !string.IsNullOrWhiteSpace(metadataOptions.PhotographerName))
            {
                if (_metadataService != null)
                {
                    await _metadataService.WritePhotographerMetadataAsync(
                        tempPath,
                        metadataOptions.PhotographerName,
                        metadataOptions.CopyrightNotice);
                }
            }

            // 3. Sostituzione definitiva su disco
            File.Copy(tempPath, sourcePath, overwrite: true);
            try { File.Delete(tempPath); } catch { }

            // 4. Aggiorna modello Foto e proprietà
            var fileInfo = new FileInfo(sourcePath);
            CurrentPhoto.Foto.DimensioneByte = fileInfo.Length;
            CurrentPhoto.Foto.WatermarkApplicato = watermarkOptions.Enabled;
            if (metadataOptions.InjectPhotographer && !string.IsNullOrWhiteSpace(metadataOptions.PhotographerName))
            {
                CurrentPhoto.Foto.Fotografo = metadataOptions.PhotographerName;
            }

            // 5. Aggiorna record nel database Excel (se disponibile)
            if (_excelRepo != null)
            {
                await _excelRepo.UpdateFotoAsync(CurrentPhoto.Foto);
            }

            // 6. Ricarica l'immagine nel visore a risoluzione piena
            await LoadPhotoAtCurrentIndexAsync();

            // 7. Ricarica la miniatura nel PhotoItemViewModel
            CurrentPhoto.ThumbnailBitmap = null;
            _ = CurrentPhoto.LoadThumbnailAsync(_imageService);

            // 8. Notifica proprietà aggiornate
            NotifyNavigationChanged();
            StatusMessage = "Foto salvata con successo con watermark e metadati.";

            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore salvataggio foto: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    public void CloseViewer()
    {
        RequestClose?.Invoke();
    }

    private void NotifyNavigationChanged()
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(CounterDisplay));
        OnPropertyChanged(nameof(PhotosCount));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanApplyWatermark));
        OnPropertyChanged(nameof(NomeFile));
        OnPropertyChanged(nameof(AtletaDisplay));
        OnPropertyChanged(nameof(DisciplinaDisplay));
        OnPropertyChanged(nameof(DimensioneDisplay));
        OnPropertyChanged(nameof(DataScattoDisplay));
        OnPropertyChanged(nameof(Formato));
        OnPropertyChanged(nameof(WatermarkApplicato));
        OnPropertyChanged(nameof(FullPath));
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        var old = CurrentBitmap;
        CurrentBitmap = null;
        (old as IDisposable)?.Dispose();
    }
}

