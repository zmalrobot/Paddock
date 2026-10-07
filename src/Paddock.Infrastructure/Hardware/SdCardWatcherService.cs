using Paddock.Core.Interfaces;

namespace Paddock.Infrastructure.Hardware;

public class SdCardWatcherService : ISdCardWatcherService
{
    private readonly Timer _timer;
    private readonly HashSet<string> _knownDrives = new(StringComparer.OrdinalIgnoreCase);
    private bool _isWatching;
    private readonly object _lock = new();

    public event EventHandler<List<RemovableDriveInfo>>? RemovableDrivesChanged;

    public SdCardWatcherService()
    {
        _timer = new Timer(OnPollTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void StartWatching()
    {
        lock (_lock)
        {
            if (_isWatching) return;
            _isWatching = true;
            _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(3));
        }
    }

    public void StopWatching()
    {
        lock (_lock)
        {
            _isWatching = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public List<RemovableDriveInfo> GetCurrentRemovableDrives()
    {
        var result = new List<RemovableDriveInfo>();

        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var d in drives)
            {
                try
                {
                    if (d.IsReady && (d.DriveType == DriveType.Removable || IsPotentialCameraCard(d.RootDirectory.FullName)))
                    {
                        var dcimPath = FindDcimDirectory(d.RootDirectory.FullName);
                        result.Add(new RemovableDriveInfo
                        {
                            RootDirectory = d.RootDirectory.FullName,
                            VolumeLabel = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Scheda SD" : d.VolumeLabel,
                            TotalSize = d.TotalSize,
                            AvailableFreeSpace = d.AvailableFreeSpace,
                            DcimPath = dcimPath
                        });
                    }
                }
                catch
                {
                    // Possibile Drive temporaneamente non pronto
                }
            }
        }
        catch
        {
            // Errore generico DriveInfo
        }

        return result;
    }

    private void OnPollTimer(object? state)
    {
        try
        {
            var current = GetCurrentRemovableDrives();
            var currentRoots = new HashSet<string>(current.Select(d => d.RootDirectory), StringComparer.OrdinalIgnoreCase);

            bool changed;
            lock (_lock)
            {
                changed = !_knownDrives.SetEquals(currentRoots);
                if (changed)
                {
                    _knownDrives.Clear();
                    foreach (var r in currentRoots) _knownDrives.Add(r);
                }
            }

            if (changed)
            {
                RemovableDrivesChanged?.Invoke(this, current);
            }
        }
        catch
        {
            // Evita crash del timer di polling
        }
    }

    private static bool IsPotentialCameraCard(string rootPath)
    {
        try
        {
            var dcim = Path.Combine(rootPath, "DCIM");
            return Directory.Exists(dcim);
        }
        catch
        {
            return false;
        }
    }

    private static string FindDcimDirectory(string rootPath)
    {
        try
        {
            var dcim = Path.Combine(rootPath, "DCIM");
            if (Directory.Exists(dcim)) return dcim;
        }
        catch
        {
        }
        return rootPath;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}

