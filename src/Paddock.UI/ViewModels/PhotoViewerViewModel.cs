using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;

namespace Paddock.UI.ViewModels;

public partial class PhotoViewerViewModel : ViewModelBase, IDisposable
{
    private readonly List<PhotoItemViewModel> _photos;
    private readonly IImageProcessingService? _imageService;
    private readonly Func<PhotoItemViewModel, Task<bool>>? _deleteCallback;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private PhotoItemViewModel? _currentPhoto;

    [ObservableProperty]
    private Bitmap? _currentBitmap;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isDeleting;

    public IReadOnlyList<PhotoItemViewModel> Photos => _photos;
    public int PhotosCount => _photos.Count;

    public string WindowTitle => CurrentPhoto != null
        ? $"{CurrentPhoto.NomeFile} ({CurrentIndex + 1}/{PhotosCount}) — Visore Foto Paddock"
        : "Visore Foto Paddock";

    public string CounterDisplay => PhotosCount > 0
        ? $"{CurrentIndex + 1} / {PhotosCount}"
        : "0 / 0";

    public bool CanGoPrevious => CurrentIndex > 0 && !IsDeleting;
    public bool CanGoNext => CurrentIndex < _photos.Count - 1 && !IsDeleting;

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
        Func<PhotoItemViewModel, Task<bool>>? deleteCallback = null)
    {
        _photos = photos != null ? new List<PhotoItemViewModel>(photos) : new List<PhotoItemViewModel>();
        _imageService = imageService;
        _deleteCallback = deleteCallback;

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

    private async Task LoadPhotoAtCurrentIndexAsync()
    {
        if (CurrentIndex < 0 || CurrentIndex >= _photos.Count)
        {
            CurrentPhoto = null;
            var old = CurrentBitmap;
            CurrentBitmap = null;
            old?.Dispose();
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
                oldBmp?.Dispose();
                StatusMessage = "File immagine non trovato su disco.";
                return;
            }

            Bitmap? loadedBitmap = null;

            if (photo.IsRaw)
            {
                if (_imageService != null)
                {
                    var bytes = await _imageService.GenerateThumbnailAsync(photo.FullPath, 2560, 1600, token);
                    if (token.IsCancellationRequested) return;
                    if (bytes != null && bytes.Length > 0)
                    {
                        using var ms = new MemoryStream(bytes);
                        loadedBitmap = new Bitmap(ms);
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
                    loadedBitmap = new Bitmap(ms);
                }, token);
            }

            if (!token.IsCancellationRequested)
            {
                var old = CurrentBitmap;
                CurrentBitmap = loadedBitmap;
                old?.Dispose();
            }
            else
            {
                loadedBitmap?.Dispose();
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
            old?.Dispose();
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
        old?.Dispose();
    }
}

