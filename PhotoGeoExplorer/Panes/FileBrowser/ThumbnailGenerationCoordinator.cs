using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// サムネイル生成サブシステムのコーディネーター。
/// SemaphoreSlim による並列数制限、UI スレッドタイマーによるバッチ UI 更新、
/// CancellationTokenSource のライフサイクル管理を担う。
/// </summary>
internal sealed class ThumbnailGenerationCoordinator : IDisposable
{
    private const int ThumbnailGenerationConcurrency = 3;
    private const int ThumbnailUpdateBatchIntervalMs = 300;

    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<string, bool> _isJpegFile;
    private readonly Func<string, DateTime, (string? ThumbnailPath, int? Width, int? Height)> _generateThumbnail;
    private readonly Func<string, CancellationToken, Task<PhotoMetadata?>> _getMetadataAsync;
    private readonly Func<string?, BitmapImage?> _createThumbnailImage;

    private readonly SemaphoreSlim _thumbnailGenerationSemaphore = new(ThumbnailGenerationConcurrency, ThumbnailGenerationConcurrency);
    private readonly HashSet<string> _thumbnailsInProgress = new();
    private readonly object _thumbnailsInProgressLock = new();
    private readonly List<(PhotoListItem Item, string? ThumbnailPath, string? Key, int Generation, int? Width, int? Height, DateTimeOffset? TakenAt, bool? HasLocation, bool IsLocationFixFailed)> _pendingThumbnailUpdates = new();
    private readonly object _pendingThumbnailUpdatesLock = new();
    private readonly HashSet<Task> _activeThumbnailTasks = new();
    private readonly object _activeThumbnailTasksLock = new();
    private int _thumbnailGenerationTotal;
    private int _thumbnailGenerationCompleted;
    private CancellationTokenSource? _thumbnailGenerationCts;
    private IUiDispatcherTimer? _thumbnailUpdateTimer;

    // テストからタイムアウト値を差し替え可能にするための internal フィールド
    internal TimeSpan DisposeTimeout = TimeSpan.FromSeconds(30);

    /// <param name="uiDispatcher">バッチ更新タイマーの構成に使う UI スレッドディスパッチャ。</param>
    /// <param name="isJpegFile">EXIF メタデータ取得対象（JPEG）かどうかの判定。</param>
    /// <param name="generateThumbnail">サムネイル生成処理。null の場合は <see cref="ThumbnailService.GenerateThumbnail"/>（テスト用シーム）。</param>
    /// <param name="getMetadataAsync">EXIF メタデータ取得処理。null の場合は <see cref="ExifReader.GetMetadataAsync"/>（テスト用シーム）。</param>
    /// <param name="createThumbnailImage">サムネイル画像の生成処理。null の場合は BitmapImage を生成する既定実装（テスト用シーム）。</param>
    public ThumbnailGenerationCoordinator(
        IUiDispatcher uiDispatcher,
        Func<string, bool> isJpegFile,
        Func<string, DateTime, (string? ThumbnailPath, int? Width, int? Height)>? generateThumbnail = null,
        Func<string, CancellationToken, Task<PhotoMetadata?>>? getMetadataAsync = null,
        Func<string?, BitmapImage?>? createThumbnailImage = null)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(isJpegFile);

