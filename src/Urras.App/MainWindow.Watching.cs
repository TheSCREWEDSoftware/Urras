using Avalonia.Threading;

namespace Urras.App;

// Refreshes only after a change on disk or a long gap, so switching windows doesn't start a burst of Git commands
public partial class MainWindow
{
    // Edits arrive in bursts (a save, a build, a checkout), so a refresh waits for them to settle
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(400);

    // Coming back to the window refreshes anyway after this long, in case a change went unnoticed
    private static readonly TimeSpan ActivationRefresh = TimeSpan.FromMinutes(2);

    // The other repositories' labels are refreshed at most this often when coming back to the window
    private static readonly TimeSpan SummaryRefresh = TimeSpan.FromMinutes(1);

    private readonly DispatcherTimer _settleTimer = new DispatcherTimer();
    private FileSystemWatcher? _watcher;
    private DateTime _lastRefresh = DateTime.MinValue;
    private DateTime _lastSummaries = DateTime.MinValue;
    private volatile bool _changedOnDisk = true;

    private void InitializeWatching()
    {
        _settleTimer.Interval = SettleTime;
        _settleTimer.Tick += async (_, _) =>
        {
            _settleTimer.Stop();
            if (IsActive && _changedOnDisk)
                await RefreshAsync();
        };

        Activated += async (_, _) =>
        {
            if (_changedOnDisk || DateTime.UtcNow - _lastRefresh > ActivationRefresh)
                await RefreshAsync();
            if (DateTime.UtcNow - _lastSummaries > SummaryRefresh)
                await RefreshSummariesAsync();
        };
        Closed += (_, _) => StopWatching();
    }

    // Watches the working tree and .git (commits, fetches and branch switches from other tools land there)
    private void WatchRepository(string? path)
    {
        StopWatching();
        _changedOnDisk = true;
        if (path == null || !Directory.Exists(path))
            return;

        try
        {
            FileSystemWatcher watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                               NotifyFilters.Size
            };
            watcher.Changed += (_, _) => OnDiskChanged();
            watcher.Created += (_, _) => OnDiskChanged();
            watcher.Deleted += (_, _) => OnDiskChanged();
            watcher.Renamed += (_, _) => OnDiskChanged();
            // Too many changes at once overflow the watcher; a refresh catches up with all of them
            watcher.Error += (_, _) => OnDiskChanged();
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or
                                              UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Some network drives can't be watched; refreshing on coming back to the window still works
            _watcher = null;
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    // Called on the watcher's own thread
    private void OnDiskChanged()
    {
        _changedOnDisk = true;
        Dispatcher.UIThread.Post(() =>
        {
            _settleTimer.Stop();
            _settleTimer.Start();
        });
    }

    // A refresh is starting; changes from here on need another one
    private void MarkRefreshed()
    {
        _changedOnDisk = false;
        _lastRefresh = DateTime.UtcNow;
    }
}
