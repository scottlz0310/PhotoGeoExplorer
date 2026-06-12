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
/// FileOperationCoordinator のテスト。
/// Move/Copy 統一後の共通進捗実装（CTS・進捗タイマー・カウンタ・キャンセル連携）と
/// クリップボード状態管理・バリデーションを Coordinator 直接で検証する。
/// </summary>
public sealed class FileOperationCoordinatorTests
{
    public enum TransferKind { Move, Copy }

    private static readonly bool[] ExpectedInProgressLifecycle = { true, false };

    // =========================================================
    // Move/Copy 共通進捗実装
    // =========================================================

    [Theory]
    [InlineData(TransferKind.Move)]
    [InlineData(TransferKind.Copy)]
    public async Task Transfer_WithoutConflictCallback_UsesSyncPathWithoutProgress(TransferKind kind)
    {
        var harness = new CoordinatorHarness();
        var items = new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") };

        var summary = await TransferAsync(harness.Coordinator, kind, items, resolveConflictAsync: null);

        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(kind == TransferKind.Move ? 1 : 0, harness.Service.MoveItemsCallCount);
        Assert.Equal(kind == TransferKind.Copy ? 1 : 0, harness.Service.CopyItemsCallCount);
        Assert.Empty(harness.InProgressChanges(kind));
        Assert.Null(harness.Dispatcher.LastTimer);
    }

    [Theory]
    [InlineData(TransferKind.Move)]
    [InlineData(TransferKind.Copy)]
    public async Task Transfer_WithConflictCallback_NotifiesProgressLifecycle(TransferKind kind)
    {
        var harness = new CoordinatorHarness();
        var items = new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") };

        var summary = await TransferAsync(
            harness.Coordinator, kind, items, (_, _) => Task.FromResult(ConflictResolution.Skip));

        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(ExpectedInProgressLifecycle, harness.InProgressChanges(kind));
        Assert.False(kind == TransferKind.Move
            ? harness.Coordinator.IsMoveInProgress
            : harness.Coordinator.IsCopyInProgress);

        // 進捗タイマーが構成され、転送完了後に停止している
        Assert.NotNull(harness.Dispatcher.LastTimer);
        Assert.False(harness.Dispatcher.LastTimer!.IsRunning);
    }

    [Theory]
    [InlineData(TransferKind.Move, "Message.MoveProgress")]
    [InlineData(TransferKind.Copy, "Message.CopyProgress")]
    public async Task Transfer_ProgressTimerTick_ReportsStatusBarTextWithOperationSpecificKey(TransferKind kind, string expectedResourceKey)
    {
        var harness = new CoordinatorHarness();
        var items = new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") };
        harness.Service.TransferAsyncHook = (_, _) =>
        {
            harness.Dispatcher.LastTimer?.FireTick();
            return Task.FromResult(new FileOperationSummary(1, 0, Array.Empty<FileOperationFailure>()));
        };

        await TransferAsync(harness.Coordinator, kind, items, (_, _) => Task.FromResult(ConflictResolution.Skip));

        // テストホストでは LocalizationService.Format がリソースキーをそのまま返すため、
        // 操作種別に対応するリソースキーで進捗メッセージが通知されたことを検証する
        var statusText = Assert.Single(harness.StatusBarTexts);
        Assert.Equal(expectedResourceKey, statusText);
    }

    [Theory]
    [InlineData(TransferKind.Move)]
    [InlineData(TransferKind.Copy)]
    public async Task Transfer_ConflictCallback_IsMarshalledThroughUiDispatcher(TransferKind kind)
    {
        var harness = new CoordinatorHarness();
        var items = new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") };
        harness.Service.TransferAsyncHook = async (_, resolveConflictAsync) =>
        {
            var resolution = await resolveConflictAsync("photo.jpg", false).ConfigureAwait(false);
            Assert.Equal(ConflictResolution.Overwrite, resolution);
            return new FileOperationSummary(1, 0, Array.Empty<FileOperationFailure>());
        };

        await TransferAsync(
            harness.Coordinator, kind, items, (_, _) => Task.FromResult(ConflictResolution.Overwrite));

        Assert.Equal(1, harness.Dispatcher.EnqueueAsyncCallCount);
    }

