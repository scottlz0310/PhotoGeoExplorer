using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Preview;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 表示の更新は UI のキューへ後回しになるため、その間に届いた次の選択を打ち消してはならない（#388）。
/// BitmapImage は単体テストで作れないため、どの写真を表示しているかはメタデータの表示で確かめる
/// </summary>
public sealed class PreviewPaneSelectionOrderTests
{
    private const string PhotoA = "a.jpg";
    private const string PhotoB = "b.jpg";

    // 地図の矩形選択は、一覧の選択を空にしてから入れ直すため、「空」と「選択」が続けて届く
    [Theory]
    [InlineData(PhotoA, 1)]
    [InlineData(PhotoB, 1)]
    public void SelectionAfterAClearThatHasNotRunYetIsShown(string reselected, int expectedLoads)
    {
        var context = new Context();
        context.Select(PhotoA);
        context.Queue.RunAll();

        context.Select();
        context.Select(reselected);
        context.Queue.RunAll();

        Assert.Equal(reselected, context.ViewModel.MetadataSummary);
        Assert.Equal(expectedLoads, context.Service.LoadCount(reselected));
    }

    [Fact]
    public void ClearingTheSelectionEmptiesThePreviewAndReselectingLoadsAgain()
    {
        var context = new Context();
        context.Select(PhotoA);
        context.Queue.RunAll();

        context.Select();
        context.Queue.RunAll();
        Assert.Null(context.ViewModel.MetadataSummary);

        context.Select(PhotoA);
        context.Queue.RunAll();

        Assert.Equal(PhotoA, context.ViewModel.MetadataSummary);
        Assert.Equal(2, context.Service.LoadCount(PhotoA));
    }

    [Theory]
    [InlineData(PhotoB, PhotoB)]
    [InlineData(null, null)]
    public async Task LoadThatFinishesAfterANewerSelectionIsDiscarded(string? newer, string? expected)
    {
        var context = new Context();
        var slowLoad = context.Service.Hold(PhotoA);
        context.Select(PhotoA);

        context.Select(newer is null ? [] : [newer]);
        context.Queue.RunAll();

        await Complete(slowLoad);
        context.Queue.RunAll();

        Assert.Equal(expected, context.ViewModel.MetadataSummary);
    }

    [Fact]
    public void LoadAlreadyPostedWhenANewerSelectionArrivesIsDiscarded()
    {
        var context = new Context();

        context.Select(PhotoA);
        context.Select(PhotoB);

        // 先に積まれた A の反映は、B が選ばれた後なので何もしない
        context.Queue.RunNext();
        Assert.Null(context.ViewModel.MetadataSummary);

        context.Queue.RunAll();
        Assert.Equal(PhotoB, context.ViewModel.MetadataSummary);
    }

    // 同じ写真へ戻った場合も、最初の読み込みは古い。反映すると画像が差し替わり、拡大の状態が失われる
    [Fact]
    public async Task FirstLoadThatFinishesAfterReturningToTheSamePhotoIsDiscarded()
    {
        var context = new Context();
        var firstLoad = context.Service.Hold(PhotoA);
        context.Select(PhotoA);
        context.Select(PhotoB);
        context.Queue.RunAll();
        context.Select(PhotoA);
        context.Queue.RunAll();

        // 古い読み込みが反映されると、表示が変わるようにする
        context.Service.CameraModel = "stale";
        await Complete(firstLoad);
        context.Queue.RunAll();

        Assert.Equal(PhotoA, context.ViewModel.MetadataSummary);
    }

    // 終了処理の後の UI のキューは投入を拒否するので、後片付けの後に終わった読み込みは投入しない
    [Fact]
    public async Task LoadThatFinishesAfterCleanupIsNotPostedToTheUi()
    {
        var context = new Context();
        var slowLoad = context.Service.Hold(PhotoA);
        context.Select(PhotoA);
        context.ViewModel.Cleanup();

        await Complete(slowLoad);

        Assert.Equal(0, context.Queue.Count);
    }

