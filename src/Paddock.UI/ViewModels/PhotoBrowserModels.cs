using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class PhotoItemViewModel : ObservableObject
{
    private static readonly SemaphoreSlim _decodingThrottle = new(4, 4);

    public Foto Foto { get; }
    public string FullPath { get; }

    public string NomeFile => Foto.NomeFileOriginale;
    public string Formato => Foto.Formato;
    public bool IsRaw => Foto.IsRaw;
    public bool WatermarkApplicato => Foto.WatermarkApplicato;
    public long DimensioneByte => Foto.DimensioneByte;
    public DateTime? DataScatto => Foto.DataScatto;

    public string AtletaDisplay { get; set; } = string.Empty;
    public string DisciplinaDisplay { get; set; } = string.Empty;
    public string DimensioneDisplay => DimensioneByte < 1024 * 1024 ? $"{DimensioneByte / 1024.0:F1} KB" : $"{DimensioneByte / (1024.0 * 1024.0):F1} MB";
    public string DataScattoDisplay => DataScatto?.ToString("dd/MM/yyyy HH:mm") ?? "-";

    [ObservableProperty]
    private Bitmap? _thumbnailBitmap;

    [ObservableProperty]
    private bool _isLoadingThumbnail;

    [ObservableProperty]
    private bool _hasThumbnailError;

    public bool ShowRawPlaceholder => IsRaw && ThumbnailBitmap == null && !IsLoadingThumbnail;

    public PhotoItemViewModel(Foto foto, string fullPath)
    {
        Foto = foto;
        FullPath = fullPath;
    }

    public async Task LoadThumbnailAsync(IImageProcessingService? imageService, CancellationToken cancellationToken = default)
    {
        if (ThumbnailBitmap != null || !File.Exists(FullPath))
            return;

        await _decodingThrottle.WaitAsync(cancellationToken);
        try
        {
            if (ThumbnailBitmap != null || cancellationToken.IsCancellationRequested)
                return;

            IsLoadingThumbnail = true;

            if (!IsRaw)
            {
                await Task.Run(() =>
                {
                    using var stream = new FileStream(FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    var bmp = Bitmap.DecodeToWidth(ms, 260);
                    Dispatcher.UIThread.Post(() =>
                    {
                        ThumbnailBitmap = bmp;
                        OnPropertyChanged(nameof(ShowRawPlaceholder));
                    });
                }, cancellationToken);
            }
            else
            {
                if (imageService != null)
                {
                    var bytes = await imageService.GenerateThumbnailAsync(FullPath, 260, 160, cancellationToken);
                    if (bytes != null && bytes.Length > 0)
                    {
                        using var ms = new MemoryStream(bytes);
                        var bmp = new Bitmap(ms);
                        Dispatcher.UIThread.Post(() =>
                        {
                            ThumbnailBitmap = bmp;
                            OnPropertyChanged(nameof(ShowRawPlaceholder));
                        });
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Richiesta annullata
        }
        catch
        {
            HasThumbnailError = true;
        }
        finally
        {
            _decodingThrottle.Release();
            IsLoadingThumbnail = false;
            OnPropertyChanged(nameof(ShowRawPlaceholder));
        }
    }
}

public partial class PhotoBrowserDisciplinaGroup : ObservableObject
{
    private IImageProcessingService? _imageService;

    public Disciplina Disciplina { get; }
    public string NomeDisciplina => string.IsNullOrWhiteSpace(Disciplina.NomeDisciplina) ? "Generale" : Disciplina.NomeDisciplina;

    [ObservableProperty]
    private bool _isExpanded = false;

    public ObservableCollection<PhotoItemViewModel> Photos { get; } = new();

    public int PhotosCount => Photos.Count;

    public PhotoBrowserDisciplinaGroup(Disciplina disciplina)
    {
        Disciplina = disciplina;
        Photos.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PhotosCount));
    }

    public void SetImageService(IImageProcessingService? imageService)
    {
        _imageService = imageService;
    }

    public void LoadThumbnails(IImageProcessingService? imageService = null)
    {
        var svc = imageService ?? _imageService;
        foreach (var p in Photos)
        {
            if (p.ThumbnailBitmap == null && !p.IsLoadingThumbnail)
            {
                _ = p.LoadThumbnailAsync(svc);
            }
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            LoadThumbnails(_imageService);
        }
    }
}

public partial class PhotoBrowserAthleteGroup : ObservableObject
{
    private IImageProcessingService? _imageService;

    public Atleta Atleta { get; }
    public string DisplayTitle => Atleta.DisplayPettoraleNome;

    [ObservableProperty]
    private bool _isExpanded = false;

    public ObservableCollection<PhotoBrowserDisciplinaGroup> DisciplineGroups { get; } = new();

    public int TotalPhotosCount => DisciplineGroups.Sum(d => d.Photos.Count);

    public PhotoBrowserAthleteGroup(Atleta atleta)
    {
        Atleta = atleta;
        DisciplineGroups.CollectionChanged += (s, e) => OnPropertyChanged(nameof(TotalPhotosCount));
    }

    public void SetImageService(IImageProcessingService? imageService)
    {
        _imageService = imageService;
        foreach (var dg in DisciplineGroups)
        {
            dg.SetImageService(imageService);
        }
    }

    public void LoadThumbnails(IImageProcessingService? imageService = null)
    {
        var svc = imageService ?? _imageService;
        if (!IsExpanded) return;

        foreach (var dg in DisciplineGroups)
        {
            if (dg.IsExpanded)
            {
                dg.LoadThumbnails(svc);
            }
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            LoadThumbnails(_imageService);
        }
    }

    public void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(TotalPhotosCount));
    }
}
