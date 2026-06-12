using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// ファイル操作（作成・リネーム・移動・コピー・貼り付け・削除）の実行と
/// Move/Copy の進捗管理（CTS・進捗タイマー・カウンタ）、クリップボード状態を担うコーディネーター。
/// 進捗状態の変化はコンストラクタ注入のコールバックで ViewModel へ通知する。
/// 操作完了後の Refresh・選択復元は ViewModel 側の責務であり、本クラスは結果サマリーを返すだけにする。
/// </summary>
internal sealed class FileOperationCoordinator : IDisposable
{
    private const int ProgressTimerIntervalMs = 300;

    internal static readonly FileOperationSummary EmptySummary = new(0, 0, Array.Empty<FileOperationFailure>());

    private readonly IFileOperationService _fileOperationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Action<string> _onStatusBarTextChanged;
    private readonly Action _onClipboardChanged;
    private readonly TransferProgressState _moveProgress;
    private readonly TransferProgressState _copyProgress;

    private IReadOnlyList<PhotoListItem> _clipboardItems = Array.Empty<PhotoListItem>();
    private ClipboardOperation _clipboardOperation = ClipboardOperation.None;

    /// <param name="fileOperationService">ファイル操作の実処理。</param>
    /// <param name="uiDispatcher">進捗タイマーの構成と UI スレッドへの marshal に使うディスパッチャ。</param>
    /// <param name="onMoveInProgressChanged">Move 進捗状態の変化通知（UI スレッドで呼ばれる）。</param>
    /// <param name="onCopyInProgressChanged">Copy 進捗状態の変化通知（UI スレッドで呼ばれる）。</param>
    /// <param name="onStatusBarTextChanged">進捗メッセージの通知（UI スレッドタイマーの Tick で呼ばれる）。</param>
    /// <param name="onClipboardChanged">クリップボード状態の変化通知。</param>
    public FileOperationCoordinator(
        IFileOperationService fileOperationService,
        IUiDispatcher uiDispatcher,
        Action<bool> onMoveInProgressChanged,
        Action<bool> onCopyInProgressChanged,
        Action<string> onStatusBarTextChanged,
        Action onClipboardChanged)
    {
        ArgumentNullException.ThrowIfNull(fileOperationService);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(onMoveInProgressChanged);
        ArgumentNullException.ThrowIfNull(onCopyInProgressChanged);
        ArgumentNullException.ThrowIfNull(onStatusBarTextChanged);
        ArgumentNullException.ThrowIfNull(onClipboardChanged);

        _fileOperationService = fileOperationService;
        _uiDispatcher = uiDispatcher;
        _onStatusBarTextChanged = onStatusBarTextChanged;
        _onClipboardChanged = onClipboardChanged;
        _moveProgress = new TransferProgressState("Message.MoveProgress", onMoveInProgressChanged);
        _copyProgress = new TransferProgressState("Message.CopyProgress", onCopyInProgressChanged);
    }

    public bool IsMoveInProgress => _moveProgress.IsInProgress;
    public bool IsCopyInProgress => _copyProgress.IsInProgress;
    public bool HasClipboardItems => _clipboardItems.Count > 0;
    public bool IsCutClipboard => _clipboardOperation == ClipboardOperation.Cut;

    public Task CancelMoveAsync() => _moveProgress.Cts?.CancelAsync() ?? Task.CompletedTask;
    public Task CancelCopyAsync() => _copyProgress.Cts?.CancelAsync() ?? Task.CompletedTask;

    public FileOperationResult CreateFolder(string? parentFolderPath, string folderName)
    {
        if (_fileOperationService.ContainsInvalidFileNameChars(folderName))
        {
            return FileOperationResult.Failure(FileOperationError.InvalidName);
        }

        if (string.IsNullOrWhiteSpace(parentFolderPath))
        {
            return FileOperationResult.Failure(FileOperationError.NoParent);
        }

        return _fileOperationService.CreateFolder(parentFolderPath, folderName);
    }

