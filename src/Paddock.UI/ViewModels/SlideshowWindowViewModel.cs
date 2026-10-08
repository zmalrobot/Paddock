using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public class SlideshowPhotoItem
{
    public Foto Foto { get; set; }
    public string AbsolutePath { get; set; } = string.Empty;
    public string AtletaInfo { get; set; } = string.Empty;
    public string DisciplinaInfo { get; set; } = string.Empty;
    public string EventoInfo { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    public SlideshowPhotoItem(
        Foto foto,
        string absolutePath,
        string atletaInfo = "",
        string disciplinaInfo = "",
        string eventoInfo = "",
        string fileName = "")
    {
        Foto = foto;
        AbsolutePath = absolutePath;
        AtletaInfo = atletaInfo;
        DisciplinaInfo = disciplinaInfo;
        EventoInfo = eventoInfo;
        FileName = !string.IsNullOrWhiteSpace(fileName) ? fileName : Path.GetFileName(absolutePath);
    }
}

public partial class SlideshowWindowViewModel : ViewModelBase
{
    private readonly SlideshowConfig _config;
    private readonly IExcelRepository _excelRepo;
    private readonly IImageProcessingService? _imageService;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _hudTimer;
    private readonly Random _random = new();

    private List<SlideshowPhotoItem> _photos = new();
    private int _currentIndex = -1;
    private int _currentTransitionIndex = 0;

    /// <summary>
    /// Loader opzionale per decoupling e unit testing.
    /// </summary>
    public Func<string, Task<IImage?>>? ImageLoader { get; set; }

    /// <summary>
    /// Stream loader opzionale per decoupling e unit testing senza runtime Avalonia.
    /// </summary>
    public Func<Stream, IImage>? BitmapStreamLoader { get; set; }

    [ObservableProperty]
    private IImage? _currentImage;

    [ObservableProperty]
    private IImage? _nextImage;

    [ObservableProperty]
    private double _layerAOpacity = 1.0;

    [ObservableProperty]
    private double _layerBOpacity = 0.0;

    [ObservableProperty]
    private bool _transitionsEnabled = true;

    [ObservableProperty]
    private double _flashOpacity = 0.0;

    [ObservableProperty]
    private string _currentEventoText = string.Empty;

    [ObservableProperty]
    private string _currentAtletaText = string.Empty;

    [ObservableProperty]
    private string _currentDisciplinaText = string.Empty;

    [ObservableProperty]
    private string _currentPhotoNameText = string.Empty;

    [ObservableProperty]
    private string _currentCounterText = string.Empty;

    [ObservableProperty]
    private string _currentTransition = "crossfade";

    [ObservableProperty]
    private bool _isHudVisible = true;

    [ObservableProperty]
    private bool _isLayerBActive = false;

    public event Action? RequestClose;

    public SlideshowWindowViewModel(
        SlideshowConfig config,
        IExcelRepository excelRepo,
        IImageProcessingService? imageService = null)
    {
        _config = config;
        _excelRepo = excelRepo;
        _imageService = imageService;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _config.DurationSeconds))
        };
        _timer.Tick += OnTimerTick;

        _hudTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _hudTimer.Tick += (s, e) =>
        {
            IsHudVisible = false;
            _hudTimer.Stop();
        };
    }

    public async Task StartAsync()
    {
        await LoadPhotosAsync();
        if (_photos.Count == 0)
        {
            RequestClose?.Invoke();
            return;
        }

        // Mostra prima foto immediatamente
        await ShowNextPhotoAsync();

        _timer.Start();
        _hudTimer.Start();
    }

    private async Task LoadPhotosAsync()
    {
        var basePath = await _excelRepo.GetResolvedBasePathAsync();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = (await _excelRepo.GetBasePathAsync()) ?? string.Empty;
        }

        var allFoto = await _excelRepo.GetFotoByEventoAsync(_config.EventoId);
        var allAtleti = await _excelRepo.GetAtletiByEventoAsync(_config.EventoId);
        var allDiscipline = await _excelRepo.GetDisciplineByEventoAsync(_config.EventoId);
        var evento = await _excelRepo.GetEventoByIdAsync(_config.EventoId);

        var atletiMap = allAtleti.ToDictionary(a => a.Id, a => a.DisplayPettoraleNome);
        var disciplineMap = allDiscipline.ToDictionary(d => d.Id, d => string.IsNullOrWhiteSpace(d.NomeDisciplina) ? "Generale" : d.NomeDisciplina);
        var eventoName = evento?.NomeEvento ?? "Evento";

        var matchingFoto = allFoto.Where(f =>
            (_config.SelectedAtletiIds.Contains(f.AtletaId) || (_config.IncludePremiazioni && f.IsPremiazione)) &&
            ((_config.IncludeJpegPng && !f.IsRaw) || (_config.IncludeRaw && f.IsRaw))
        ).ToList();

        var items = new List<SlideshowPhotoItem>();

        foreach (var f in matchingFoto)
        {
            var cleanRel = f.PathRelativo.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var absPath = Path.IsPathRooted(cleanRel)
                ? cleanRel
                : (!string.IsNullOrWhiteSpace(basePath) ? Path.Combine(basePath, cleanRel) : cleanRel);

            if (!File.Exists(absPath))
            {
                // Fallback: cerca nella cartella destinazione root dell'evento
                if (evento != null && !string.IsNullOrWhiteSpace(evento.CartellaDestinazioneRoot))
                {
                    var resolvedRoot = _excelRepo.ResolvePath(evento.CartellaDestinazioneRoot);
                    var rootDir = !string.IsNullOrWhiteSpace(resolvedRoot) ? resolvedRoot : evento.CartellaDestinazioneRoot;
                    var altPath = Path.Combine(rootDir, Path.GetFileName(cleanRel));
                    if (File.Exists(altPath))
                    {
                        absPath = altPath;
                    }
                }
            }

            if (File.Exists(absPath))
            {
                var atInfo = f.IsPremiazione ? "Premiazioni" : (atletiMap.TryGetValue(f.AtletaId, out var name) ? name : "Atleta");
                var discInfo = f.IsPremiazione ? "Podio & Premiazioni" : (disciplineMap.TryGetValue(f.DisciplinaId, out var disc) ? disc : "Generale");
                var fileName = Path.GetFileName(absPath);
                items.Add(new SlideshowPhotoItem(f, absPath, atInfo, discInfo, eventoName, fileName));
            }
        }

        if (_config.IncludePremiazioni)
        {
            string eventFolder;
            if (evento != null && !string.IsNullOrWhiteSpace(evento.CartellaDestinazioneRoot))
            {
                var resolvedRoot = _excelRepo.ResolvePath(evento.CartellaDestinazioneRoot);
                eventFolder = !string.IsNullOrWhiteSpace(resolvedRoot) ? resolvedRoot : evento.CartellaDestinazioneRoot;
            }
            else
            {
                eventFolder = !string.IsNullOrWhiteSpace(basePath) && evento != null
                    ? Path.Combine(basePath, evento.NomeEvento)
                    : (evento?.NomeEvento ?? string.Empty);
            }
            var premiazioniFolder = !string.IsNullOrWhiteSpace(eventFolder) ? Path.Combine(eventFolder, "Premiazioni") : string.Empty;

            if (Directory.Exists(premiazioniFolder))
            {
                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_config.IncludeJpegPng)
                {
                    extensions.Add(".jpg");
                    extensions.Add(".jpeg");
                    extensions.Add(".png");
                }
                if (_config.IncludeRaw)
                {
                    extensions.Add(".cr2");
                    extensions.Add(".cr3");
                    extensions.Add(".nef");
                    extensions.Add(".arw");
                    extensions.Add(".dng");
                }

                var existingPaths = new HashSet<string>(items.Select(i => i.AbsolutePath), StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.EnumerateFiles(premiazioniFolder, "*.*", SearchOption.AllDirectories))
                {
                    if (extensions.Contains(Path.GetExtension(file)) && !existingPaths.Contains(file))
                    {
                        var ext = Path.GetExtension(file);
                        var isRaw = ext.Equals(".CR2", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".CR3", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".NEF", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".ARW", StringComparison.OrdinalIgnoreCase) ||
                                    ext.Equals(".DNG", StringComparison.OrdinalIgnoreCase);
                        var dummyFoto = new Foto
                        {
                            Id = Guid.NewGuid(),
                            EventoId = _config.EventoId,
                            NomeFileOriginale = Path.GetFileName(file),
                            PathRelativo = Path.GetRelativePath(basePath, file),
                            IsPremiazione = true,
                            Formato = isRaw ? "RAW" : "JPEG"
                        };
                        items.Add(new SlideshowPhotoItem(dummyFoto, file, "Premiazioni", "Podio & Premiazioni", eventoName, Path.GetFileName(file)));
                    }
                }
            }
        }

        if (_config.IsRandomOrder)
        {
            // Shuffle Fisher-Yates
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
        else
        {
            // Ordine alfabetico per nome file
            items = items.OrderBy(x => x.Foto.NomeFileOriginale).ToList();
        }

        _photos = items;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await ShowNextPhotoAsync();
        }
        catch
        {
            // In caso di errore nel ciclo, disabilita le transizioni e continua lo scorrimento
            TransitionsEnabled = false;
        }
    }

    public async Task ShowNextPhotoAsync()
    {
        if (_photos.Count == 0) return;

        IImage? img = null;
        SlideshowPhotoItem? currentItem = null;
        int attempts = 0;

        // Cerca la prossima foto caricabile (salta file corrotti/illeggibili senza bloccare lo slideshow)
        while (img == null && attempts < _photos.Count)
        {
            _currentIndex++;
            if (_currentIndex >= _photos.Count)
            {
                _currentIndex = 0;
                if (_config.IsRandomOrder && _photos.Count > 1)
                {
                    // Rimescola per il ciclo successivo
                    for (int i = _photos.Count - 1; i > 0; i--)
                    {
                        int j = _random.Next(i + 1);
                        (_photos[i], _photos[j]) = (_photos[j], _photos[i]);
                    }
                }
            }

            currentItem = _photos[_currentIndex];
            img = await LoadImageAsync(currentItem);
            attempts++;
        }

        if (img == null || currentItem == null) return;

        // Sceglie la transizione successiva
        if (_config.SelectedTransitionIds.Count > 0)
        {
            CurrentTransition = _config.SelectedTransitionIds[_currentTransitionIndex % _config.SelectedTransitionIds.Count];
            _currentTransitionIndex++;
        }
        else
        {
            CurrentTransition = "crossfade";
        }

        // Prima foto in assoluto: mostra direttamente sul Layer A senza ritardo
        if (CurrentImage == null && NextImage == null)
        {
            CurrentImage = img;
            LayerAOpacity = 1.0;
            LayerBOpacity = 0.0;
            IsLayerBActive = false;
        }
        else
        {
            try
            {
                if (TransitionsEnabled)
                {
                    ApplyPhotoWithTransition(img);
                }
                else
                {
                    ApplyPhotoDirectNoTransition(img);
                }
            }
            catch
            {
                // Nel caso di errore nelle transizioni le immagini devono comunque scorrere ma senza transizione
                TransitionsEnabled = false;
                ApplyPhotoDirectNoTransition(img);
            }
        }

        CurrentEventoText = currentItem.EventoInfo;
        CurrentAtletaText = currentItem.AtletaInfo;
        CurrentDisciplinaText = currentItem.DisciplinaInfo;
        CurrentPhotoNameText = currentItem.FileName;
        CurrentCounterText = $"{_currentIndex + 1} / {_photos.Count}";
    }

    private void ApplyPhotoWithTransition(IImage img)
    {
        if (CurrentTransition == "strobe_flash")
        {
            FlashOpacity = 0.6;
            _ = Task.Delay(80).ContinueWith(_ => Dispatcher.UIThread.Post(() => FlashOpacity = 0.0));
        }

        if (!IsLayerBActive)
        {
            var old = NextImage;
            NextImage = img;
            LayerBOpacity = 1.0;
            LayerAOpacity = 0.0;
            IsLayerBActive = true;
            (old as IDisposable)?.Dispose();
        }
        else
        {
            var old = CurrentImage;
            CurrentImage = img;
            LayerAOpacity = 1.0;
            LayerBOpacity = 0.0;
            IsLayerBActive = false;
            (old as IDisposable)?.Dispose();
        }
    }

    public void ApplyPhotoDirectNoTransition(IImage img)
    {
        FlashOpacity = 0.0;

        if (!IsLayerBActive)
        {
            var old = NextImage;
            NextImage = img;
            LayerBOpacity = 1.0;
            LayerAOpacity = 0.0;
            IsLayerBActive = true;
            (old as IDisposable)?.Dispose();
        }
        else
        {
            var old = CurrentImage;
            CurrentImage = img;
            LayerAOpacity = 1.0;
            LayerBOpacity = 0.0;
            IsLayerBActive = false;
            (old as IDisposable)?.Dispose();
        }
    }

    private async Task<IImage?> LoadImageAsync(SlideshowPhotoItem item)
    {
        if (ImageLoader != null)
        {
            return await ImageLoader(item.AbsolutePath);
        }

        try
        {
            if (item.Foto.IsRaw)
            {
                if (_imageService != null)
                {
                    var bytes = await _imageService.ExtractRawPreviewAsync(item.AbsolutePath);
                    if (bytes == null || bytes.Length == 0)
                    {
                        bytes = await _imageService.GenerateThumbnailAsync(item.AbsolutePath, 2560, 1600);
                    }

                    if (bytes != null && bytes.Length > 0)
                    {
                        using var ms = new MemoryStream(bytes);
                        return BitmapStreamLoader != null ? BitmapStreamLoader(ms) : new Bitmap(ms);
                    }
                }
                return null;
            }

            return await Task.Run<IImage?>(() =>
            {
                using var fs = new FileStream(item.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                return BitmapStreamLoader != null ? BitmapStreamLoader(ms) : new Bitmap(ms);
            });
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    public async Task NextPhotoManualAsync()
    {
        UserActivityDetected();
        await ShowNextPhotoAsync();
    }

    [RelayCommand]
    public async Task PreviousPhotoManualAsync()
    {
        if (_photos.Count <= 1) return;
        UserActivityDetected();
        _currentIndex = (_currentIndex - 2 + _photos.Count) % _photos.Count;
        await ShowNextPhotoAsync();
    }

    [RelayCommand]
    public void UserActivityDetected()
    {
        IsHudVisible = true;
        _hudTimer.Stop();
        _hudTimer.Start();
    }

    [RelayCommand]
    public void CloseSlideshow()
    {
        _timer.Stop();
        _hudTimer.Stop();
        RequestClose?.Invoke();
    }
}
