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

    public SlideshowPhotoItem(Foto foto, string absolutePath, string atletaInfo, string disciplinaInfo)
    {
        Foto = foto;
        AbsolutePath = absolutePath;
        AtletaInfo = atletaInfo;
        DisciplinaInfo = disciplinaInfo;
    }
}

public partial class SlideshowWindowViewModel : ViewModelBase
{
    private readonly SlideshowConfig _config;
    private readonly IExcelRepository _excelRepo;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _hudTimer;
    private readonly Random _random = new();

    private List<SlideshowPhotoItem> _photos = new();
    private int _currentIndex = -1;
    private int _currentTransitionIndex = 0;

    [ObservableProperty]
    private Bitmap? _currentImage;

    [ObservableProperty]
    private Bitmap? _nextImage;

    [ObservableProperty]
    private string _currentAtletaText = string.Empty;

    [ObservableProperty]
    private string _currentCounterText = string.Empty;

    [ObservableProperty]
    private string _currentTransition = "crossfade";

    [ObservableProperty]
    private bool _isHudVisible = true;

    [ObservableProperty]
    private bool _isLayerBActive = false;

    public event Action? RequestClose;

    public SlideshowWindowViewModel(SlideshowConfig config, IExcelRepository excelRepo)
    {
        _config = config;
        _excelRepo = excelRepo;

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

        // Mostra prima foto
        ShowNextPhoto();

        _timer.Start();
        _hudTimer.Start();
    }

    private async Task LoadPhotosAsync()
    {
        var basePath = await _excelRepo.GetBasePathAsync();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Paddock");
        }

        var allFoto = await _excelRepo.GetFotoByEventoAsync(_config.EventoId);
        var allAtleti = await _excelRepo.GetAtletiByEventoAsync(_config.EventoId);
        var atletiMap = allAtleti.ToDictionary(a => a.Id, a => a.DisplayPettoraleNome);

        var matchingFoto = allFoto.Where(f =>
            _config.SelectedAtletiIds.Contains(f.AtletaId) &&
            ((_config.IncludeJpegPng && !f.IsRaw) || (_config.IncludeRaw && f.IsRaw))
        ).ToList();

        var items = new List<SlideshowPhotoItem>();
        foreach (var f in matchingFoto)
        {
            var absPath = Path.IsPathRooted(f.PathRelativo)
                ? f.PathRelativo
                : Path.Combine(basePath, f.PathRelativo);

            if (File.Exists(absPath))
            {
                var atInfo = atletiMap.TryGetValue(f.AtletaId, out var name) ? name : "Atleta";
                items.Add(new SlideshowPhotoItem(f, absPath, atInfo, f.Formato));
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

    private void OnTimerTick(object? sender, EventArgs e)
    {
        ShowNextPhoto();
    }

    private void ShowNextPhoto()
    {
        if (_photos.Count == 0) return;

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

        var item = _photos[_currentIndex];

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

        try
        {
            var bmp = new Bitmap(item.AbsolutePath);

            // Alterna tra Layer A e Layer B per consentire la transizione fluida
            if (!IsLayerBActive)
            {
                NextImage = bmp;
                IsLayerBActive = true;
            }
            else
            {
                CurrentImage = bmp;
                IsLayerBActive = false;
            }

            CurrentAtletaText = item.AtletaInfo;
            CurrentCounterText = $"{_currentIndex + 1} / {_photos.Count}";
        }
        catch
        {
            // Se il file non può essere caricato passa al successivo
        }
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