    /// <summary>
    /// 名前を正規化・検証してリネームを実行する。
    /// 正規化後の名前が現在と同じ場合は何もせず成功（ResultPath は null）を返す。
    /// </summary>
    public FileOperationResult Rename(PhotoListItem item, string newName)
    {
        ArgumentNullException.ThrowIfNull(item);

        var normalizedName = _fileOperationService.NormalizeName(item, newName);

        if (string.Equals(normalizedName, item.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return FileOperationResult.Success();
        }

        if (_fileOperationService.ContainsInvalidFileNameChars(normalizedName))
        {
            return FileOperationResult.Failure(FileOperationError.InvalidName);
        }

        return _fileOperationService.RenameItem(item, normalizedName);
    }

    public Task<FileOperationSummary> MoveItemsToFolderAsync(
        IReadOnlyList<PhotoListItem> items,
        string destinationFolder,
        Func<string, bool, Task<ConflictResolution>>? resolveConflictAsync = null)
    {
        return ExecuteTransferAsync(
            _moveProgress,
            items,
            destinationFolder,
            _fileOperationService.MoveItems,
            _fileOperationService.MoveItemsAsync,
            resolveConflictAsync);
    }

    public Task<FileOperationSummary> CopyItemsToFolderAsync(
        IReadOnlyList<PhotoListItem> items,
        string destinationFolder,
        Func<string, bool, Task<ConflictResolution>>? resolveConflictAsync = null)
    {
        return ExecuteTransferAsync(
            _copyProgress,
            items,
            destinationFolder,
            _fileOperationService.CopyItems,
            _fileOperationService.CopyItemsAsync,
            resolveConflictAsync);
    }

    public Task<FileOperationSummary> DeleteItemsAsync(IReadOnlyList<PhotoListItem> items)
        => _fileOperationService.DeleteItemsAsync(items);

    public void SetClipboard(IReadOnlyList<PhotoListItem> items, ClipboardOperation operation)
    {
        ArgumentNullException.ThrowIfNull(items);

        _clipboardItems = items.ToList();
        _clipboardOperation = operation;
        _onClipboardChanged();
    }

    /// <summary>
    /// クリップボードの内容を現在のフォルダへ貼り付ける。
    /// Cut 操作が全件成功（失敗・スキップなし）した場合のみクリップボードをクリアする。
    /// </summary>
    /// <returns>結果サマリーと、実行されたクリップボード操作種別（クリップボードが空の場合は None）。</returns>
    public async Task<(FileOperationSummary Summary, ClipboardOperation Operation)> PasteAsync(
        string destinationFolder,
        Func<string, bool, Task<ConflictResolution>>? resolveMoveConflictAsync = null,
        Func<string, bool, Task<ConflictResolution>>? resolveCopyConflictAsync = null)
    {
        if (_clipboardItems.Count == 0)
        {
            return (EmptySummary, ClipboardOperation.None);
        }

        var items = _clipboardItems;
        var operation = _clipboardOperation;

        if (operation == ClipboardOperation.Copy)
        {
            var copyResult = await CopyItemsToFolderAsync(items, destinationFolder, resolveCopyConflictAsync).ConfigureAwait(false);
            return (copyResult, operation);
        }

        var moveResult = await MoveItemsToFolderAsync(items, destinationFolder, resolveMoveConflictAsync).ConfigureAwait(false);
        if (moveResult.FailureCount == 0 && moveResult.SkipCount == 0)
        {
            _clipboardItems = Array.Empty<PhotoListItem>();
            _clipboardOperation = ClipboardOperation.None;
            _onClipboardChanged();
        }

        return (moveResult, operation);
    }

    public void Dispose()
    {
        DisposeTransferState(_moveProgress);
        DisposeTransferState(_copyProgress);
    }

    /// <summary>
    /// Move/Copy 共通の転送実装。競合解決コールバックが null の場合は同期パス（進捗管理なし）。
    /// それ以外はバックグラウンドスレッドで転送し、進捗タイマー・キャンセル・完了通知を管理する。
    /// </summary>
    private async Task<FileOperationSummary> ExecuteTransferAsync(
        TransferProgressState state,
        IReadOnlyList<PhotoListItem> items,
        string destinationFolder,
        Func<IReadOnlyList<PhotoListItem>, string, FileOperationSummary> transferItems,
        Func<IReadOnlyList<PhotoListItem>, string, Func<string, bool, Task<ConflictResolution>>, IProgress<int>?, CancellationToken, Task<FileOperationSummary>> transferItemsAsync,
        Func<string, bool, Task<ConflictResolution>>? resolveConflictAsync)
    {
        if (resolveConflictAsync is null)
        {
            return transferItems(items, destinationFolder);
        }

        var cts = new CancellationTokenSource();
        state.Cts = cts;
        // cts が後で破棄されても ObjectDisposedException が発生しないよう、Task.Run 前にトークン値をキャプチャする
        var token = cts.Token;
        state.Total = items.Count;
        Volatile.Write(ref state.Completed, 0);
        SetInProgress(state, true);
        StartProgressTimer(state);

        // ファイル操作はバックグラウンドスレッドで実行し UI スレッドをブロックしない。
        // 競合ダイアログは UI スレッドへ marshal してから表示する。
        Func<string, bool, Task<ConflictResolution>> marshalledCallback = async (name, isFolder) =>
            await _uiDispatcher.EnqueueAsync(() => resolveConflictAsync(name, isFolder)).ConfigureAwait(false);

        try
        {
            var progress = new Progress<int>(completed =>
            {
                Interlocked.Exchange(ref state.Completed, completed);
            });

            return await Task.Run(() => transferItemsAsync(
                items, destinationFolder, marshalledCallback, progress, token))
                .ConfigureAwait(false);
        }
        finally
        {
            // ConfigureAwait(false) により finally はバックグラウンドスレッドで実行される。
            // 進捗タイマーの Stop()、進捗状態の変更通知、CTS の Dispose/null 化は
            // すべて UI スレッドで行い、キャンセルコマンドとの race を防ぐ（#137）。
            await _uiDispatcher.RunAsync(() =>
            {
                StopProgressTimer(state);
                SetInProgress(state, false);
                state.Cts?.Dispose();
                state.Cts = null;
            }).ConfigureAwait(false);
        }
    }

    private static void SetInProgress(TransferProgressState state, bool value)
    {
        if (state.IsInProgress == value)
        {
            return;
        }

        state.IsInProgress = value;
        state.OnInProgressChanged(value);
    }

    private void StartProgressTimer(TransferProgressState state)
    {
        var timer = _uiDispatcher.CreateTimer();
        if (timer is null)
        {
            return;
        }

        state.Timer = timer;
        timer.Interval = TimeSpan.FromMilliseconds(ProgressTimerIntervalMs);
        timer.Tick += (_, _) => ReportProgress(state);
        timer.Start();
    }

    private static void StopProgressTimer(TransferProgressState state)
    {
        state.Timer?.Stop();
        state.Timer = null;
    }

    private void ReportProgress(TransferProgressState state)
    {
        var completed = Volatile.Read(ref state.Completed);
        _onStatusBarTextChanged(LocalizationService.Format(state.ProgressResourceKey, completed, state.Total));
    }

    private static void DisposeTransferState(TransferProgressState state)
    {
        StopProgressTimer(state);
        state.Cts?.Cancel();
        state.Cts?.Dispose();
        state.Cts = null;
    }

    /// <summary>
    /// 操作種別（Move/Copy）ごとの進捗状態。進捗メッセージのリソースキーと
    /// 進捗状態の変更通知コールバックをパラメータ化することで、両操作の実装を統一する。
    /// </summary>
    private sealed class TransferProgressState
    {
        public TransferProgressState(string progressResourceKey, Action<bool> onInProgressChanged)
        {
            ProgressResourceKey = progressResourceKey;
            OnInProgressChanged = onInProgressChanged;
        }

        public string ProgressResourceKey { get; }
        public Action<bool> OnInProgressChanged { get; }
        public CancellationTokenSource? Cts;
        public int Total;
        public int Completed;
        public bool IsInProgress;
        public IUiDispatcherTimer? Timer;
    }
}