    [Theory]
    [InlineData(TransferKind.Move)]
    [InlineData(TransferKind.Copy)]
    public async Task Cancel_DuringTransfer_CancelsTokenAndCompletesLifecycle(TransferKind kind)
    {
        var harness = new CoordinatorHarness();
        var items = new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") };
        using var transferStarted = new SemaphoreSlim(0, 1);
        CancellationToken observedToken = default;
        harness.Service.TransferAsyncTokenHook = async (token, _) =>
        {
            observedToken = token;
            transferStarted.Release();
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return new FileOperationSummary(1, 0, Array.Empty<FileOperationFailure>());
        };

        var transferTask = TransferAsync(
            harness.Coordinator, kind, items, (_, _) => Task.FromResult(ConflictResolution.Skip));
        Assert.True(await transferStarted.WaitAsync(5000));

        if (kind == TransferKind.Move)
        {
            await harness.Coordinator.CancelMoveAsync();
        }
        else
        {
            await harness.Coordinator.CancelCopyAsync();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transferTask);
        Assert.True(observedToken.IsCancellationRequested);

        // キャンセル後も #137 の finally 集約で進捗状態が解除される
        Assert.Equal(ExpectedInProgressLifecycle, harness.InProgressChanges(kind));
        Assert.False(kind == TransferKind.Move
            ? harness.Coordinator.IsMoveInProgress
            : harness.Coordinator.IsCopyInProgress);
    }

    [Fact]
    public async Task Cancel_WithoutRunningTransfer_DoesNotThrow()
    {
        var harness = new CoordinatorHarness();

        await harness.Coordinator.CancelMoveAsync();
        await harness.Coordinator.CancelCopyAsync();
    }

    [Fact]
    public async Task Transfer_MoveAndCopy_TrackIndependentProgressStates()
    {
        var harness = new CoordinatorHarness();
        var items = new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") };
        using var transferStarted = new SemaphoreSlim(0, 1);
        using var releaseTransfer = new SemaphoreSlim(0, 1);
        harness.Service.TransferAsyncHook = async (_, _) =>
        {
            transferStarted.Release();
            Assert.True(await releaseTransfer.WaitAsync(5000).ConfigureAwait(false));
            return new FileOperationSummary(1, 0, Array.Empty<FileOperationFailure>());
        };

        var moveTask = harness.Coordinator.MoveItemsToFolderAsync(
            items, Path.GetTempPath(), (_, _) => Task.FromResult(ConflictResolution.Skip));
        Assert.True(await transferStarted.WaitAsync(5000));

        // Move 実行中でも Copy の進捗状態は独立している
        Assert.True(harness.Coordinator.IsMoveInProgress);
        Assert.False(harness.Coordinator.IsCopyInProgress);

        releaseTransfer.Release();
        await moveTask.ConfigureAwait(true);
        Assert.False(harness.Coordinator.IsMoveInProgress);
    }

    // =========================================================
    // クリップボード状態管理
    // =========================================================

    [Theory]
    [InlineData(false)] // Copy
    [InlineData(true)]  // Cut
    public void SetClipboard_UpdatesStateAndNotifies(bool expectedIsCut)
    {
        var harness = new CoordinatorHarness();
        var items = new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") };

        harness.Coordinator.SetClipboard(items, expectedIsCut ? ClipboardOperation.Cut : ClipboardOperation.Copy);

        Assert.True(harness.Coordinator.HasClipboardItems);
        Assert.Equal(expectedIsCut, harness.Coordinator.IsCutClipboard);
        Assert.Equal(1, harness.ClipboardChangedCount);
    }

    [Fact]
    public async Task PasteAsync_EmptyClipboard_ReturnsEmptySummaryWithNoneOperation()
    {
        var harness = new CoordinatorHarness();

        var (summary, operation) = await harness.Coordinator.PasteAsync(Path.GetTempPath());

        Assert.Equal(0, summary.SuccessCount);
        Assert.False(summary.HasFailures);
        Assert.Equal(ClipboardOperation.None, operation);
    }

