using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.DTOs;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Infrastructure.Excel;
using Paddock.Infrastructure.Services;

namespace Paddock.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IExcelRepository _excelRepo;
    private readonly IAppPreferencesService _prefsService;
    private readonly IImageProcessingService? _imageService;
    private readonly IUpdateService _updateService;

    // Informazioni & Versioni
    public string CoreVersion { get; }
    public string InfrastructureVersion { get; }
    public string UiVersion { get; }
    public string CopyrightText { get; }
    public string GitHubUrl { get; } = "https://github.com/zmalrobot/Paddock";

    // Aggiornamenti Software
    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateStatusIsSuccess))]
    private string? _updateStatusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateStatusIsSuccess))]
    private bool _updateStatusIsError;

    public bool UpdateStatusIsSuccess => !string.IsNullOrWhiteSpace(UpdateStatusMessage) && !UpdateStatusIsError;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private UpdateInfo? _availableUpdateInfo;

    [ObservableProperty]
    private bool _checkUpdatesOnStartup;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _databaseFilePath = string.Empty;

    [ObservableProperty]
    private string _resolvedDatabasePath = string.Empty;

    [ObservableProperty]
    private string _basePath = string.Empty;

    [ObservableProperty]
    private string _resolvedBasePath = string.Empty;

    [ObservableProperty]
    private bool _autoOpenLastDatabase;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isErrorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<string> RecentDatabases { get; } = new();

    // Catalogo Prezzi
    public ObservableCollection<PrezzoCatalogoItem> CatalogoPrezzi { get; } = new();
    public List<CategoriaPrezzo> AvailableCategorie { get; } = Enum.GetValues<CategoriaPrezzo>().ToList();

    [ObservableProperty]
    private string _newItemNome = string.Empty;

    [ObservableProperty]
    private CategoriaPrezzo _newItemCategoria = CategoriaPrezzo.FotoSingola;

    [ObservableProperty]
    private decimal _newItemPrezzo = 10.00m;

    [ObservableProperty]
    private int _newItemQuantitaFoto = 1;

    [ObservableProperty]
    private string _newItemDescrizione = string.Empty;

    // Watermark & Metadati di Default
    [ObservableProperty]
    private bool _defaultWatermarkEnabled;

    [ObservableProperty]
    private string? _defaultWatermarkImagePath;

    [ObservableProperty]
    private float _defaultWatermarkOpacity = 0.65f;

    [ObservableProperty]
    private WatermarkPosition _defaultWatermarkPosition = WatermarkPosition.BottomRight;

    [ObservableProperty]
    private float _defaultWatermarkScalePercent = 0.20f;

    [ObservableProperty]
    private string _defaultPhotographerName = string.Empty;

    [ObservableProperty]
    private string _defaultCopyrightNotice = string.Empty;

    [ObservableProperty]
    private Bitmap? _watermarkPreviewBitmap;

    [ObservableProperty]
    private bool _isPreviewLoading;

    public List<WatermarkPosition> AvailablePositions { get; } = Enum.GetValues<WatermarkPosition>().ToList();

    public event Action? RequestClose;
    public event Func<Task>? DatabaseChanged;
    public event Func<Task>? BasePathChanged;

    public SettingsViewModel(
        IExcelRepository excelRepo,
        IAppPreferencesService prefsService,
        IImageProcessingService? imageService = null,
        IUpdateService? updateService = null)
    {
        _excelRepo = excelRepo;
        _prefsService = prefsService;
        _imageService = imageService;
        _updateService = updateService ?? new GitHubUpdateService();

        DatabaseFilePath = _excelRepo.DatabaseFilePath;
        ResolvedDatabasePath = _excelRepo.ResolvedDatabaseFilePath;
        AutoOpenLastDatabase = _prefsService.AutoOpenLastDatabase;

        // Inizializza Watermark e Metadati da preferenze
        DefaultWatermarkEnabled = _prefsService.DefaultWatermarkEnabled;
        DefaultWatermarkImagePath = _prefsService.DefaultWatermarkImagePath;
        DefaultWatermarkOpacity = _prefsService.DefaultWatermarkOpacity > 0 ? _prefsService.DefaultWatermarkOpacity : 0.65f;
        DefaultWatermarkPosition = _prefsService.DefaultWatermarkPosition;
        DefaultWatermarkScalePercent = _prefsService.DefaultWatermarkScalePercent > 0 ? _prefsService.DefaultWatermarkScalePercent : 0.20f;
        DefaultPhotographerName = _prefsService.DefaultPhotographerName ?? string.Empty;
        DefaultCopyrightNotice = _prefsService.DefaultCopyrightNotice ?? string.Empty;

        // Inizializza Informazioni di Versione e Autore
        CoreVersion = GetAssemblyVersion(typeof(Evento).Assembly);
        InfrastructureVersion = GetAssemblyVersion(typeof(ExcelRepository).Assembly);
        UiVersion = GetAssemblyVersion(typeof(SettingsViewModel).Assembly);
        CopyrightText = typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright © 2026 zmalrobot";
        _checkUpdatesOnStartup = _prefsService.CheckUpdatesOnStartup;

        RefreshRecentDatabases();
    }

    partial void OnDatabaseFilePathChanged(string value)
    {
        ResolvedDatabasePath = _excelRepo.ResolvedDatabaseFilePath;
    }

    partial void OnBasePathChanged(string value)
    {
        ResolvedBasePath = _excelRepo.ResolvePath(value);
    }

    partial void OnCheckUpdatesOnStartupChanged(bool value)
    {
        _prefsService.CheckUpdatesOnStartup = value;
        _ = _prefsService.SaveAsync();
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            var currentBase = await _excelRepo.GetBasePathAsync();
            BasePath = currentBase ?? string.Empty;
            ResolvedBasePath = _excelRepo.ResolvePath(BasePath);
            ResolvedDatabasePath = _excelRepo.ResolvedDatabaseFilePath;
            await LoadCatalogoPrezziAsync();
            await UpdateWatermarkPreviewAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore nel caricamento delle impostazioni: {ex.Message}";
            IsErrorMessage = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshRecentDatabases()
    {
        RecentDatabases.Clear();
        if (_prefsService.RecentDatabases != null)
        {
            foreach (var p in _prefsService.RecentDatabases)
            {
                if (!string.Equals(p, DatabaseFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    RecentDatabases.Add(p);
                }
            }
        }
    }

    [RelayCommand]
    public async Task UpdateBasePathAsync()
    {
        if (string.IsNullOrWhiteSpace(BasePath))
        {
            StatusMessage = "Specificare un percorso BasePath valido per l'archivio foto.";
            IsErrorMessage = true;
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = null;
            IsErrorMessage = false;

            var targetPath = BasePath.Trim();
            await _excelRepo.UpdateAllEventRootsAsync(targetPath);
            ResolvedBasePath = _excelRepo.ResolvePath(targetPath);

            StatusMessage = "BasePath aggiornato con successo nel foglio Impostazioni e in tutti gli eventi!";
            IsErrorMessage = false;

            if (BasePathChanged != null)
            {
                await BasePathChanged.Invoke();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore durante l'aggiornamento del BasePath: {ex.Message}";
            IsErrorMessage = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SwitchDatabaseAsync(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        try
        {
            IsBusy = true;
            StatusMessage = null;
            IsErrorMessage = false;

            await _excelRepo.SwitchDatabaseAsync(targetPath);
            DatabaseFilePath = _excelRepo.DatabaseFilePath;
            ResolvedDatabasePath = _excelRepo.ResolvedDatabaseFilePath;

            _prefsService.AddRecentDatabase(targetPath);
            await _prefsService.SaveAsync();
            RefreshRecentDatabases();

            var currentBase = await _excelRepo.GetBasePathAsync();
            BasePath = currentBase ?? string.Empty;
            ResolvedBasePath = _excelRepo.ResolvePath(BasePath);

            StatusMessage = "Database commutato con successo!";
            IsErrorMessage = false;

            if (DatabaseChanged != null)
            {
                await DatabaseChanged.Invoke();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore durante la selezione del database: {ex.Message}";
            IsErrorMessage = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ToggleAutoOpenLastDatabaseAsync()
    {
        _prefsService.AutoOpenLastDatabase = AutoOpenLastDatabase;
        await _prefsService.SaveAsync();
    }

    [RelayCommand]
    public async Task LoadCatalogoPrezziAsync()
    {
        try
        {
            var items = await _excelRepo.GetCatalogoPrezziAsync();
            CatalogoPrezzi.Clear();
            foreach (var item in items)
            {
                CatalogoPrezzi.Add(item);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore nel caricamento del catalogo prezzi: {ex.Message}";
            IsErrorMessage = true;
        }
    }

    [RelayCommand]
    public async Task AddPrezzoItemAsync()
    {
        if (string.IsNullOrWhiteSpace(NewItemNome))
        {
            StatusMessage = "Inserire un nome valido per l'articolo o pacchetto.";
            IsErrorMessage = true;
            return;
        }

        if (NewItemPrezzo < 0)
        {
            StatusMessage = "Il prezzo non può essere negativo.";
            IsErrorMessage = true;
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = null;
            IsErrorMessage = false;

            var item = new PrezzoCatalogoItem
            {
                Nome = NewItemNome.Trim(),
                Categoria = NewItemCategoria,
                Prezzo = NewItemPrezzo,
                QuantitaFotoIncluse = NewItemQuantitaFoto > 0 ? NewItemQuantitaFoto : 1,
                Descrizione = NewItemDescrizione.Trim()
            };

            await _excelRepo.UpsertPrezzoCatalogoItemAsync(item);
            CatalogoPrezzi.Add(item);

            NewItemNome = string.Empty;
            NewItemPrezzo = 10.00m;
            NewItemQuantitaFoto = 1;
            NewItemDescrizione = string.Empty;

            StatusMessage = $"Articolo '{item.Nome}' aggiunto al listino prezzi!";
            IsErrorMessage = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore durante l'aggiunta dell'articolo: {ex.Message}";
            IsErrorMessage = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeletePrezzoItemAsync(PrezzoCatalogoItem item)
    {
        if (item == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = null;
            IsErrorMessage = false;

            await _excelRepo.DeletePrezzoCatalogoItemAsync(item.Id);
            CatalogoPrezzi.Remove(item);

            StatusMessage = $"Articolo '{item.Nome}' rimosso dal listino prezzi.";
            IsErrorMessage = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore durante la rimozione dell'articolo: {ex.Message}";
            IsErrorMessage = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ResetDefaultCatalogoAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = null;
            IsErrorMessage = false;

            var defaults = ExcelRepository.GetDefaultCatalogoItems();
            await _excelRepo.SaveCatalogoPrezziAsync(defaults);
            CatalogoPrezzi.Clear();
            foreach (var item in defaults)
            {
                CatalogoPrezzi.Add(item);
            }

            StatusMessage = $"Listino prezzi ripristinato con i {defaults.Count} articoli predefiniti dal catalogo.";
            IsErrorMessage = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore nel ripristino del listino: {ex.Message}";
            IsErrorMessage = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    #region Watermark & Metadati

    public async Task UpdateWatermarkPreviewAsync()
    {
        if (_imageService == null) return;

        try
        {
            IsPreviewLoading = true;
            var options = new WatermarkOptions
            {
                Enabled = DefaultWatermarkEnabled,
                WatermarkImagePath = DefaultWatermarkImagePath,
                Opacity = DefaultWatermarkOpacity,
                Position = DefaultWatermarkPosition,
                ScalePercent = DefaultWatermarkScalePercent
            };

            var bytes = await _imageService.GenerateWatermarkPreviewJpegAsync(null, options);
            if (bytes != null && bytes.Length > 0)
            {
                using var ms = new MemoryStream(bytes);
                WatermarkPreviewBitmap = new Bitmap(ms);
            }
        }
        catch
        {
            // Ignora errori di rendering anteprima
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    partial void OnDefaultWatermarkEnabledChanged(bool value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnDefaultWatermarkImagePathChanged(string? value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnDefaultWatermarkOpacityChanged(float value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnDefaultWatermarkPositionChanged(WatermarkPosition value) => _ = UpdateWatermarkPreviewAsync();
    partial void OnDefaultWatermarkScalePercentChanged(float value) => _ = UpdateWatermarkPreviewAsync();

    [RelayCommand]
    public async Task SaveWatermarkSettingsAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = null;
            IsErrorMessage = false;

            _prefsService.DefaultWatermarkEnabled = DefaultWatermarkEnabled;
            _prefsService.DefaultWatermarkImagePath = DefaultWatermarkImagePath;
            _prefsService.DefaultWatermarkOpacity = DefaultWatermarkOpacity;
            _prefsService.DefaultWatermarkPosition = DefaultWatermarkPosition;
            _prefsService.DefaultWatermarkScalePercent = DefaultWatermarkScalePercent;
            _prefsService.DefaultPhotographerName = DefaultPhotographerName.Trim();
            _prefsService.DefaultCopyrightNotice = DefaultCopyrightNotice.Trim();

            await _prefsService.SaveAsync();

            StatusMessage = "Impostazioni predefinite di Watermark e Metadati salvate con successo!";
            IsErrorMessage = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore durante il salvataggio: {ex.Message}";
            IsErrorMessage = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
    #endregion

    #region Informazioni & GitHub
    [RelayCommand]
    public void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GitHubUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Impossibile aprire il browser: {ex.Message}";
            IsErrorMessage = true;
        }
    }

    private static string GetAssemblyVersion(Assembly assembly)
    {
        var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIndex = infoVer.IndexOf('+');
            return plusIndex > 0 ? infoVer[..plusIndex] : infoVer;
        }

        var ver = assembly.GetName().Version;
        if (ver != null)
        {
            return $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }

        return "0.5.8";
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates) return;

        IsCheckingUpdates = true;
        UpdateStatusMessage = "Verifica aggiornamenti su GitHub in corso...";
        UpdateStatusIsError = false;
        IsUpdateAvailable = false;
        AvailableUpdateInfo = null;

        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync(UiVersion, includePrerelease: true);

            if (!string.IsNullOrWhiteSpace(updateInfo.ErrorMessage))
            {
                UpdateStatusMessage = updateInfo.ErrorMessage;
                UpdateStatusIsError = true;
                return;
            }

            if (updateInfo.IsUpdateAvailable)
            {
                AvailableUpdateInfo = updateInfo;
                IsUpdateAvailable = true;
                UpdateStatusMessage = $"Nuova versione disponibile: v{updateInfo.NewVersion}";
                UpdateStatusIsError = false;
            }
            else
            {
                UpdateStatusMessage = $"Paddock è già aggiornato all'ultima versione (v{UiVersion}).";
                UpdateStatusIsError = false;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Errore durante la verifica: {ex.Message}";
            UpdateStatusIsError = true;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    public async Task ApplyUpdateAsync()
    {
        if (AvailableUpdateInfo == null || string.IsNullOrWhiteSpace(AvailableUpdateInfo.DownloadUrl))
        {
            UpdateStatusMessage = "Nessun pacchetto di aggiornamento valido da installare.";
            UpdateStatusIsError = true;
            return;
        }

        try
        {
            UpdateStatusMessage = "Avvio dell'utility di aggiornamento e chiusura applicazione...";
            UpdateStatusIsError = false;

            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            await _updateService.LaunchUpdaterAndExitAsync(AvailableUpdateInfo, appDir);
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Impossibile avviare l'aggiornamento: {ex.Message}";
            UpdateStatusIsError = true;
        }
    }
    #endregion

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke();
    }
}

