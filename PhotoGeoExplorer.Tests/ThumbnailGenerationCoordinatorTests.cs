using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// ThumbnailGenerationCoordinator のテスト。
/// 生成パイプライン・並列制御・バッチ更新・キャンセル・Dispose 時の安全性を
/// 公開 API（コンストラクタ注入・internal メンバー）経由で検証する。
/// </summary>
public sealed class ThumbnailGenerationCoordinatorTests
{
    private const int WaitTimeoutMs = 5000;

    [Fact]
    public async Task StartGenerationGeneratesThumbnailsOnlyForItemsNeedingThem()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        try
        {
            var targetPath = CreateTempFile(tempDir, "target.jpg");
            var noKeyPath = CreateTempFile(tempDir, "no-key.jpg");

            var generatedPaths = new List<string>();
            var generatedPathsLock = new object();
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = CreateCoordinator(dispatcher, (path, _) =>
            {
                lock (generatedPathsLock)
                {
                    generatedPaths.Add(path);
                }

                return (null, null, null);
            });

            var items = new List<PhotoListItem>
            {
                CreateListItem(targetPath, thumbnailKey: "key-target"),
                CreateListItem(noKeyPath, thumbnailKey: null),
                CreateFolderListItem(tempDir),
            };

            // Act
            coordinator.StartGeneration(items);
            await Task.WhenAll(coordinator.GetActiveTasksSnapshot()).ConfigureAwait(true);

            // Assert: ThumbnailKey を持つ非フォルダのアイテムのみ生成対象になる
            lock (generatedPathsLock)
            {
                var generated = Assert.Single(generatedPaths);
                Assert.Equal(targetPath, generated);
            }
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task StartGenerationLimitsConcurrencyWithSemaphore()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        try
        {
            var items = new List<PhotoListItem>();
            for (var i = 0; i < 8; i++)
            {
                var path = CreateTempFile(tempDir, $"photo{i}.jpg");
                items.Add(CreateListItem(path, thumbnailKey: $"key{i}"));
            }

            var running = 0;
            var maxObserved = 0;
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = CreateCoordinator(dispatcher, (_, _) =>
            {
                var current = Interlocked.Increment(ref running);
                int snapshot;
                while (current > (snapshot = Volatile.Read(ref maxObserved)))
                {
                    Interlocked.CompareExchange(ref maxObserved, current, snapshot);
                }

                Thread.Sleep(30);
                Interlocked.Decrement(ref running);
                return (null, null, null);
            });

            // Act
            coordinator.StartGeneration(items);
            await Task.WhenAll(coordinator.GetActiveTasksSnapshot()).ConfigureAwait(true);

            // Assert: 同時実行数がセマフォの上限（3）を超えない
            Assert.InRange(Volatile.Read(ref maxObserved), 1, 3);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task TimerTickAppliesPendingUpdatesAndStopsTimerWhenCompleted()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        try
        {
            var photoPath = CreateTempFile(tempDir, "photo.jpg");
            var takenAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = CreateCoordinator(
                dispatcher,
                (_, _) => (Path.Combine(tempDir, "thumb.png"), 200, 100),
                isJpegFile: _ => true,
                getMetadataAsync: (_, _) => Task.FromResult<PhotoMetadata?>(
                    new PhotoMetadata(takenAt, null, null, 35.0, 139.0, hasGpsData: true)));

            var item = CreateListItem(photoPath, thumbnailKey: "key1");

            // Act
            coordinator.StartGeneration(new List<PhotoListItem> { item });
            await Task.WhenAll(coordinator.GetActiveTasksSnapshot()).ConfigureAwait(true);

            var timer = dispatcher.LastTimer;
            Assert.NotNull(timer);
            Assert.True(timer.IsRunning);
            timer.FireTick();

            // Assert: 保留中の更新がアイテムへ適用される（createThumbnailImage は null を
            // 返すテストシームのため、解像度とメタデータの反映を検証する）
            Assert.Equal("200 x 100", item.ResolutionText);
            Assert.Equal(takenAt, item.TakenAt);
            Assert.True(item.HasLocation);

            // Assert: 全タスク完了かつキューが空になったためタイマーが停止する
            Assert.False(timer.IsRunning);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CancelStopsTimerAndDiscardsInFlightResults()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        try
        {
            var photoPath = CreateTempFile(tempDir, "photo.jpg");
            using var generationStarted = new ManualResetEventSlim(initialState: false);
            using var releaseGeneration = new ManualResetEventSlim(initialState: false);
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = CreateCoordinator(dispatcher, (_, _) =>
            {
                generationStarted.Set();
                releaseGeneration.Wait(WaitTimeoutMs);
                return (Path.Combine(tempDir, "thumb.png"), 200, 100);
            });

            var item = CreateListItem(photoPath, thumbnailKey: "key1");
            coordinator.StartGeneration(new List<PhotoListItem> { item });
            Assert.True(generationStarted.Wait(WaitTimeoutMs));
            var timer = dispatcher.LastTimer;
            Assert.NotNull(timer);
            var pendingTasks = coordinator.GetActiveTasksSnapshot();

            // Act: 生成処理の実行中にキャンセル
            coordinator.Cancel();
            releaseGeneration.Set();
            await Task.WhenAll(pendingTasks).ConfigureAwait(true);

            // Assert: タイマーが停止し、生成済みの結果はアイテムへ適用されない
            Assert.False(timer.IsRunning);
            timer.FireTick();
            Assert.Equal(string.Empty, item.ResolutionText);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    /// <summary>
    /// 回帰テスト(タスク上書き): フォルダ切り替えで生成バッチが複数作られても旧タスクが
    /// 失われず、Dispose() が全タスクの完了を待ってからセマフォを破棄することを検証する。
    /// </summary>
    [Fact]
    public void DisposeWhileMultipleThumbnailTasksRunning_WaitsForAllAndDoesNotThrow()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        try
        {
            var photo1 = CreateTempFile(tempDir, "photo1.jpg");
            var photo2 = CreateTempFile(tempDir, "photo2.jpg");

            var started = 0;
            var completed = 0;
            var dispatcher = new FakeUiDispatcher();
            var coordinator = CreateCoordinator(dispatcher, (_, _) =>
            {
                Interlocked.Increment(ref started);
                Thread.Sleep(40);
                Interlocked.Increment(ref completed);
                return (null, null, null);
            });

            // フォルダ切り替えを模擬: 1 つ目のバッチ実行中に 2 つ目のバッチを開始する
            coordinator.StartGeneration(new List<PhotoListItem> { CreateListItem(photo1, thumbnailKey: "key1") });
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref started) >= 1, WaitTimeoutMs));
            coordinator.StartGeneration(new List<PhotoListItem> { CreateListItem(photo2, thumbnailKey: "key2") });
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref started) >= 2, WaitTimeoutMs));

            // Act: 2 つのバッチタスクが実行中に Dispose
            coordinator.Dispose();

            // Assert: Dispose が両バッチの生成処理の完了を待っている
            Assert.Equal(2, Volatile.Read(ref completed));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    /// <summary>
    /// 回帰テスト(タイムアウト): Dispose のタイムアウトを短くして待機を超過させても
    /// GenerateThumbnailAsync が ObjectDisposedException を捕捉し クラッシュしないことを検証する。
    /// </summary>
    [Fact]
    public async Task DisposeWithTimeoutExceeded_TaskCatchesObjectDisposedExceptionAndDoesNotCrash()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        try
        {
            var photoPath = CreateTempFile(tempDir, "photo.jpg");
            var started = 0;
            var dispatcher = new FakeUiDispatcher();
            var coordinator = CreateCoordinator(dispatcher, (_, _) =>
            {
                Interlocked.Increment(ref started);
                Thread.Sleep(200); // タイムアウト（10ms）を超過する処理
                return (null, null, null);
            });

            // タイムアウトを極端に短くする（テスト専用フィールド）
            coordinator.DisposeTimeout = TimeSpan.FromMilliseconds(10);

            coordinator.StartGeneration(new List<PhotoListItem> { CreateListItem(photoPath, thumbnailKey: "key1") });
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref started) >= 1, WaitTimeoutMs));
            var pendingTasks = coordinator.GetActiveTasksSnapshot();
            Assert.NotEmpty(pendingTasks);

            // Act: タイムアウト前に Dispose が戻り、セマフォが先に破棄される
            coordinator.Dispose();

            // Assert: 破棄済みセマフォへの Release() で発生する ObjectDisposedException が
            // 捕捉され、タスクが例外なく完了する（アプリがクラッシュしない）
            await Task.WhenAll(pendingTasks).ConfigureAwait(true);
            Assert.All(pendingTasks, task => Assert.True(task.IsCompletedSuccessfully));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    private static ThumbnailGenerationCoordinator CreateCoordinator(
        FakeUiDispatcher dispatcher,
        Func<string, DateTime, (string? ThumbnailPath, int? Width, int? Height)> generateThumbnail,
        Func<string, bool>? isJpegFile = null,
        Func<string, CancellationToken, Task<PhotoMetadata?>>? getMetadataAsync = null)
    {
        return new ThumbnailGenerationCoordinator(
            dispatcher,
            isJpegFile ?? (_ => false),
            generateThumbnail,
            getMetadataAsync ?? ((_, _) => Task.FromResult<PhotoMetadata?>(null)),
            createThumbnailImage: _ => null);
    }

    private static PhotoListItem CreateListItem(string filePath, string? thumbnailKey)
    {
        var photoItem = new PhotoItem(
            filePath: filePath,
            sizeBytes: 1000,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: false);

        return new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: thumbnailKey);
    }

    private static PhotoListItem CreateFolderListItem(string folderPath)
    {
        var photoItem = new PhotoItem(
            filePath: folderPath,
            sizeBytes: 0,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: true);

        return new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: "folder-key");
    }

    private static string CreateTempFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xD8, 0xFF });
        return path;
    }

    private static string CreateTempTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-thumbnailcoordinator-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void CleanupTempDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // ベストエフォート
            }
            catch (DirectoryNotFoundException)
            {
                // ベストエフォート
            }
            catch (PathTooLongException)
            {
                // ベストエフォート
            }
            catch (IOException)
            {
                // ベストエフォート
            }
        }
    }

    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public FakeUiDispatcherTimer? LastTimer { get; private set; }

        public bool IsAvailable => true;

        public Task RunAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => asyncFunc();

        public bool TryEnqueue(Action action)
        {
            action();
            return true;
        }

        public IUiDispatcherTimer? CreateTimer()
        {
            LastTimer = new FakeUiDispatcherTimer();
            return LastTimer;
        }
    }

    private sealed class FakeUiDispatcherTimer : IUiDispatcherTimer
    {
        public TimeSpan Interval { get; set; }

        public bool IsRunning { get; private set; }

        public event EventHandler? Tick;

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        public void FireTick() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
