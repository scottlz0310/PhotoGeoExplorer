using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class ViewExtentPrefetchSchedulerTests
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // 世界の左端・上端（メルカトルの座標）。タイルの番号の境界を確かめるために使う
    private const double Edge = Math.PI * WebMercator.EarthRadius;

    [Theory]
    // ズーム 0 は世界全体で 1 枚（中心のタイル座標 0.5 → 起点 0）
    [InlineData(0, 0, WebMercator.ZoomZeroResolution, 1.0, 0, 0, 0)]
    // 原点（赤道・本初子午線）はズーム 3 でタイル座標 (4, 4)。2×2 は 3・4 の列と行なので、起点は (3, 3)
    [InlineData(0, 0, WebMercator.ZoomZeroResolution / 8, 1.0, 3, 3, 3)]
    // 世界の左端・上端はタイル座標 (0, 0)。2×2 は -1・0（西の外側は折り返し、北の外側は除かれる）なので、起点は (-1, -1)
    [InlineData(-Edge, Edge, WebMercator.ZoomZeroResolution / 8, 1.0, 3, -1, -1)]
    // 世界の右端・下端の手前はタイル座標 (8 未満, 8 未満)。2×2 は 7・8 なので、起点は (7, 7)
    [InlineData(Edge - 1, -Edge + 1, WebMercator.ZoomZeroResolution / 8, 1.0, 3, 7, 7)]
    // 連続ズームは切り捨て（3.9 でも取得ズームは 3）
    [InlineData(0, 0, WebMercator.ZoomZeroResolution / 14.9, 1.0, 3, 3, 3)]
    // 画素密度が 2 倍なら、同じ解像度でもズームは 1 大きい（タイル座標 (8, 8) → 起点 (7, 7)）
    [InlineData(0, 0, WebMercator.ZoomZeroResolution / 8, 2.0, 4, 7, 7)]
    public void PrefetchKeyIsTheZoomAndTheTopLeftTileOfTheTwoByTwoBlock(
        double centerX,
        double centerY,
        double resolution,
        double density,
        int expectedZoom,
        long expectedX,
        long expectedY)
    {
        var request = Request(centerX, centerY, resolution, density);

        var key = ViewExtentPrefetchScheduler.GetPrefetchKey(request);

        Assert.Equal((expectedZoom, expectedX, expectedY), key);
    }

    [Theory]
    // 同じ中心タイル（512）の中で半タイルを越えると、2×2 の起点が変わる（レビュー指摘の再現。z10 の (512.25, 512.25) → (512.75, 512.25)。
    // 必要な列は 511・512 から 512・513 へ変わる）
    [InlineData(512.25, 512.25, 512.75, 512.25, false)]
    [InlineData(512.25, 512.25, 512.25, 512.75, false)]
    // 半タイルを越えない移動は、同じ 2×2 のまま（起点は変わらない）
    [InlineData(512.55, 512.55, 512.95, 512.95, true)]
    [InlineData(512.5, 512.5, 513.49, 513.49, true)]
    // 半タイルの境界ちょうど（floor(t − 0.5) が切り替わる点）
    [InlineData(512.49, 512.49, 512.5, 512.5, false)]
    public void PrefetchKeyChangesExactlyWhenTheTwoByTwoBlockChanges(
        double fromTileX,
        double fromTileY,
        double toTileX,
        double toTileY,
        bool sameKey)
    {
        const int zoom = 10;

        var from = ViewExtentPrefetchScheduler.GetPrefetchKey(RequestAtTile(zoom, fromTileX, fromTileY));
        var to = ViewExtentPrefetchScheduler.GetPrefetchKey(RequestAtTile(zoom, toTileX, toTileY));

        Assert.Equal(sameKey, from == to);
    }

    [Fact]
    public void ConstructorRejectsNullArgumentsAndANonPositiveDelay()
    {
        static Task Prefetch(ViewExtentRequest request, CancellationToken token) => Task.CompletedTask;
        static void Failed(ViewExtentRequest request, Exception exception)
        {
        }

        Assert.Throws<ArgumentNullException>(() => new ViewExtentPrefetchScheduler(null!, () => false, Failed, IdleDelay));
        Assert.Throws<ArgumentNullException>(() => new ViewExtentPrefetchScheduler(Prefetch, null!, Failed, IdleDelay));
        Assert.Throws<ArgumentNullException>(() => new ViewExtentPrefetchScheduler(Prefetch, () => false, null!, IdleDelay));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewExtentPrefetchScheduler(Prefetch, () => false, Failed, TimeSpan.Zero));
        using var harness = new Harness();
        Assert.Throws<ArgumentNullException>(() => harness.Scheduler.Notify(null!));
    }

    [Fact]
    public void PrefetchStartsOnlyAfterTheIdleDelay()
    {
        using var harness = new Harness();
        var request = Request(0, 0, WebMercator.ZoomZeroResolution / 1024);

        harness.Scheduler.Notify(request);
        harness.Clock.Advance(IdleDelay - TimeSpan.FromMilliseconds(1));
        Assert.Empty(harness.Calls);

        harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Same(request, Assert.Single(harness.Calls));
    }

    [Fact]
    public void ABurstOfNotificationsPrefetchesOnlyTheLatestViewAfterTheLastOne()
    {
        using var harness = new Harness();
        var last = Request(10000, 0, WebMercator.ZoomZeroResolution / 1024);

        for (var i = 0; i < 5; i++)
        {
            harness.Scheduler.Notify(Request(i * 1000, 0, WebMercator.ZoomZeroResolution / 1024));
            harness.Clock.Advance(IdleDelay - TimeSpan.FromMilliseconds(1));
        }

        harness.Scheduler.Notify(last);
        Assert.Empty(harness.Calls);

        harness.Clock.Advance(IdleDelay);
        Assert.Same(last, Assert.Single(harness.Calls));
    }

    [Fact]
    public void PrefetchWaitsForRenderingToFinishAndThenStarts()
    {
        using var harness = new Harness { Busy = true };
        harness.Scheduler.Notify(Request(0, 0, WebMercator.ZoomZeroResolution / 1024));

        harness.Clock.Advance(IdleDelay);
        harness.Clock.Advance(IdleDelay);
        Assert.Empty(harness.Calls);

        harness.Busy = false;
        harness.Clock.Advance(IdleDelay);

        Assert.Single(harness.Calls);
    }

    [Fact]
    public void TheSameBlockIsNotPrefetchedAgainButAnotherBlockOrZoomIs()
    {
        using var harness = new Harness();
        const double resolution = WebMercator.ZoomZeroResolution / 1024;

        harness.Scheduler.Notify(Request(0, 0, resolution));
        harness.Clock.Advance(IdleDelay);
        Assert.Single(harness.Calls);

        // 同じ 2×2 の中で動いた（ズーム 10 のタイルは約 39 km。原点はタイルの角なので、原点の右下の側で数 km 動く）
        harness.Scheduler.Notify(Request(5000, -5000, resolution));
        harness.Clock.Advance(IdleDelay);
        Assert.Single(harness.Calls);

        // 半タイルを越えて、別の 2×2 になった
        harness.Scheduler.Notify(Request(50000, 0, resolution));
        harness.Clock.Advance(IdleDelay);
        Assert.Equal(2, harness.Calls.Count);

        // 同じ中心でもズームが変わった（取得ズームが違えば 2×2 も違う）
        harness.Scheduler.Notify(Request(50000, 0, resolution / 2));
        harness.Clock.Advance(IdleDelay);
        Assert.Equal(3, harness.Calls.Count);
    }

    [Fact]
    public void MovingAcrossAHalfTileBoundaryPrefetchesTheNewNeighboursEvenInsideTheSameCenterTile()
    {
        using var harness = new Harness();
        const int zoom = 10;

        harness.Scheduler.Notify(RequestAtTile(zoom, 512.25, 512.25));
        harness.Clock.Advance(IdleDelay);
        Assert.Single(harness.Calls);

        // 同じ中心タイル（512）のまま、東の半分へ入った。必要な列は 511・512 から 512・513 へ変わる
        harness.Scheduler.Notify(RequestAtTile(zoom, 512.75, 512.25));
        harness.Clock.Advance(IdleDelay);
        Assert.Equal(2, harness.Calls.Count);

        // 同じ 2×2 の中の移動は繰り返さない
        harness.Scheduler.Notify(RequestAtTile(zoom, 512.9, 512.3));
        harness.Clock.Advance(IdleDelay);
        Assert.Equal(2, harness.Calls.Count);
    }

    [Fact]
    public async Task ANewNotificationCancelsARunningPrefetchAndTheSameTileIsTriedAgainAsync()
    {
        using var harness = new Harness();
        harness.Behavior = RunUntilCancelledAsync(harness);
        var request = Request(0, 0, WebMercator.ZoomZeroResolution / 1024);

        harness.Scheduler.Notify(request);
        harness.Clock.Advance(IdleDelay);
        Assert.Single(harness.Calls);
        Assert.False(harness.Cancelled.Task.IsCompleted);

        harness.Scheduler.Notify(request);
        await harness.Cancelled.Task.WaitAsync(Timeout).ConfigureAwait(true);
        Assert.Empty(harness.Failures);

        // 取り消された事前デコードは済んだことにしない。同じ 2×2 でも次の機会にやり直す
        harness.Behavior = (_, _) => Task.CompletedTask;
        harness.Clock.Advance(IdleDelay);
        Assert.Equal(2, harness.Calls.Count);
    }

    [Fact]
    public async Task DisposeCancelsARunningPrefetchAndStopsFurtherOnesAsync()
    {
        var harness = new Harness();
        harness.Behavior = RunUntilCancelledAsync(harness);
        harness.Scheduler.Notify(Request(0, 0, WebMercator.ZoomZeroResolution / 1024));
        harness.Clock.Advance(IdleDelay);
        Assert.Single(harness.Calls);

        harness.Dispose();
        await harness.Cancelled.Task.WaitAsync(Timeout).ConfigureAwait(true);

        harness.Scheduler.Notify(Request(50000, 0, WebMercator.ZoomZeroResolution / 1024));
        harness.Clock.Advance(IdleDelay);
        Assert.Single(harness.Calls);
        Assert.Empty(harness.Failures);
    }

    [Fact]
    public void DisposeBeforeTheIdleDelayPreventsThePrefetchAndCanBeCalledTwice()
    {
        var harness = new Harness();
        harness.Scheduler.Notify(Request(0, 0, WebMercator.ZoomZeroResolution / 1024));

        harness.Dispose();
        harness.Dispose();
        harness.Clock.Advance(IdleDelay);

        Assert.Empty(harness.Calls);
    }

    [Fact]
    public void AFailureIsReportedOnceAndTheSameBlockIsNotRetried()
    {
        using var harness = new Harness();
        var failure = new InvalidOperationException("取得の失敗（テスト）");
        harness.Behavior = (_, _) => Task.FromException(failure);
        const double resolution = WebMercator.ZoomZeroResolution / 1024;

        harness.Scheduler.Notify(Request(0, 0, resolution));
        harness.Clock.Advance(IdleDelay);

        var (failedRequest, exception) = Assert.Single(harness.Failures);
        Assert.Same(harness.Calls[0], failedRequest);
        Assert.Same(failure, exception);

        // 同じ 2×2 では、失敗の記録があふれないよう繰り返さない
        harness.Scheduler.Notify(Request(1000, -1000, resolution));
        harness.Clock.Advance(IdleDelay);
        Assert.Single(harness.Calls);

        // 2×2 が変われば、また試みる
        harness.Scheduler.Notify(Request(50000, 0, resolution));
        harness.Clock.Advance(IdleDelay);
        Assert.Equal(2, harness.Calls.Count);
        Assert.Equal(2, harness.Failures.Count);
    }

    private static Func<ViewExtentRequest, CancellationToken, Task> RunUntilCancelledAsync(Harness harness)
        => async (_, token) =>
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.Infinite, token).ConfigureAwait(false);
            }
            finally
            {
                harness.Cancelled.TrySetResult();
            }
        };

    // 指定したズームで、中心がタイル座標 (tileX, tileY)（タイルの端数を含む）になる視点
    private static ViewExtentRequest RequestAtTile(int zoom, double tileX, double tileY)
    {
        var tiles = Math.Pow(2, zoom);
        var centerX = (tileX / tiles * 2 * Edge) - Edge;
        var centerY = Edge - (tileY / tiles * 2 * Edge);
        return Request(centerX, centerY, WebMercator.ZoomZeroResolution / tiles);
    }

    private static ViewExtentRequest Request(double centerX, double centerY, double resolution, double density = 1.0)
    {
        Assert.True(ViewExtentRequest.TryCreate(centerX, centerY, resolution, 800, 600, density, "test", out var request));
        return request;
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Scheduler = new ViewExtentPrefetchScheduler(
                (request, token) =>
                {
                    Calls.Add(request);
                    return Behavior(request, token);
                },
                () => Busy,
                (request, exception) => Failures.Add((request, exception)),
                IdleDelay,
                Clock);
        }

        public ManualTimeProvider Clock { get; } = new();

        public ViewExtentPrefetchScheduler Scheduler { get; }

        public List<ViewExtentRequest> Calls { get; } = [];

        public List<(ViewExtentRequest Request, Exception Exception)> Failures { get; } = [];

        public bool Busy { get; set; }

        public Func<ViewExtentRequest, CancellationToken, Task> Behavior { get; set; } = (_, _) => Task.CompletedTask;

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Scheduler.Dispose();
    }
}
