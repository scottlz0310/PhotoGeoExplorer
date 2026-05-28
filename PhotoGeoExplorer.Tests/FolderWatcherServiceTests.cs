using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class FolderWatcherServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FolderWatcherServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // =========================================================
    // Watch / FolderChanged
    // =========================================================

    [Fact]
    public async Task Watch_FileCreated_RaisesFolderChanged()
    {
        using var service = new FolderWatcherService(
            debounceInterval: TimeSpan.FromMilliseconds(50));

        var tcs = new TaskCompletionSource<bool>();
        service.FolderChanged += (_, _) => tcs.TrySetResult(true);
        service.Watch(_tempDir);

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "new.txt"), "x").ConfigureAwait(true);

        var raised = await Task.WhenAny(tcs.Task, Task.Delay(3000)).ConfigureAwait(true);
        Assert.Equal(tcs.Task, raised);
    }

    [Fact]
    public async Task Watch_FileDeleted_RaisesFolderChanged()
    {
        var file = Path.Combine(_tempDir, "del.txt");
        await File.WriteAllTextAsync(file, "x").ConfigureAwait(true);

        using var service = new FolderWatcherService(
            debounceInterval: TimeSpan.FromMilliseconds(50));

        var tcs = new TaskCompletionSource<bool>();
        service.FolderChanged += (_, _) => tcs.TrySetResult(true);
        service.Watch(_tempDir);

        File.Delete(file);

        var raised = await Task.WhenAny(tcs.Task, Task.Delay(3000)).ConfigureAwait(true);
        Assert.Equal(tcs.Task, raised);
    }

    [Fact]
    public async Task Watch_BurstyEvents_DebouncesToSingleNotification()
    {
        using var service = new FolderWatcherService(
            debounceInterval: TimeSpan.FromMilliseconds(200));

        var count = 0;
        service.FolderChanged += (_, _) => Interlocked.Increment(ref count);
        service.Watch(_tempDir);

        for (var i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_tempDir, $"burst{i}.txt"), "x").ConfigureAwait(true);
            await Task.Delay(10).ConfigureAwait(true);
        }

        await Task.Delay(500).ConfigureAwait(true);

        Assert.Equal(1, count);
    }

    // =========================================================
    // Stop / Dispose
    // =========================================================

    [Fact]
    public async Task Stop_AfterWatch_NoFurtherNotifications()
    {
        using var service = new FolderWatcherService(
            debounceInterval: TimeSpan.FromMilliseconds(50));

        var count = 0;
        service.FolderChanged += (_, _) => Interlocked.Increment(ref count);
        service.Watch(_tempDir);
        service.Stop();

        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "after_stop.txt"), "x").ConfigureAwait(true);

        await Task.Delay(300).ConfigureAwait(true);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Dispose_StopsWatching()
    {
        var service = new FolderWatcherService(
            debounceInterval: TimeSpan.FromMilliseconds(50));

        var count = 0;
        service.FolderChanged += (_, _) => Interlocked.Increment(ref count);
        service.Watch(_tempDir);
        service.Dispose();

        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "after_dispose.txt"), "x").ConfigureAwait(true);

        await Task.Delay(300).ConfigureAwait(true);

        Assert.Equal(0, count);
    }

    // =========================================================
    // Polling fallback
    // =========================================================

    [Fact]
    public async Task Watch_PollingFallback_RaisesFolderChangedPeriodically()
    {
        // 存在しないパスは FileSystemWatcher が失敗しポーリングにフォールバックする
        var nonExistentPath = Path.Combine(_tempDir, "does_not_exist");

        using var service = new FolderWatcherService(
            pollingInterval: TimeSpan.FromMilliseconds(100));

        var count = 0;
        service.FolderChanged += (_, _) => Interlocked.Increment(ref count);
        service.Watch(nonExistentPath);

        await Task.Delay(350).ConfigureAwait(true);

        Assert.True(count >= 2, $"Expected at least 2 polling ticks but got {count}");
    }

    // =========================================================
    // Watch 再呼び出し（フォルダ切り替え）
    // =========================================================

    [Fact]
    public async Task Watch_CalledTwice_WatchesLatestFolder()
    {
        var dir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir2);
        try
        {
            using var service = new FolderWatcherService(
                debounceInterval: TimeSpan.FromMilliseconds(50));

            var tcs = new TaskCompletionSource<bool>();
            service.FolderChanged += (_, _) => tcs.TrySetResult(true);

            service.Watch(_tempDir);
            service.Watch(dir2);

            await File.WriteAllTextAsync(
                Path.Combine(dir2, "new.txt"), "x").ConfigureAwait(true);

            var raised = await Task.WhenAny(tcs.Task, Task.Delay(3000)).ConfigureAwait(true);
            Assert.Equal(tcs.Task, raised);
        }
        finally
        {
            try { Directory.Delete(dir2, recursive: true); } catch { }
        }
    }
}
