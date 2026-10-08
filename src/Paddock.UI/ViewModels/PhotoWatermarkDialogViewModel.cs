using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;

namespace Paddock.UI.ViewModels;

public partial class PhotoWatermarkDialogViewModel : ViewModelBase
{
    private readonly IImageProcessingService? _imageService;
    private readonly Func<WatermarkOptions, MetadataOptions, Task<bool>>? _applyCallback;

    public PhotoItemViewModel CurrentPhoto { get; }

    public string PhotoFileName => CurrentPhoto.NomeFile;
    public string PhotoFormatDisplay => CurrentPhoto.Formato;

    [ObservableProperty]
    private bool _watermarkEnabled = true;

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
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public List<WatermarkPosition> AvailablePositions { get; } = Enum.GetValues<WatermarkPosition>().ToList();

    public event Action? RequestClose;

    public PhotoWatermarkDialogViewModel(
        PhotoItemViewModel photo,
        IAppPreferencesService? prefsService = null,
        IImageProcessingService? imageService = null,
        Func<WatermarkOptions, MetadataOptions, Task<bool>>? applyCallback = null)
    {
        CurrentPhoto = photo;
        _imageService = imageService;
        _applyCallback = applyCallback;

        // Inizializza impostazioni predefinite da preferenze globali se disponibili
        if (prefsService != null)
        {
            WatermarkEnabled = prefsService.DefaultWatermarkEnabled;
            WatermarkImagePath = prefsService.DefaultWatermarkImagePath;
            WatermarkOpacity = prefsService.DefaultWatermarkOpacity > 0 ? prefsService.DefaultWatermarkOpacity : 0.65f;
            WatermarkPosition = prefsService.DefaultWatermarkPosition;
            WatermarkScalePercent = prefsService.DefaultWatermarkScalePercent > 0 ? prefsService.DefaultWatermarkScalePercent : 0.20f;
            PhotographerName = !string.IsNullOrWhiteSpace(photo.Foto.Fotografo)
                ? photo.Foto.Fotografo
                : (prefsService.DefaultPhotographerName ?? string.Empty);
            CopyrightNotice = prefsService.DefaultCopyrightNotice ?? string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(photo.Foto.Fotografo))
        {
            PhotographerName = photo.Foto.Fotografo;
        }

        // Se la foto aveva già il watermark applicato, mantieni WatermarkEnabled a true
        if (photo.WatermarkApplicato)
        {
            WatermarkEnabled = true;
        }

        _ = UpdatePreviewAsync();
    }

    public async Task UpdatePreviewAsync()
    {
        if (_imageService == null || string.IsNullOrWhiteSpace(CurrentPhoto.FullPath) || !File.Exists(CurrentPhoto.FullPath))
            return;

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

            var bytes = await _imageService.GenerateWatermarkPreviewJpegAsync(
                CurrentPhoto.FullPath,
                options,
                previewWidth: 640,
                previewHeight: 426);

            if (bytes != null && bytes.Length > 0)
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new Bitmap(ms);
                Dispatcher.UIThread.Post(() =>
                {
                    var old = WatermarkPreviewBitmap;
                    WatermarkPreviewBitmap = bmp;
                    old?.Dispose();
                });
            }
        }
        catch
        {
            // Ignora errori di rendering anteprima temporanei
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsPreviewLoading = false;
            });
        }
    }

    partial void OnWatermarkEnabledChanged(bool value) => _ = UpdatePreviewAsync();
    partial void OnWatermarkImagePathChanged(string? value) => _ = UpdatePreviewAsync();
    partial void OnWatermarkOpacityChanged(float value) => _ = UpdatePreviewAsync();
    partial void OnWatermarkPositionChanged(WatermarkPosition value) => _ = UpdatePreviewAsync();
    partial void OnWatermarkScalePercentChanged(float value) => _ = UpdatePreviewAsync();

    [RelayCommand]
    public async Task ApplyAndSaveAsync()
    {
        if (WatermarkEnabled && string.IsNullOrWhiteSpace(WatermarkImagePath))
        {
            ErrorMessage = "Seleziona un'immagine per il watermark oppure disattiva l'opzione.";
            return;
        }

        if (WatermarkEnabled && !File.Exists(WatermarkImagePath))
        {
            ErrorMessage = "Il file del logo watermark selezionato non esiste su disco.";
            return;
        }

        ErrorMessage = null;
        IsBusy = true;

        try
        {
            var watermarkOptions = new WatermarkOptions
            {
                Enabled = WatermarkEnabled,
                WatermarkImagePath = WatermarkImagePath,
                Opacity = WatermarkOpacity,
                Position = WatermarkPosition,
                ScalePercent = WatermarkScalePercent
            };

            var metadataOptions = new MetadataOptions
            {
                InjectPhotographer = InjectMetadata,
                PhotographerName = PhotographerName.Trim(),
                CopyrightNotice = CopyrightNotice.Trim()
            };

            if (_applyCallback != null)
            {
                var success = await _applyCallback(watermarkOptions, metadataOptions);
                if (success)
                {
                    RequestClose?.Invoke();
                }
                else
                {
                    ErrorMessage = "Impossibile applicare il watermark o salvare la foto.";
                }
            }
            else
            {
                RequestClose?.Invoke();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Errore durante l'elaborazione: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        RequestClose?.Invoke();
    }
}

