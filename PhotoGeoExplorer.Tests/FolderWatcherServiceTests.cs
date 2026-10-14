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
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void BurstyEvents_DebounceToSingleNotificationPerBurst(int eventsPerBurst)
    {
        // 実時間・OS のイベント到着に依存すると、負荷の高い CI でバーストが分割されるため、
        // 手動時計とデバウンス入口の直接呼び出しで決定的に検証する。
        var debounce = TimeSpan.FromMilliseconds(200);
        var clock = new ManualTimeProvider();
        using var service = new FolderWatcherService(debounceInterval: debounce, timeProvider: clock);

        var count = 0;
        service.FolderChanged += (_, _) => count++;

        for (var burst = 1; burst <= 2; burst++)
        {
            for (var i = 0; i < eventsPerBurst; i++)
            {
                service.ResetDebounceTimer();
                clock.Advance(debounce - TimeSpan.FromMilliseconds(1));
                Assert.Equal(burst - 1, count);
            }

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(burst, count);

            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(burst, count);
        }
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
            try { Directory.Delete(dir2, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            var target = _now + delta;
            while (true)
            {
                var next = _timers
                    .Where(t => t.DueAt is not null && t.DueAt <= target)
                    .OrderBy(t => t.DueAt)
                    .FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                _now = next.DueAt!.Value;
                next.Fire();
            }

            _now = target;
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan _period = Timeout.InfiniteTimeSpan;

            public DateTimeOffset? DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                _period = period;
                return true;
            }

            public void Fire()
            {
                DueAt = _period == Timeout.InfiniteTimeSpan ? null : owner._now + _period;
                callback(state);
            }

            public void Dispose()
            {
                DueAt = null;
                owner._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