    [Fact]
    public async Task PasteAsync_CopyOperation_ExecutesCopyAndKeepsClipboard()
    {
        var harness = new CoordinatorHarness();
        harness.Coordinator.SetClipboard(
            new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") }, ClipboardOperation.Copy);

        var (summary, operation) = await harness.Coordinator.PasteAsync(Path.GetTempPath());

        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(ClipboardOperation.Copy, operation);
        Assert.Equal(1, harness.Service.CopyItemsCallCount);
        Assert.True(harness.Coordinator.HasClipboardItems);
    }

    [Fact]
    public async Task PasteAsync_CutOperation_AllSuccess_ClearsClipboard()
    {
        var harness = new CoordinatorHarness();
        harness.Coordinator.SetClipboard(
            new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") }, ClipboardOperation.Cut);

        var (summary, operation) = await harness.Coordinator.PasteAsync(Path.GetTempPath());

        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(ClipboardOperation.Cut, operation);
        Assert.Equal(1, harness.Service.MoveItemsCallCount);
        Assert.False(harness.Coordinator.HasClipboardItems);
        Assert.False(harness.Coordinator.IsCutClipboard);
        Assert.Equal(2, harness.ClipboardChangedCount); // SetClipboard + クリア
    }

    [Theory]
    [InlineData(0, 0, 1)] // 全件失敗
    [InlineData(1, 0, 1)] // 部分成功（失敗あり）
    [InlineData(1, 1, 0)] // スキップあり
    public async Task PasteAsync_CutOperation_NotAllSuccess_KeepsClipboard(int successCount, int skipCount, int failureCount)
    {
        var failures = new FileOperationFailure[failureCount];
        for (var i = 0; i < failureCount; i++)
        {
            failures[i] = new FileOperationFailure($"path{i}", $"photo{i}.jpg", FileOperationError.AlreadyExists);
        }

        var harness = new CoordinatorHarness();
        harness.Service.MoveItemsResult = new FileOperationSummary(successCount, skipCount, failures);
        harness.Coordinator.SetClipboard(
            new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") }, ClipboardOperation.Cut);

        await harness.Coordinator.PasteAsync(Path.GetTempPath());

        Assert.True(harness.Coordinator.HasClipboardItems);
        Assert.True(harness.Coordinator.IsCutClipboard);
    }

    // =========================================================
    // CreateFolder / Rename バリデーション
    // =========================================================

    [Theory]
    [InlineData("bad:name", null, nameof(FileOperationError.InvalidName))]
    [InlineData("NewFolder", null, nameof(FileOperationError.NoParent))]
    [InlineData("NewFolder", "", nameof(FileOperationError.NoParent))]
    public void CreateFolder_InvalidInput_ReturnsFailureWithoutServiceCall(
        string folderName, string? parentFolderPath, string expectedErrorName)
    {
        var harness = new CoordinatorHarness();

        var result = harness.Coordinator.CreateFolder(parentFolderPath, folderName);

        Assert.False(result.IsSuccess);
        Assert.Equal(Enum.Parse<FileOperationError>(expectedErrorName), result.Error);
        Assert.Equal(0, harness.Service.CreateFolderCallCount);
    }

    [Fact]
    public void CreateFolder_ValidInput_DelegatesToService()
    {
        var harness = new CoordinatorHarness();
        harness.Service.CreateFolderResult = FileOperationResult.Success("created-path");

        var result = harness.Coordinator.CreateFolder(Path.GetTempPath(), "NewFolder");

        Assert.True(result.IsSuccess);
        Assert.Equal("created-path", result.ResultPath);
        Assert.Equal(1, harness.Service.CreateFolderCallCount);
    }

    [Fact]
    public void Rename_SameName_ReturnsSuccessWithoutServiceCall()
    {
        var harness = new CoordinatorHarness();
        var item = CreatePhotoListItem("photo.jpg");

        var result = harness.Coordinator.Rename(item, "photo.jpg");

        Assert.True(result.IsSuccess);
        Assert.Null(result.ResultPath);
        Assert.False(harness.Service.RenameItemWasCalled);
    }

    [Fact]
    public void Rename_InvalidName_ReturnsInvalidName()
    {
        var harness = new CoordinatorHarness();
        var item = CreatePhotoListItem("photo.jpg");

        var result = harness.Coordinator.Rename(item, "bad:name");

        Assert.False(result.IsSuccess);
        Assert.Equal(FileOperationError.InvalidName, result.Error);
        Assert.False(harness.Service.RenameItemWasCalled);
    }

    [Fact]
    public void Rename_ValidNewName_DelegatesToService()
    {
        var harness = new CoordinatorHarness();
        harness.Service.RenameItemResult = FileOperationResult.Success("renamed-path");
        var item = CreatePhotoListItem("photo.jpg");

        var result = harness.Coordinator.Rename(item, "renamed.jpg");

        Assert.True(result.IsSuccess);
        Assert.Equal("renamed-path", result.ResultPath);
        Assert.True(harness.Service.RenameItemWasCalled);
    }

    // =========================================================
    // Dispose
    // =========================================================

    [Fact]
    public async Task Dispose_DuringTransfer_CancelsRunningOperation()
    {
        var harness = new CoordinatorHarness();
        var items = new List<PhotoListItem> { CreatePhotoListItem("photo.jpg") };
        using var transferStarted = new SemaphoreSlim(0, 1);
        harness.Service.TransferAsyncTokenHook = async (token, _) =>
        {
            transferStarted.Release();
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return new FileOperationSummary(1, 0, Array.Empty<FileOperationFailure>());
        };

        var transferTask = harness.Coordinator.MoveItemsToFolderAsync(
            items, Path.GetTempPath(), (_, _) => Task.FromResult(ConflictResolution.Skip));
        Assert.True(await transferStarted.WaitAsync(5000));

        harness.Coordinator.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transferTask);
    }

    // =========================================================
    // テストヘルパー
    // =========================================================

    private static Task<FileOperationSummary> TransferAsync(
        FileOperationCoordinator coordinator,
        TransferKind kind,
        IReadOnlyList<PhotoListItem> items,
        Func<string, bool, Task<ConflictResolution>>? resolveConflictAsync)
    {
        return kind == TransferKind.Move
            ? coordinator.MoveItemsToFolderAsync(items, Path.GetTempPath(), resolveConflictAsync)
            : coordinator.CopyItemsToFolderAsync(items, Path.GetTempPath(), resolveConflictAsync);
    }

    /// <summary>
    /// Coordinator と依存スタブ・コールバック記録をまとめたテストハーネス。
    /// </summary>
    private sealed class CoordinatorHarness
    {
        private readonly List<bool> _moveInProgressChanges = new();
        private readonly List<bool> _copyInProgressChanges = new();

        public CoordinatorHarness()
        {
            Coordinator = new FileOperationCoordinator(
                Service,
                Dispatcher,
                onMoveInProgressChanged: _moveInProgressChanges.Add,
                onCopyInProgressChanged: _copyInProgressChanges.Add,
                onStatusBarTextChanged: StatusBarTexts.Add,
                onClipboardChanged: () => ClipboardChangedCount++);
        }

        public StubFileOperationService Service { get; } = new();
        public RecordingUiDispatcher Dispatcher { get; } = new();
        public FileOperationCoordinator Coordinator { get; }
        public List<string> StatusBarTexts { get; } = new();
        public int ClipboardChangedCount { get; private set; }

        public List<bool> InProgressChanges(TransferKind kind)
            => kind == TransferKind.Move ? _moveInProgressChanges : _copyInProgressChanges;
    }

    private sealed class StubFileOperationService : IFileOperationService
    {
        public FileOperationResult CreateFolderResult { get; set; } = FileOperationResult.Success("result");
        public FileOperationResult RenameItemResult { get; set; } = FileOperationResult.Success("result");
        public FileOperationSummary MoveItemsResult { get; set; } = new(1, 0, Array.Empty<FileOperationFailure>());
        public FileOperationSummary CopyItemsResult { get; set; } = new(1, 0, Array.Empty<FileOperationFailure>());
        public FileOperationSummary DeleteItemsResult { get; set; } = new(1, 0, Array.Empty<FileOperationFailure>());
        public int CreateFolderCallCount { get; private set; }
        public bool RenameItemWasCalled { get; private set; }
        public int MoveItemsCallCount { get; private set; }
        public int CopyItemsCallCount { get; private set; }

        /// <summary>非同期転送（Move/Copy 共通）の差し替えフック。進捗と競合解決コールバックを受け取る。</summary>
        public Func<IProgress<int>?, Func<string, bool, Task<ConflictResolution>>, Task<FileOperationSummary>>? TransferAsyncHook { get; set; }

        /// <summary>非同期転送の差し替えフック（CancellationToken 観測用）。</summary>
        public Func<CancellationToken, IProgress<int>?, Task<FileOperationSummary>>? TransferAsyncTokenHook { get; set; }

        public bool ContainsInvalidFileNameChars(string name) => name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

        public string NormalizeName(PhotoListItem item, string newName)
        {
            var trimmed = newName.Trim();
            if (item.IsFolder)
            {
                return trimmed;
            }

            var originalExt = Path.GetExtension(item.FileName);
            if (string.IsNullOrEmpty(originalExt))
            {
                return trimmed;
            }

            var newExt = Path.GetExtension(trimmed);
            return string.IsNullOrEmpty(newExt) ? $"{trimmed}{originalExt}" : trimmed;
        }

        public bool IsDescendantPath(string root, string candidate) => false;
        public bool IsSamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        public string? GetParentPath(string path) => Path.GetTempPath();
        public bool ItemExistsAtPath(string path) => false;
        public bool FolderExistsAtPath(string path) => true;
        public bool IsJpegFile(string filePath) => false;

        public FileOperationResult CreateFolder(string parentFolder, string folderName)
        {
            CreateFolderCallCount++;
            return CreateFolderResult;
        }

        public FileOperationResult RenameItem(PhotoListItem item, string normalizedName)
        {
            RenameItemWasCalled = true;
            return RenameItemResult;
        }

        public FileOperationSummary MoveItems(IReadOnlyList<PhotoListItem> items, string destinationFolder)
        {
            MoveItemsCallCount++;
            return MoveItemsResult;
        }

        public Task<FileOperationSummary> MoveItemsAsync(
            IReadOnlyList<PhotoListItem> items,
            string destinationFolder,
            Func<string, bool, Task<ConflictResolution>> resolveConflictAsync,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => RunTransferAsync(resolveConflictAsync, progress, MoveItemsResult, cancellationToken);

        public FileOperationSummary CopyItems(IReadOnlyList<PhotoListItem> items, string destinationFolder)
        {
            CopyItemsCallCount++;
            return CopyItemsResult;
        }

        public Task<FileOperationSummary> CopyItemsAsync(
            IReadOnlyList<PhotoListItem> items,
            string destinationFolder,
            Func<string, bool, Task<ConflictResolution>> resolveConflictAsync,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => RunTransferAsync(resolveConflictAsync, progress, CopyItemsResult, cancellationToken);

        public Task<FileOperationSummary> DeleteItemsAsync(IReadOnlyList<PhotoListItem> items) => Task.FromResult(DeleteItemsResult);

        private Task<FileOperationSummary> RunTransferAsync(
            Func<string, bool, Task<ConflictResolution>> resolveConflictAsync,
            IProgress<int>? progress,
            FileOperationSummary defaultResult,
            CancellationToken cancellationToken)
        {
            if (TransferAsyncTokenHook is not null)
            {
                return TransferAsyncTokenHook(cancellationToken, progress);
            }

            if (TransferAsyncHook is not null)
            {
                return TransferAsyncHook(progress, resolveConflictAsync);
            }

            return Task.FromResult(defaultResult);
        }
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public RecordingUiDispatcherTimer? LastTimer { get; private set; }
        public int EnqueueAsyncCallCount { get; private set; }

        public bool IsAvailable => true;

        public Task RunAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc)
        {
            EnqueueAsyncCallCount++;
            return asyncFunc();
        }

        public bool TryEnqueue(Action action)
        {
            action();
            return true;
        }

        public IUiDispatcherTimer? CreateTimer()
        {
            LastTimer = new RecordingUiDispatcherTimer();
            return LastTimer;
        }
    }

    private sealed class RecordingUiDispatcherTimer : IUiDispatcherTimer
    {
        public TimeSpan Interval { get; set; }

        public bool IsRunning { get; private set; }

        public event EventHandler? Tick;

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        public void FireTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private static PhotoListItem CreatePhotoListItem(string fileName)
    {
        var photoItem = new PhotoItem(
            filePath: $"/test/{fileName}",
            sizeBytes: 1000,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: false,
            thumbnailPath: null,
            pixelWidth: 100,
            pixelHeight: 100);

        return new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: null);
    }
}
