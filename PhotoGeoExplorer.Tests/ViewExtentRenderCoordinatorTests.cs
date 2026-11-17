using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class ViewExtentRenderCoordinatorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RendersAndShowsASingleRequestOnTheUiThread()
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0));
        var render = await harness.NextRenderAsync().ConfigureAwait(true);
        Assert.True(harness.Coordinator.IsBusy);

        render.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 1).ConfigureAwait(true);
        Assert.Empty(harness.Shown);
        Assert.True(harness.Coordinator.IsBusy);

        harness.RunUi();

        var frame = Assert.Single(harness.Shown);
        Assert.Equal(1, frame.Generation);
        Assert.Same(render.Image, frame.Image.AcquireValue());
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);
        Assert.Empty(harness.Failures);
    }

    [Fact]
    public async Task BusyChangedIsRaisedOnlyOnTransitions()
    {
        using var harness = new Harness();
        var transitions = new ConcurrentQueue<bool>();
        harness.Coordinator.BusyChanged += (_, _) => transitions.Enqueue(harness.Coordinator.IsBusy);

        harness.Coordinator.Submit(Request(0));
        harness.Coordinator.Submit(Request(1));
        (await harness.NextRenderAsync().ConfigureAwait(true)).Complete();
        (await harness.NextRenderAsync().ConfigureAwait(true)).Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 2).ConfigureAwait(true);
        harness.RunUi();
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);

        Assert.Equal([true, false], transitions.ToArray());
    }

    [Fact]
    public async Task UnstartedRequestsAreReplacedByTheNewestOne()
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0));
        var first = await harness.NextRenderAsync().ConfigureAwait(true);
        harness.Coordinator.Submit(Request(10));
        harness.Coordinator.Submit(Request(20));
        harness.Coordinator.Submit(Request(30));

        // 実行中の描画は、重なる範囲への要求ではキャンセルされず、最後まで走る
        Assert.False(first.Token.IsCancellationRequested);
        first.Complete();
        var second = await harness.NextRenderAsync().ConfigureAwait(true);
        second.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 2).ConfigureAwait(true);
        harness.RunUi();

        // 世代 2・3 の要求は、描かれずに最新（世代 4）へ置き換わる
        Assert.Equal([30], harness.Started.Skip(1).Select(r => (int)r.CenterX).ToArray());
        Assert.Equal([1L, 4L], harness.Shown.Select(f => f.Generation).ToArray());
    }

    [Theory]
    // 範囲が重なる間は完走させる（連続操作で確定描画が一度も完了せず、白紙になるのを防ぐ）
    [InlineData(10, "a", false)]
    [InlineData(99, "a", false)]
    // 範囲が重ならなくなったら、結果は使えない
    [InlineData(100, "a", true)]
    [InlineData(5000, "a", true)]
    // 描画元・スタイルが変わったら、結果は使えない
    [InlineData(0, "b", true)]
    internal async Task RunningRenderIsCancelledOnlyWhenItsResultCannotBeUsed(double newCenterX, string newSource, bool expectCancelled)
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0, "a"));
        var running = await harness.NextRenderAsync().ConfigureAwait(true);
        harness.Coordinator.Submit(Request(newCenterX, newSource));

        Assert.Equal(expectCancelled, running.Token.IsCancellationRequested);
        running.Complete();
    }

    [Theory]
    [InlineData(5000, "a")]
    [InlineData(0, "b")]
    internal async Task FinishedImageThatIsNoLongerUsableIsDiscardedAndDisposed(double newCenterX, string newSource)
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0, "a"));
        var running = await harness.NextRenderAsync().ConfigureAwait(true);
        harness.Coordinator.Submit(Request(newCenterX, newSource));

        // キャンセルを無視して画像を返してしまう描画でも、古い画像は表示へ回らない
        running.Complete();
        var next = await harness.NextRenderAsync().ConfigureAwait(true);
        next.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 1).ConfigureAwait(true);
        harness.RunUi();

        Assert.True(running.Image.IsDisposed);
        var frame = Assert.Single(harness.Shown);
        Assert.Equal(2, frame.Generation);
    }

    [Fact]
    public async Task StaleButOverlappingImageIsShownBeforeTheNewestOne()
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0));
        var first = await harness.NextRenderAsync().ConfigureAwait(true);
        harness.Coordinator.Submit(Request(60));

        // 最新の範囲と重なる古い画像は、位置が正しいので直前画像として使える
        first.Complete();
        var second = await harness.NextRenderAsync().ConfigureAwait(true);
        second.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 2).ConfigureAwait(true);
        harness.RunUi();

        Assert.Equal([1L, 2L], harness.Shown.Select(f => f.Generation).ToArray());
    }

    [Fact]
    public async Task CancelledRenderIsNotReportedAsFailureAndTheNextRequestStillRuns()
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0));
        var first = await harness.NextRenderAsync().ConfigureAwait(true);
        harness.Coordinator.Submit(Request(5000));
        first.Cancel();
        var second = await harness.NextRenderAsync().ConfigureAwait(true);
        second.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 1).ConfigureAwait(true);
        harness.RunUi();

        Assert.Empty(harness.Failures);
        Assert.Equal(2, Assert.Single(harness.Shown).Generation);
    }

    [Fact]
    public async Task RenderFailureIsReportedAndDoesNotStopLaterRequests()
    {
        using var harness = new Harness();
        var boom = new InvalidOperationException("boom");

        harness.Coordinator.Submit(Request(0));
        var first = await harness.NextRenderAsync().ConfigureAwait(true);
        harness.Coordinator.Submit(Request(10));
        first.Fail(boom);
        var second = await harness.NextRenderAsync().ConfigureAwait(true);
        second.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 1).ConfigureAwait(true);
        harness.RunUi();

        var (request, exception) = Assert.Single(harness.Failures);
        Assert.Equal(0, request.CenterX);
        Assert.Same(boom, exception);
        Assert.Equal(2, Assert.Single(harness.Shown).Generation);
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeCancelsTheRunningRenderAndDisposesItsImage()
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0));
        var running = await harness.NextRenderAsync().ConfigureAwait(true);
        harness.Coordinator.Dispose();

        Assert.True(running.Token.IsCancellationRequested);
        running.Complete();
        await WaitUntilAsync(() => running.Image.IsDisposed).ConfigureAwait(true);
        Assert.Empty(harness.Shown);
        Assert.Empty(harness.Failures);
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);
    }

    [Fact]
    public async Task SubmitAfterDisposeDoesNothingAndDoesNotThrow()
    {
        using var harness = new Harness();
        harness.Coordinator.Dispose();

        var exception = Record.Exception(() => harness.Coordinator.Submit(Request(0)));

        Assert.Null(exception);
        await Task.Delay(50).ConfigureAwait(true);
        Assert.Empty(harness.Started);
        Assert.False(harness.Coordinator.IsBusy);
    }

    [Fact]
    public async Task FrameHandedToTheUiAfterDisposeIsReleasedNotShown()
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0));
        var render = await harness.NextRenderAsync().ConfigureAwait(true);
        render.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 1).ConfigureAwait(true);
        harness.Coordinator.Dispose();
        harness.RunUi();

        Assert.Empty(harness.Shown);
        Assert.True(render.Image.IsDisposed);
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImageIsReleasedWhenTheUiCannotBeReached()
    {
        using var harness = new Harness { UiAvailable = false };

        harness.Coordinator.Submit(Request(0));
        var render = await harness.NextRenderAsync().ConfigureAwait(true);
        render.Complete();

        await WaitUntilAsync(() => render.Image.IsDisposed).ConfigureAwait(true);
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);
        Assert.Empty(harness.Shown);
    }

    [Fact]
    public async Task ImageIsReleasedAndTheErrorPropagatesWhenShowingFails()
    {
        using var harness = new Harness { ShowFailure = new InvalidOperationException("show failed") };

        harness.Coordinator.Submit(Request(0));
        var render = await harness.NextRenderAsync().ConfigureAwait(true);
        render.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 1).ConfigureAwait(true);

        var thrown = Assert.Throws<InvalidOperationException>(harness.RunUi);

        Assert.Same(harness.ShowFailure, thrown);
        Assert.True(render.Image.IsDisposed);
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);
    }

    [Fact]
    public async Task ManyThreadsSubmittingConcurrentlyNeverThrowAndTheNewestRequestIsShownLast()
    {
        const int Submissions = 400;
        var images = new ConcurrentBag<TestDisposable>();
        var shown = new ConcurrentQueue<ViewExtentFrame<TestDisposable>>();
        var failures = new ConcurrentQueue<Exception>();
        using var coordinator = new ViewExtentRenderCoordinator<TestDisposable>(
            async (_, _) =>
            {
                await Task.Yield();
                var image = new TestDisposable();
                images.Add(image);
                return image;
            },
            action =>
            {
                action();
                return true;
            },
            shown.Enqueue,
            (_, ex) => failures.Enqueue(ex));

        await Parallel.ForAsync(0, Submissions, async (i, _) =>
        {
            coordinator.Submit(Request(i % 7));
            await Task.Yield();
        }).ConfigureAwait(true);

        await WaitUntilAsync(() => !coordinator.IsBusy).ConfigureAwait(true);

        Assert.Empty(failures);
        var generations = shown.Select(f => f.Generation).ToArray();
        Assert.Equal(generations.Order().ToArray(), generations);
        Assert.Equal(Submissions, generations[^1]);
        // 表示へ渡した画像だけが生きていて、それ以外はすべて破棄されている
        var live = images.Count(i => !i.IsDisposed);
        Assert.Equal(shown.Count, live);
    }

    [Theory]
    // UI へ渡したあと、実行される前に要求が変わった: 結果が使えなければ、表示へ回さず解放する
    [InlineData(5000, "a", false)]
    [InlineData(0, "b", false)]
    // 範囲が重なる（互換）なら、位置が正しい直前画像として表示する
    [InlineData(60, "a", true)]
    internal async Task FrameWaitingForTheUiIsRevalidatedAgainstTheNewestRequestBeforeItIsShown(double newCenterX, string newSource, bool expectFirstShown)
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0, "a"));
        var first = await harness.NextRenderAsync().ConfigureAwait(true);
        first.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 1).ConfigureAwait(true);

        // 世代 1 の画像が UI キューで待っている間に、要求が変わる
        harness.Coordinator.Submit(Request(newCenterX, newSource));
        var second = await harness.NextRenderAsync().ConfigureAwait(true);
        second.Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 2).ConfigureAwait(true);
        harness.RunUi();

        Assert.Equal(expectFirstShown, harness.Shown.Any(f => f.Generation == 1));
        Assert.Equal(expectFirstShown, !first.Image.IsDisposed);
        Assert.Equal(2, harness.Shown[^1].Generation);
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);
    }

    [Fact]
    public async Task CancellationThatTheCoordinatorDidNotRequestIsReportedAsFailure()
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0));
        var render = await harness.NextRenderAsync().ConfigureAwait(true);
        Assert.False(render.Token.IsCancellationRequested);

        // 描画側が独自のタイムアウトなどで OperationCanceledException を返した
        render.CancelByItself();
        await WaitUntilAsync(() => !harness.Coordinator.IsBusy).ConfigureAwait(true);

        var (request, exception) = Assert.Single(harness.Failures);
        Assert.Equal(0, request.CenterX);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Empty(harness.Shown);
    }

    [Fact]
    public async Task CancellationTheCoordinatorRequestedIsNotReportedAsFailure()
    {
        using var harness = new Harness();

        harness.Coordinator.Submit(Request(0));
        var render = await harness.NextRenderAsync().ConfigureAwait(true);
        harness.Coordinator.Submit(Request(5000));
        Assert.True(render.Token.IsCancellationRequested);

        render.CancelByItself();
        (await harness.NextRenderAsync().ConfigureAwait(true)).Complete();
        await WaitUntilAsync(() => harness.PendingUiCount == 1).ConfigureAwait(true);
        harness.RunUi();

        Assert.Empty(harness.Failures);
    }

    [Fact]
    public void ShouldCancelRunningFollowsTheDocumentedConditions()
    {
        var running = Request(0, "a");

        Assert.False(ViewExtentRenderCoordinator<TestDisposable>.ShouldCancelRunning(running, Request(50, "a")));
        Assert.True(ViewExtentRenderCoordinator<TestDisposable>.ShouldCancelRunning(running, Request(100, "a")));
        Assert.True(ViewExtentRenderCoordinator<TestDisposable>.ShouldCancelRunning(running, Request(0, "b")));
    }

    [Fact]
    public void ConstructorRejectsMissingCallbacks()
    {
        static Task<TestDisposable> Render(ViewExtentRequest r, CancellationToken t) => Task.FromResult(new TestDisposable());

        Assert.Throws<ArgumentNullException>(() => new ViewExtentRenderCoordinator<TestDisposable>(null!, _ => true, _ => { }, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => new ViewExtentRenderCoordinator<TestDisposable>(Render, null!, _ => { }, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => new ViewExtentRenderCoordinator<TestDisposable>(Render, _ => true, null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => new ViewExtentRenderCoordinator<TestDisposable>(Render, _ => true, _ => { }, null!));
    }

    // 画面 100 論理ピクセル四方・解像度 1 の要求。中心 X が 100 以上離れると範囲が重ならない
    private static ViewExtentRequest Request(double centerX, string source = "a")
    {
        Assert.True(ViewExtentRequest.TryCreate(centerX, 0, 1, 100, 100, 1, source, out var request));
        return request;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(5, cts.Token).ConfigureAwait(true);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<Action> _ui = new();
        private readonly Queue<RenderCall> _renders = new();
        private readonly SemaphoreSlim _renderSignal = new(0);

        public Harness()
        {
            Coordinator = new ViewExtentRenderCoordinator<TestDisposable>(
                RenderAsync,
                PostToUi,
                frame =>
                {
                    if (ShowFailure is not null)
                    {
                        throw ShowFailure;
                    }

                    Shown.Add(frame);
                },
                (request, exception) => Failures.Add((request, exception)));
        }

        public ViewExtentRenderCoordinator<TestDisposable> Coordinator { get; }

        public List<ViewExtentFrame<TestDisposable>> Shown { get; } = [];

        public List<ViewExtentRequest> Started { get; } = [];

        public List<(ViewExtentRequest Request, Exception Exception)> Failures { get; } = [];

        public bool UiAvailable { get; set; } = true;

        public Exception? ShowFailure { get; set; }

        public int PendingUiCount
        {
            get
            {
                lock (_gate)
                {
                    return _ui.Count;
                }
            }
        }

        public async Task<RenderCall> NextRenderAsync()
        {
            Assert.True(await _renderSignal.WaitAsync(Timeout).ConfigureAwait(true), "描画が開始されなかった");
            lock (_gate)
            {
                return _renders.Dequeue();
            }
        }

        public void RunUi()
        {
            while (true)
            {
                Action action;
                lock (_gate)
                {
                    if (_ui.Count == 0)
                    {
                        return;
                    }

                    action = _ui.Dequeue();
                }

                action();
            }
        }

        public void Dispose()
        {
            Coordinator.Dispose();
            _renderSignal.Dispose();
        }

        private Task<TestDisposable> RenderAsync(ViewExtentRequest request, CancellationToken token)
        {
            var call = new RenderCall(request, token);
            lock (_gate)
            {
                Started.Add(request);
                _renders.Enqueue(call);
            }

            _renderSignal.Release();
            return call.Task;
        }

        private bool PostToUi(Action action)
        {
            if (!UiAvailable)
            {
                return false;
            }

            lock (_gate)
            {
                _ui.Enqueue(action);
            }

            return true;
        }
    }

    private sealed class RenderCall
    {
        private readonly TaskCompletionSource<TestDisposable> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RenderCall(ViewExtentRequest request, CancellationToken token)
        {
            Request = request;
            Token = token;
        }

        public ViewExtentRequest Request { get; }

        public CancellationToken Token { get; }

        public TestDisposable Image { get; } = new();

        public Task<TestDisposable> Task => _completion.Task;

        public void Complete() => _completion.SetResult(Image);

        public void Cancel() => _completion.SetCanceled(Token.IsCancellationRequested ? Token : new CancellationToken(true));

        /// <summary>コーディネーターのキャンセルとは無関係に、描画側が OperationCanceledException（TaskCanceledException）で終わる。</summary>
        public void CancelByItself() => _completion.SetCanceled(new CancellationToken(true));

        public void Fail(Exception exception) => _completion.SetException(exception);
    }
}

internal static class SharedResourceTestExtensions
{
    /// <summary>テストで保持している値を、リースを取って読み出す（取得できなければ null）。</summary>
    public static T? AcquireValue<T>(this SharedResource<T> resource)
        where T : class, IDisposable
    {
        if (!resource.TryAcquire(out var lease))
        {
            return null;
        }

        using (lease)
        {
            return lease.Value;
        }
    }
}
