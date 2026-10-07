using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;

namespace Paddock.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IExcelRepository _excelRepo;
    private readonly IAppPreferencesService _prefsService;

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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore nel caricamento del BasePath: {ex.Message}";
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
        foreach (var p in _prefsService.RecentDatabases)
        {
            if (!string.Equals(p, DatabaseFilePath, StringComparison.OrdinalIgnoreCase))
            {
                RecentDatabases.Add(p);
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
    private void Close()
    {
        RequestClose?.Invoke();
    }
}