    // 後片付けと投入が重なった場合だけ、拒否を無視する。それ以外の拒否は例外のままにする
    [Theory]
    [InlineData(PhotoA, true, 0)]
    [InlineData(null, true, 0)]
    [InlineData(PhotoA, false, 1)]
    [InlineData(null, false, 1)]
    public void RejectedUiDispatchFailsOnlyWhileThePaneIsAlive(string? selected, bool cleanedUpWhilePosting, int expectedFailures)
    {
        var dispatcher = new RejectingDispatcher();
        var workspaceState = new WorkspaceState();
        var viewModel = new PreviewPaneViewModel(new FakeService(), workspaceState, dispatcher);
        if (cleanedUpWhilePosting)
        {
            dispatcher.BeforeReject = viewModel.Cleanup;
        }

        // 選択の通知を受ける処理は async void なので、漏れた例外は呼び出し時の同期コンテキストへ投入される
        var failures = new RecordingContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(failures);
        try
        {
            workspaceState.SelectedPhotos = selected is null ? [] : [Context.CreateItem(selected)];
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(expectedFailures, failures.Posted);
    }

    // 同期コンテキストのないスレッドで完了させると、読み込みの続き（UI への投入まで）は SetResult の中で実行される
    private static Task Complete(TaskCompletionSource<BitmapImage?> load) => Task.Run(() => load.SetResult(null));

    private sealed class RecordingContext : SynchronizationContext
    {
        public int Posted { get; private set; }

        public override void Post(SendOrPostCallback d, object? state) => Posted++;
    }

    private sealed class RejectingDispatcher : IUiDispatcher
    {
        public Action? BeforeReject { get; set; }

        public bool IsAvailable => true;

        public Task RunAsync(Action action)
        {
            BeforeReject?.Invoke();
            return Task.FromException(new InvalidOperationException("rejected"));
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => throw new NotSupportedException();

        public bool TryEnqueue(Action action) => throw new NotSupportedException();

        public IUiDispatcherTimer? CreateTimer() => throw new NotSupportedException();
    }

    private sealed class Context
    {
        private readonly WorkspaceState _workspaceState = new();

        public Context() => ViewModel = new PreviewPaneViewModel(Service, _workspaceState, Queue);

        public FakeService Service { get; } = new();

        public ManualUiQueue Queue { get; } = new();

        public PreviewPaneViewModel ViewModel { get; }

        public void Select(params string[] fileNames)
            => _workspaceState.SelectedPhotos = fileNames.Select(CreateItem).ToList();

        public static PhotoListItem CreateItem(string fileName)
        {
            var item = new PhotoItem(
                filePath: fileName,
                sizeBytes: 1,
                modifiedAt: DateTimeOffset.UnixEpoch,
                isFolder: false,
                thumbnailPath: null,
                pixelWidth: null,
                pixelHeight: null);
            return new PhotoListItem(item, thumbnail: null, toolTipText: null, thumbnailKey: null);
        }
    }

    // 実行の順序をテストが決められるように、投入された処理を積むだけの UI ディスパッチャ
    private sealed class ManualUiQueue : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _actions = new();

        public bool IsAvailable => true;

        public int Count => _actions.Count;

        public Task RunAsync(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _actions.Enqueue(() =>
            {
                action();
                completion.SetResult();
            });
            return completion.Task;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => throw new NotSupportedException();

        public bool TryEnqueue(Action action) => throw new NotSupportedException();

        public IUiDispatcherTimer? CreateTimer() => throw new NotSupportedException();

        public void RunAll()
        {
            while (_actions.TryDequeue(out var action))
            {
                action();
            }
        }

        public void RunNext()
        {
            Assert.True(_actions.TryDequeue(out var action));
            action();
        }
    }

    // カメラ名にファイル名を返すので、メタデータの表示が写真を表す
    private sealed class FakeService : IPreviewPaneService
    {
        private readonly Dictionary<string, TaskCompletionSource<BitmapImage?>> _held = [];
        private readonly List<string> _loads = [];

        public string? CameraModel { get; set; }

        // 次の 1 回の読み込みだけを、呼び出し側が完了させるまで待たせる
        public TaskCompletionSource<BitmapImage?> Hold(string filePath)
            => _held[filePath] = new TaskCompletionSource<BitmapImage?>();

        public int LoadCount(string filePath) => _loads.Count(path => path == filePath);

        public Task<BitmapImage?> LoadImageAsync(string filePath)
        {
            _loads.Add(filePath);
            return _held.Remove(filePath, out var held) ? held.Task : Task.FromResult<BitmapImage?>(null);
        }

        public Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult<PhotoMetadata?>(new PhotoMetadata(null, filePath, CameraModel, null, null));
    }
}
