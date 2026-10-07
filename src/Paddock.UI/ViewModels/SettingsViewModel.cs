using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;
using Paddock.Core.Models;
using Paddock.Infrastructure.Excel;

namespace Paddock.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IExcelRepository _excelRepo;
    private readonly IAppPreferencesService _prefsService;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _databaseFilePath = string.Empty;

    [ObservableProperty]
    private string _basePath = string.Empty;

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

    public event Action? RequestClose;
    public event Func<Task>? DatabaseChanged;
    public event Func<Task>? BasePathChanged;

    public SettingsViewModel(IExcelRepository excelRepo, IAppPreferencesService prefsService)
    {
        _excelRepo = excelRepo;
        _prefsService = prefsService;

        DatabaseFilePath = _excelRepo.DatabaseFilePath;
        AutoOpenLastDatabase = _prefsService.AutoOpenLastDatabase;

        RefreshRecentDatabases();
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            var currentBase = await _excelRepo.GetBasePathAsync();
            BasePath = currentBase ?? string.Empty;
            await LoadCatalogoPrezziAsync();
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

            _prefsService.AddRecentDatabase(targetPath);
            await _prefsService.SaveAsync();
            RefreshRecentDatabases();

            var currentBase = await _excelRepo.GetBasePathAsync();
            BasePath = currentBase ?? string.Empty;

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

            StatusMessage = "Listino prezzi ripristinato con i 9 pacchetti predefiniti consigliati.";
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

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke();
    }
}

