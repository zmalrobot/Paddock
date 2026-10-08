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

    public static string ResolveDbPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    public DatabaseStartupDialogViewModel(IAppPreferencesService prefsService)
    {
        _prefsService = prefsService;

        LastDatabasePath = _prefsService.LastDatabasePath;
        var resolvedLast = ResolveDbPath(LastDatabasePath);
        HasLastDatabase = !string.IsNullOrWhiteSpace(resolvedLast) && File.Exists(resolvedLast);
        AutoOpenLastDatabase = _prefsService.AutoOpenLastDatabase;

        // Se non c'è un ultimo database configurato ma esiste già la struttura standard ..\Database\Paddock_Database.xlsx
        if (!HasLastDatabase && string.IsNullOrWhiteSpace(LastDatabasePath))
        {
            var defaultRel = Path.Combine("..", "Database", "Paddock_Database.xlsx");
            var defaultAbs = ResolveDbPath(defaultRel);
            if (File.Exists(defaultAbs))
            {
                LastDatabasePath = defaultRel;
                HasLastDatabase = true;
            }
        }

        foreach (var p in _prefsService.RecentDatabases)
        {
            if (File.Exists(ResolveDbPath(p)))
            {
                RecentDatabases.Add(p);
            }
        }
    }

    [RelayCommand]
    private async Task ContinueWithLastDatabaseAsync()
    {
        var resolved = ResolveDbPath(LastDatabasePath);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
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

