using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paddock.Core.Interfaces;

namespace Paddock.UI.ViewModels;

public class RecentDatabaseItemViewModel
{
    public string Path { get; }
    public string EngineBadge { get; }
    public string BadgeBackground { get; }
    public bool IsSqlite { get; }

    public RecentDatabaseItemViewModel(string path)
    {
        Path = path;
        var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
        IsSqlite = ext switch
        {
            ".xlsx" or ".xlsm" or ".xls" => false,
            _ => true
        };
        EngineBadge = IsSqlite ? "SQLITE" : "EXCEL";
        BadgeBackground = IsSqlite ? "#2ECC71" : "#00B4D8";
    }
}

public partial class DatabaseStartupDialogViewModel : ViewModelBase
{
    private readonly IAppPreferencesService _prefsService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastDatabaseEngineBadge))]
    [NotifyPropertyChangedFor(nameof(LastDatabaseBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(IsLastDatabaseSqlite))]
    private string? _lastDatabasePath;

    [ObservableProperty]
    private bool _hasLastDatabase;

    [ObservableProperty]
    private bool _autoOpenLastDatabase;

    [ObservableProperty]
    private string? _errorMessage;

    public string LastDatabaseEngineBadge => IsLastDatabaseSqlite ? "SQLITE" : "EXCEL";
    public string LastDatabaseBadgeBackground => IsLastDatabaseSqlite ? "#2ECC71" : "#00B4D8";

    public bool IsLastDatabaseSqlite
    {
        get
        {
            var ext = Path.GetExtension(LastDatabasePath)?.ToLowerInvariant();
            return ext != ".xlsx" && ext != ".xlsm" && ext != ".xls";
        }
    }

    public ObservableCollection<RecentDatabaseItemViewModel> RecentDatabases { get; } = new();

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

        // Se non c'è un ultimo database configurato, cerca se esiste la struttura standard
        if (!HasLastDatabase && string.IsNullOrWhiteSpace(LastDatabasePath))
        {
            var defaultSqliteRel = Path.Combine("..", "Database", "Paddock_Database.db");
            var defaultSqliteAbs = ResolveDbPath(defaultSqliteRel);
            var defaultExcelRel = Path.Combine("..", "Database", "Paddock_Database.xlsx");
            var defaultExcelAbs = ResolveDbPath(defaultExcelRel);

            if (File.Exists(defaultSqliteAbs))
            {
                LastDatabasePath = defaultSqliteRel;
                HasLastDatabase = true;
            }
            else if (File.Exists(defaultExcelAbs))
            {
                LastDatabasePath = defaultExcelRel;
                HasLastDatabase = true;
            }
        }

        foreach (var p in _prefsService.RecentDatabases)
        {
            if (File.Exists(ResolveDbPath(p)))
            {
                RecentDatabases.Add(new RecentDatabaseItemViewModel(p));
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
