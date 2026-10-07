using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;

namespace Paddock.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IExcelRepository _excelRepo;
    private readonly IFileOrganizationService _fileOrgService;
    private readonly IIngestionPipelineService _pipelineService;
    private readonly ISdCardWatcherService _sdCardWatcher;
    private readonly IAppPreferencesService _prefsService;
    private readonly IImageProcessingService? _imageService;

    [ObservableProperty]
    private string _eventSearchFilter = string.Empty;

    [ObservableProperty]
    private Evento? _selectedEvento;

    [ObservableProperty]
    private EventDetailViewModel? _activeEventDetail;

    [ObservableProperty]
    private ViewModelBase? _currentModal;

    [ObservableProperty]
    private bool _isModalOpen;

    [ObservableProperty]
    private bool _isLockBannerVisible;

    [ObservableProperty]
    private string _lockBannerMessage = string.Empty;

    [ObservableProperty]
    private bool _isSlideshowActive;

    public Func<List<DisplayScreenInfo>>? GetAvailableScreens { get; set; }
    public event Action<SlideshowConfig>? RequestLaunchSlideshow;
    public event Action? RequestStopSlideshow;

    public ObservableCollection<Evento> AllEventi { get; } = new();
    public ObservableCollection<Evento> FilteredEventi { get; } = new();
    public JobManagerViewModel JobManager { get; }

    public string DatabasePath => _excelRepo.DatabaseFilePath;
    public IExcelRepository ExcelRepo => _excelRepo;

    public MainViewModel(
        IExcelRepository excelRepo,
        IFileOrganizationService fileOrgService,
        IIngestionPipelineService pipelineService,
        ISdCardWatcherService sdCardWatcher,
        IAppPreferencesService? prefsService = null,
        IImageProcessingService? imageService = null)
    {
        _excelRepo = excelRepo;
        _fileOrgService = fileOrgService;
        _pipelineService = pipelineService;
        _sdCardWatcher = sdCardWatcher;
        _prefsService = prefsService ?? new Paddock.Infrastructure.Configuration.AppPreferencesService();
        _imageService = imageService;

        JobManager = new JobManagerViewModel(_pipelineService);

        _excelRepo.LockContentionDetected += OnExcelLockContention;
        _sdCardWatcher.StartWatching();
    }

    private void OnExcelLockContention(object? sender, LockContentionEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LockBannerMessage = e.Message;
            IsLockBannerVisible = true;
        });
    }

    [RelayCommand]
    private void DismissLockBanner()
    {
        IsLockBannerVisible = false;
    }

    public async Task InitializeAsync()
    {
        await _prefsService.LoadAsync();

        if (_prefsService.AutoOpenLastDatabase &&
            !string.IsNullOrWhiteSpace(_prefsService.LastDatabasePath) &&
            File.Exists(_prefsService.LastDatabasePath))
        {
            await SwitchDatabaseAsync(_prefsService.LastDatabasePath);
        }
        else
        {
            ShowStartupDatabaseDialog();
        }
    }

    private void ShowStartupDatabaseDialog()
    {
        var startupVm = new DatabaseStartupDialogViewModel(_prefsService);
        startupVm.DatabaseSelected += async selectedPath =>
        {
            CloseModal();
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                await SwitchDatabaseAsync(selectedPath);
            }
            else
            {
                await LoadEventsAsync();
            }
        };

        CurrentModal = startupVm;
        IsModalOpen = true;
    }

    public async Task SwitchDatabaseAsync(string targetPath)
    {
        await _excelRepo.SwitchDatabaseAsync(targetPath);
        _prefsService.AddRecentDatabase(targetPath);
        await _prefsService.SaveAsync();
        OnPropertyChanged(nameof(DatabasePath));
        await LoadEventsAsync();
    }

    public async Task LoadEventsAsync()
    {
        var eventi = await _excelRepo.GetEventiAsync();
        AllEventi.Clear();
        foreach (var ev in eventi)
        {
            AllEventi.Add(ev);
        }

        ApplyEventFilter();

        if (SelectedEvento == null && FilteredEventi.Count > 0)
        {
            SelectedEvento = FilteredEventi[0];
        }
    }

    partial void OnEventSearchFilterChanged(string value)
    {
        ApplyEventFilter();
    }

    private void ApplyEventFilter()
    {
        FilteredEventi.Clear();
        var q = EventSearchFilter.Trim();
        var query = string.IsNullOrWhiteSpace(q)
            ? AllEventi
            : AllEventi.Where(e => 
                e.NomeEvento.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Luogo.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var ev in query)
        {
            FilteredEventi.Add(ev);
        }
    }

    partial void OnSelectedEventoChanged(Evento? value)
    {
        if (value == null)
        {
            ActiveEventDetail = null;
            return;
        }

        var detailVm = new EventDetailViewModel(value, _excelRepo, _fileOrgService);
        detailVm.RequestStartIngestion += OnStartIngestionRequested;
        detailVm.RequestEditEvent += OnEditEventRequested;
        detailVm.RequestDeleteEvent += OnDeleteEventRequested;

        ActiveEventDetail = detailVm;
        _ = detailVm.LoadEventDataAsync();
    }

    #region Modal Navigation & Actions

    [RelayCommand]
    private async Task OpenCreateEventDialog()
    {
        var basePath = await _excelRepo.GetBasePathAsync();
        var editVm = new EventEditDialogViewModel(defaultRootPath: basePath);
        editVm.RequestClose += async success =>
        {
            CloseModal();
            if (success)
            {
                await _excelRepo.UpsertEventoAsync(editVm.Evento);
                await LoadEventsAsync();
                SelectedEvento = AllEventi.FirstOrDefault(e => e.Id == editVm.Evento.Id);
            }
        };

        CurrentModal = editVm;
        IsModalOpen = true;
    }

    private async void OnEditEventRequested(Evento evento)
    {
        var basePath = await _excelRepo.GetBasePathAsync();
        var editVm = new EventEditDialogViewModel(evento, defaultRootPath: basePath);
        editVm.RequestClose += async success =>
        {
            CloseModal();
            if (success)
            {
                await _excelRepo.UpsertEventoAsync(editVm.Evento);
                await LoadEventsAsync();
                if (ActiveEventDetail != null)
                {
                    await ActiveEventDetail.LoadEventDataAsync();
                }
            }
        };

        CurrentModal = editVm;
        IsModalOpen = true;
    }

    private void OnDeleteEventRequested(Evento evento)
    {
        var deleteVm = new DeleteEventDialogViewModel(evento);
        deleteVm.RequestClose += async mode =>
        {
            CloseModal();

            if (mode == DeleteMode.DatabaseOnly)
            {
                // Opzione 1: Rimuove solo da Excel mantenendo i file
                await _excelRepo.DeleteEventoAsync(evento.Id);
                SelectedEvento = null;
                await LoadEventsAsync();
            }
            else if (mode == DeleteMode.DatabaseAndFiles)
            {
                // Opzione 2: Rimuove da Excel e cancella file fisici da disco
                await _excelRepo.DeleteEventoAsync(evento.Id);
                await _fileOrgService.DeleteEventFilesOnDiskAsync(evento.CartellaDestinazioneRoot, evento.NomeEvento);
                SelectedEvento = null;
                await LoadEventsAsync();
            }
        };

        CurrentModal = deleteVm;
        IsModalOpen = true;
    }

    [RelayCommand]
    private async Task OpenSettingsDialogAsync()
    {
        var settingsVm = new SettingsViewModel(_excelRepo, _prefsService, _imageService);
        await settingsVm.InitializeAsync();

        settingsVm.RequestClose += CloseModal;
        settingsVm.DatabaseChanged += async () =>
        {
            OnPropertyChanged(nameof(DatabasePath));
            await LoadEventsAsync();
        };
        settingsVm.BasePathChanged += async () =>
        {
            await LoadEventsAsync();
            if (ActiveEventDetail != null)
            {
                await ActiveEventDetail.LoadEventDataAsync();
            }
        };

        CurrentModal = settingsVm;
        IsModalOpen = true;
    }

    [RelayCommand]
    public void OpenSlideshowConfig()
    {
        var screens = GetAvailableScreens?.Invoke() ?? new List<DisplayScreenInfo>
        {
            new DisplayScreenInfo { Index = 0, DisplayName = "Schermo 1", Width = 1920, Height = 1080, IsPrimary = true }
        };

        var configVm = new SlideshowConfigViewModel(_excelRepo, AllEventi, screens, SelectedEvento);
        configVm.RequestClose += CloseModal;
        configVm.RequestStartSlideshow += config =>
        {
            CloseModal();
            RequestLaunchSlideshow?.Invoke(config);
        };

        CurrentModal = configVm;
        IsModalOpen = true;
    }

    [RelayCommand]
    public void StopSlideshow()
    {
        RequestStopSlideshow?.Invoke();
        IsSlideshowActive = false;
    }

    private async void OnStartIngestionRequested(Evento evento)
    {
        var atleti = await _excelRepo.GetAtletiByEventoAsync(evento.Id);
        var disc = await _excelRepo.GetDisciplineByEventoAsync(evento.Id);

        var wizardVm = new IngestionWizardViewModel(evento, atleti, disc, _sdCardWatcher, _prefsService, _imageService);
        wizardVm.RequestClose += async request =>
        {
            CloseModal();
            if (request != null)
            {
                // Inietta il BasePath configurato nel DB Excel se non già presente
                if (string.IsNullOrWhiteSpace(request.BasePath))
                {
                    request.BasePath = await _excelRepo.GetBasePathAsync();
                }

                await _pipelineService.EnqueueJobAsync(request);
            }
        };

        CurrentModal = wizardVm;
        IsModalOpen = true;
    }

    private void CloseModal()
    {
        IsModalOpen = false;
        CurrentModal = null;
    }

    #endregion
}