        _uiDispatcher = uiDispatcher;
        _isJpegFile = isJpegFile;
        _generateThumbnail = generateThumbnail ?? ThumbnailService.GenerateThumbnail;
        _getMetadataAsync = getMetadataAsync ?? ExifReader.GetMetadataAsync;
        _createThumbnailImage = createThumbnailImage ?? CreateThumbnailImage;
    }

    /// <summary>
    /// サムネイル未生成のアイテムに対してバックグラウンド生成を開始する。
    /// 進行中の生成バッチがあればキャンセルしてから開始する。
    /// UI スレッドタイマーを構成するため、UI スレッド上から呼び出すこと。
    /// </summary>
    public void StartGeneration(IReadOnlyList<PhotoListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // 既存の生成処理をキャンセル
        Cancel();

        // テスト環境またはUIスレッドがない場合はスキップ
        if (!_uiDispatcher.IsAvailable)
        {
            return;
        }

        // サムネイルが未生成のアイテムを収集
        var itemsNeedingThumbnails = items
            .Where(item => !item.IsFolder && item.Thumbnail is null && item.ThumbnailKey is not null)
            .ToList();

        if (itemsNeedingThumbnails.Count == 0)
        {
            return;
        }

        // カウンターを初期化
        _thumbnailGenerationTotal = itemsNeedingThumbnails.Count;
        _thumbnailGenerationCompleted = 0;

        // 更新タイマーの初期化（IsAvailable 確認済みのため CreateTimer は非 null を返す）
        var thumbnailUpdateTimer = _uiDispatcher.CreateTimer();
        if (thumbnailUpdateTimer is null)
        {
            return;
        }

        _thumbnailUpdateTimer = thumbnailUpdateTimer;
        _thumbnailUpdateTimer.Interval = TimeSpan.FromMilliseconds(ThumbnailUpdateBatchIntervalMs);
        _thumbnailUpdateTimer.Tick += OnThumbnailUpdateTimerTick;
        _thumbnailUpdateTimer.Start();

        // 新しいキャンセルトークンを作成
        var cts = new CancellationTokenSource();
        _thumbnailGenerationCts = cts;
        // cts が後で破棄されても Token プロパティへのアクセスで ObjectDisposedException が
        // 発生しないよう、Task.Run 前にトークン値をキャプチャする（Select は lazy なので
        // Task.WhenAll 反復時点で cts が破棄済みになりうる）
        var token = cts.Token;

        AppLog.Info($"ThumbnailGenerationCoordinator.StartGeneration: Starting generation for {itemsNeedingThumbnails.Count} items");

        // バックグラウンドで並列生成開始
        // フォルダ切り替えで旧バッチがキャンセルされても生成処理（同期・非キャンセル）
        // が実行中の場合は完了まで継続する。Dispose() でセマフォを破棄する前に全タスクの完了を保証するため
        // _activeThumbnailTasks に登録し、完了時に自己削除する。
        var task = Task.Run(async () =>
        {
            try
            {
                var tasks = itemsNeedingThumbnails.Select(listItem => GenerateThumbnailAsync(listItem, token));
                await Task.WhenAll(tasks).ConfigureAwait(false);
                AppLog.Info("ThumbnailGenerationCoordinator.StartGeneration: Completed");
            }
            catch (OperationCanceledException)
            {
                // キャンセル済み - 正常終了
            }
        }, token);

        lock (_activeThumbnailTasksLock)
        {
            _activeThumbnailTasks.Add(task);
        }

        _ = task.ContinueWith(_ =>
        {
            lock (_activeThumbnailTasksLock)
            {
                _activeThumbnailTasks.Remove(task);
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// 進行中の生成バッチをキャンセルし、タイマー・保留中更新・生成中リストをクリアする。
    /// </summary>
    public void Cancel()
    {
        // タイマーを停止
        if (_thumbnailUpdateTimer is not null)
        {
            _thumbnailUpdateTimer.Stop();
            _thumbnailUpdateTimer.Tick -= OnThumbnailUpdateTimerTick;
            _thumbnailUpdateTimer = null;
        }

        // 保留中の更新をクリア
        lock (_pendingThumbnailUpdatesLock)
        {
            _pendingThumbnailUpdates.Clear();
        }

        // 生成中リストをクリア
        lock (_thumbnailsInProgressLock)
        {
            _thumbnailsInProgress.Clear();
        }

        // キャンセルトークンをキャンセル
        var previousCts = _thumbnailGenerationCts;
        _thumbnailGenerationCts = null;
        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
                previousCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄済み
            }
        }
    }

    public void Dispose()
    {
        Cancel();
        // フォルダ切り替えで生成バッチが複数回作られた場合も含め、全アクティブタスクの
        // 完了を待ってからセマフォを破棄する。タイムアウト時は後続の Release() が
        // ObjectDisposedException になりうるが、GenerateThumbnailAsync 内で捕捉される。
        Task[] pendingTasks;
        lock (_activeThumbnailTasksLock)
        {
            pendingTasks = [.. _activeThumbnailTasks];
        }

        try { Task.WhenAll(pendingTasks).Wait(DisposeTimeout); }
        catch (AggregateException) { }
        _thumbnailGenerationSemaphore.Dispose();
    }

    /// <summary>
    /// テスト用: アクティブな生成タスクのスナップショットを返す。
    /// Dispose のタイムアウト超過後にタスクが安全に完了することを検証するために使う。
    /// </summary>
    internal Task[] GetActiveTasksSnapshot()
    {
        lock (_activeThumbnailTasksLock)
        {
            return [.. _activeThumbnailTasks];
        }
    }

    private async Task GenerateThumbnailAsync(PhotoListItem listItem, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var key = listItem.ThumbnailKey;
        if (key is null)
        {
            return;
        }

        // 重複生成を防止
        lock (_thumbnailsInProgressLock)
        {
            if (_thumbnailsInProgress.Contains(key))
            {
                return;
            }

            _thumbnailsInProgress.Add(key);
        }

        try
        {
            // セマフォで並列数を制限
            await _thumbnailGenerationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // サムネイル生成（バックグラウンドスレッド）
                var fileInfo = new FileInfo(listItem.FilePath);
                if (!fileInfo.Exists)
                {
                    return;
                }

                var result = _generateThumbnail(listItem.FilePath, fileInfo.LastWriteTimeUtc);
                if (result.ThumbnailPath is null || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                DateTimeOffset? takenAt = null;
                bool? hasLocation = null;
                var isLocationFixFailed = false;
                if (IsJpegFile(listItem))
                {
                    var metadata = await _getMetadataAsync(listItem.FilePath, cancellationToken).ConfigureAwait(false);
                    if (metadata is not null)
                    {
                        takenAt = metadata.TakenAt;
                        hasLocation = metadata.HasValidLocation;
                        isLocationFixFailed = metadata.IsLikelyLocationFixFailed;
                    }
                }

                // UIスレッドで BitmapImage を作成して更新をキューに追加
                lock (_pendingThumbnailUpdatesLock)
                {
                    _pendingThumbnailUpdates.Add((listItem, result.ThumbnailPath, key, listItem.Generation, result.Width, result.Height, takenAt, hasLocation, isLocationFixFailed));
                }
            }
            finally
            {
                // タイムアウト時に Dispose() がセマフォを先に破棄した場合の安全網
                try { _thumbnailGenerationSemaphore.Release(); }
                catch (ObjectDisposedException) { }
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常
        }
        catch (ObjectDisposedException)
        {
            // WaitAsync 待機中に Dispose() でセマフォが破棄された場合
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"GenerateThumbnailAsync: Access denied for {listItem.FileName}", ex);
        }
        catch (IOException ex)
        {
            AppLog.Error($"GenerateThumbnailAsync: IO error for {listItem.FileName}", ex);
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error($"GenerateThumbnailAsync: Unsupported operation for {listItem.FileName}", ex);
        }
        finally
        {
            lock (_thumbnailsInProgressLock)
            {
                _thumbnailsInProgress.Remove(key);
            }

            // 完了カウントをインクリメント
            Interlocked.Increment(ref _thumbnailGenerationCompleted);
        }
    }

    private bool IsJpegFile(PhotoListItem item)
        => !item.IsFolder && _isJpegFile(item.FilePath);

    private void OnThumbnailUpdateTimerTick(object? sender, EventArgs e)
    {
        ApplyPendingThumbnailUpdates();
    }

    private void ApplyPendingThumbnailUpdates()
    {
        // まず、生成完了チェックを実行（キューの有無に関わらず）
        var shouldStopTimer = Volatile.Read(ref _thumbnailGenerationCompleted) >= _thumbnailGenerationTotal;

        List<(PhotoListItem Item, string? ThumbnailPath, string? Key, int Generation, int? Width, int? Height, DateTimeOffset? TakenAt, bool? HasLocation, bool IsLocationFixFailed)> updates;

        lock (_pendingThumbnailUpdatesLock)
        {
            // キューが空の場合、完了チェックのみ実行
            if (_pendingThumbnailUpdates.Count == 0)
            {
                if (shouldStopTimer && _thumbnailUpdateTimer is not null)
                {
                    _thumbnailUpdateTimer.Stop();
                    AppLog.Info("ApplyPendingThumbnailUpdates: All thumbnail generation tasks finished, stopping timer (queue empty)");
                }
                return;
            }

            updates = new List<(PhotoListItem, string?, string?, int, int?, int?, DateTimeOffset?, bool?, bool)>(_pendingThumbnailUpdates);
            _pendingThumbnailUpdates.Clear();
        }

        var successCount = 0;
        var metadataUpdatedCount = 0;
        foreach (var (item, thumbnailPath, key, generation, width, height, takenAt, hasLocation, isLocationFixFailed) in updates)
        {
            // UIスレッドでBitmapImageを作成
            var thumbnail = _createThumbnailImage(thumbnailPath);
            if (item.UpdateThumbnail(thumbnail, key, generation, width, height))
            {
                successCount++;
            }

            if (item.UpdateMetadata(takenAt, hasLocation, isLocationFixFailed))
            {
                metadataUpdatedCount++;
            }
        }

        if (successCount > 0 || metadataUpdatedCount > 0)
        {
            AppLog.Info($"ApplyPendingThumbnailUpdates: Applied {successCount} thumbnail updates and {metadataUpdatedCount} metadata updates");
        }

        // 生成完了チェック後、キューも確認してタイマーを停止
        if (shouldStopTimer)
        {
            lock (_pendingThumbnailUpdatesLock)
            {
                if (_pendingThumbnailUpdates.Count == 0 && _thumbnailUpdateTimer is not null)
                {
                    _thumbnailUpdateTimer.Stop();
                    AppLog.Info("ApplyPendingThumbnailUpdates: All thumbnail generation tasks finished, stopping timer");
                }
            }
        }
    }

    private static BitmapImage? CreateThumbnailImage(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(thumbnailPath));
        }
        catch (ArgumentException ex)
        {
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
        catch (UriFormatException ex)
        {
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
    }
}
