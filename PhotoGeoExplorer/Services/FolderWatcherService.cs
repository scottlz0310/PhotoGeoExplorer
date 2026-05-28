using System;
using System.IO;
using System.Threading;

namespace PhotoGeoExplorer.Services;

internal sealed class FolderWatcherService : IFolderWatcherService
{
    private readonly TimeSpan _debounceInterval;
    private readonly TimeSpan _pollingInterval;

    private readonly object _timerLock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private Timer? _pollingTimer;
    private string? _currentFolderPath;
    private bool _disposed;

    public event EventHandler? FolderChanged;

    public FolderWatcherService(
        TimeSpan? debounceInterval = null,
        TimeSpan? pollingInterval = null)
    {
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(500);
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(60);
    }

    public void Watch(string folderPath)
    {
        Stop();
        _currentFolderPath = folderPath;

        var watcherStarted = false;
        try
        {
            var watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            watcher.Created += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Changed += OnFileSystemEvent;
            watcher.Renamed += OnFileSystemEvent;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            watcherStarted = true;
            AppLog.Info($"FolderWatcherService: Watching '{folderPath}'");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            AppLog.Info($"FolderWatcherService: FileSystemWatcher unavailable for '{folderPath}', falling back to polling. {ex.Message}");
        }

        if (!watcherStarted)
        {
            _pollingTimer = new Timer(
                _ => RaiseFolderChanged(),
                null,
                _pollingInterval,
                _pollingInterval);
        }
    }

    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileSystemEvent;
            _watcher.Deleted -= OnFileSystemEvent;
            _watcher.Changed -= OnFileSystemEvent;
            _watcher.Renamed -= OnFileSystemEvent;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }

        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        _pollingTimer?.Dispose();
        _pollingTimer = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        ResetDebounceTimer();
    }

    private void ResetDebounceTimer()
    {
        lock (_timerLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_debounceTimer is null)
            {
                _debounceTimer = new Timer(
                    _ => RaiseFolderChanged(),
                    null,
                    _debounceInterval,
                    Timeout.InfiniteTimeSpan);
            }
            else
            {
                _debounceTimer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // フォルダ切り替え中に古い watcher のエラーが遅延発火した場合は無視する
        if (!ReferenceEquals(sender, _watcher))
        {
            return;
        }

        AppLog.Error("FolderWatcherService: FileSystemWatcher error, restarting watcher.", e.GetException());

        var folderPath = _currentFolderPath;
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            Watch(folderPath);
            RaiseFolderChanged();
        }
    }

    private void RaiseFolderChanged()
    {
        if (_disposed)
        {
            return;
        }

        FolderChanged?.Invoke(this, EventArgs.Empty);
    }
}
