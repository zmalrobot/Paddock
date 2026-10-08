using System.Text.Json;
using Paddock.Core.Enums;
using Paddock.Core.Interfaces;

namespace Paddock.Infrastructure.Configuration;

public class AppPreferencesService : IAppPreferencesService
{
    private readonly string _preferencesFilePath;
    private PreferencesData _data = new();

    public string? LastDatabasePath
    {
        get => _data.LastDatabasePath;
        set => _data.LastDatabasePath = value;
    }

    public List<string> RecentDatabases => _data.RecentDatabases;

    public bool AutoOpenLastDatabase
    {
        get => _data.AutoOpenLastDatabase;
        set => _data.AutoOpenLastDatabase = value;
    }

    public bool DefaultWatermarkEnabled
    {
        get => _data.DefaultWatermarkEnabled;
        set => _data.DefaultWatermarkEnabled = value;
    }

    public string? DefaultWatermarkImagePath
    {
        get => _data.DefaultWatermarkImagePath;
        set => _data.DefaultWatermarkImagePath = value;
    }

    public float DefaultWatermarkOpacity
    {
        get => _data.DefaultWatermarkOpacity;
        set => _data.DefaultWatermarkOpacity = value;
    }

    public WatermarkPosition DefaultWatermarkPosition
    {
        get => _data.DefaultWatermarkPosition;
        set => _data.DefaultWatermarkPosition = value;
    }

    public float DefaultWatermarkScalePercent
    {
        get => _data.DefaultWatermarkScalePercent;
        set => _data.DefaultWatermarkScalePercent = value;
    }

    public string DefaultPhotographerName
    {
        get => _data.DefaultPhotographerName;
        set => _data.DefaultPhotographerName = value;
    }

    public string DefaultCopyrightNotice
    {
        get => _data.DefaultCopyrightNotice;
        set => _data.DefaultCopyrightNotice = value;
    }

    public bool DefaultAutoRotate
    {
        get => _data.DefaultAutoRotate;
        set => _data.DefaultAutoRotate = value;
    }

    public bool CheckUpdatesOnStartup
    {
        get => _data.CheckUpdatesOnStartup;
        set => _data.CheckUpdatesOnStartup = value;
    }

    public AppPreferencesService(string? customConfigPath = null)
    {
        if (string.IsNullOrWhiteSpace(customConfigPath))
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Paddock");
            Directory.CreateDirectory(appData);
            _preferencesFilePath = Path.Combine(appData, "preferences.json");
        }
        else
        {
            _preferencesFilePath = customConfigPath;
        }
    }

    public void AddRecentDatabase(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        _data.RecentDatabases.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _data.RecentDatabases.Insert(0, path);

        if (_data.RecentDatabases.Count > 10)
        {
            _data.RecentDatabases = _data.RecentDatabases.Take(10).ToList();
        }

        LastDatabasePath = path;
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_preferencesFilePath))
            {
                var json = File.ReadAllText(_preferencesFilePath);
                var loaded = JsonSerializer.Deserialize<PreferencesData>(json);
                if (loaded != null)
                {
                    _data = loaded;
                }
            }
        }
        catch
        {
            _data = new PreferencesData();
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_preferencesFilePath))
            {
                var json = await File.ReadAllTextAsync(_preferencesFilePath).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<PreferencesData>(json);
                if (loaded != null)
                {
                    _data = loaded;
                }
            }
        }
        catch
        {
            // Fallback su configurazione vuota in caso di corruzione del json
            _data = new PreferencesData();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_preferencesFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_preferencesFilePath, json).ConfigureAwait(false);
        }
        catch
        {
            // Ignora errori di salvataggio preferenze
        }
    }

    private class PreferencesData
    {
        public string? LastDatabasePath { get; set; }
        public List<string> RecentDatabases { get; set; } = new();
        public bool AutoOpenLastDatabase { get; set; } = false;

        public bool DefaultWatermarkEnabled { get; set; } = false;
        public string? DefaultWatermarkImagePath { get; set; }
        public float DefaultWatermarkOpacity { get; set; } = 0.65f;
        public WatermarkPosition DefaultWatermarkPosition { get; set; } = WatermarkPosition.BottomRight;
        public float DefaultWatermarkScalePercent { get; set; } = 0.20f;
        public string DefaultPhotographerName { get; set; } = string.Empty;
        public string DefaultCopyrightNotice { get; set; } = string.Empty;
        public bool DefaultAutoRotate { get; set; } = true;
        public bool CheckUpdatesOnStartup { get; set; } = true;
    }
}
