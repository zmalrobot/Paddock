using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;

namespace Paddock.UI.ViewModels;

public partial class DatabaseStartupDialogViewModel : ViewModelBase
{
    private readonly IAppPreferencesService _prefsService;

    [ObservableProperty]
    private string? _lastDatabasePath;

    [ObservableProperty]
    private bool _hasLastDatabase;

    [ObservableProperty]
    private bool _autoOpenLastDatabase;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<string> RecentDatabases { get; } = new();

    public event Action<string?>? DatabaseSelected;

    public DatabaseStartupDialogViewModel(IAppPreferencesService prefsService)
    {
        _prefsService = prefsService;

        LastDatabasePath = _prefsService.LastDatabasePath;
        HasLastDatabase = !string.IsNullOrWhiteSpace(LastDatabasePath) && File.Exists(LastDatabasePath);
        AutoOpenLastDatabase = _prefsService.AutoOpenLastDatabase;

        foreach (var p in _prefsService.RecentDatabases)
        {
            if (File.Exists(p))
            {
                RecentDatabases.Add(p);
            }
        }
    }

    [RelayCommand]
    private async Task ContinueWithLastDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(LastDatabasePath) || !File.Exists(LastDatabasePath))
        {
            ErrorMessage = "Il file dell'ultimo database non è più accessibile sul disco.";
            return;
        }

        await PersistAutoOpenAsync();
        DatabaseSelected?.Invoke(LastDatabasePath);
    }

    [RelayCommand]
    public async Task SelectDatabasePathAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        await PersistAutoOpenAsync();
        DatabaseSelected?.Invoke(filePath);
    }

    [RelayCommand]
    public async Task PersistAutoOpenAsync()
    {
        _prefsService.AutoOpenLastDatabase = AutoOpenLastDatabase;
        await _prefsService.SaveAsync();
    }
}

