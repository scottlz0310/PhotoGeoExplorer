using PhotoMapStudio.Spike;
using SkiaSharp;
using Xunit;

namespace PhotoGeoExplorer.SpikeVector.Tests;

public sealed class ViewRenderSchedulerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // 1 DIP = 1 m、寸法 100×100 の要求。中心の x が違えば、範囲が違う
    private static RenderRequest Request(long generation, double centerX = 0, string source = "s", int style = 1)
        => new(centerX, 0, 1.0, 100, 100, 1.0, source, style, generation);

    private static RefCountedImage NewImage()
    {
        using var surface = SKSurface.Create(new SKImageInfo(2, 2));
        return new RefCountedImage(surface.Snapshot());
    }

    /// <summary>描画を、テストが完了させるまで止める。</summary>
    private sealed class Harness : IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<long, TaskCompletionSource<bool>> _gates = new();
        private readonly Dictionary<long, TaskCompletionSource<bool>> _started = new();
        private readonly List<RefCountedImage> _images = new();
        private readonly List<long> _delivered = new();
        private readonly List<long> _rendered = new();
        private readonly List<RefCountedImage> _deliveredImages = new();

        public Harness()
        {
            Scheduler = new ViewRenderScheduler(RenderAsync, Deliver);
        }

        public ViewRenderScheduler Scheduler { get; }

        public Func<RenderRequest, bool> FailOn { get; set; } = _ => false;

        /// <summary>キャンセルを無視して完了する描画（キャンセルが間に合わず、結果が出てしまう場面）。</summary>
        public bool IgnoreCancellation { get; set; }

        public long[] Delivered
        {
            get
            {
                lock (_gate)
                {
                    return _delivered.ToArray();
                }
            }
        }

        public long[] RenderedGenerations
        {
            get
            {
                lock (_gate)
                {
                    return _rendered.ToArray();
                }
            }
        }

        public RefCountedImage[] DeliveredImages
        {
            get
            {
                lock (_gate)
                {
                    return _deliveredImages.ToArray();
                }
            }
        }

        public RefCountedImage[] AllImages
        {
            get
            {
                lock (_gate)
                {
                    return _images.ToArray();
                }
            }
        }

        public void Dispose() => Scheduler.Dispose();

        public Task WaitStartedAsync(long generation) => StartedGate(generation).Task.WaitAsync(Timeout);

        public void Complete(long generation) => Gate(generation).TrySetResult(true);

        public async Task WaitUntilIdleAsync()
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (Scheduler.IsBusy)
            {
                Assert.True(DateTime.UtcNow < deadline, "スケジューラーが終わりませんでした。");
                await Task.Delay(5);
            }
        }

        private TaskCompletionSource<bool> Gate(long g)
        {
            lock (_gate)
            {
                if (!_gates.TryGetValue(g, out var tcs))
                {
                    _gates[g] = tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                return tcs;
            }
        }

        private TaskCompletionSource<bool> StartedGate(long g)
        {
            lock (_gate)
            {
                if (!_started.TryGetValue(g, out var tcs))
                {
                    _started[g] = tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                return tcs;
            }
        }

        private async Task<RenderOutput> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _rendered.Add(request.Generation);
            }

            StartedGate(request.Generation).TrySetResult(true);
            await (IgnoreCancellation ? Gate(request.Generation).Task : Gate(request.Generation).Task.WaitAsync(cancellationToken));
            if (FailOn(request))
            {
                throw new InvalidOperationException("render failed");
            }

            var image = NewImage();
            lock (_gate)
            {
                _images.Add(image);
            }

            return new RenderOutput(image, default);
        }

        private void Deliver(RenderResult result)
        {
            lock (_gate)
            {
                _delivered.Add(result.Request.Generation);
                _deliveredImages.Add(result.Image);
            }
        }
    }

    [Fact]
    public async Task UnstartedOlderRequestsAreReplacedByTheNewest()
    {
        using var h = new Harness();

        h.Scheduler.Submit(Request(1));
        await h.WaitStartedAsync(1);
        for (var g = 2L; g <= 5; g++)
        {
            h.Scheduler.Submit(Request(g));
        }

        h.Complete(1);
        await h.WaitStartedAsync(5);
        h.Complete(5);
        await h.WaitUntilIdleAsync();

        Assert.Equal(new long[] { 1, 5 }, h.RenderedGenerations);
        var stats = h.Scheduler.Stats;
        Assert.Equal(5, stats.Submitted);
        Assert.Equal(3, stats.Replaced);
        Assert.Equal(2, stats.Started);
    }

    [Fact]
    public async Task ARunningRenderRunsToCompletionAndIsAppliedWhileANewerOverlappingRequestWaits()
    {
        using var h = new Harness();

        h.Scheduler.Submit(Request(1, centerX: 0));
        await h.WaitStartedAsync(1);
        h.Scheduler.Submit(Request(2, centerX: 30));
        h.Complete(1);
        await h.WaitStartedAsync(2);

        // 世代 1 は、最新と重なるので反映される（連続操作中に、確定描画が完了せず白紙化しない）
        Assert.Equal(new long[] { 1 }, h.Delivered);
        h.Complete(2);
        await h.WaitUntilIdleAsync();
        Assert.Equal(new long[] { 1, 2 }, h.Delivered);
        Assert.Equal(0, h.Scheduler.Stats.Cancelled);
    }

    [Fact]
    public async Task ARunningRenderIsCancelledWhenTheNewestViewNoLongerOverlaps()
    {
        using var h = new Harness();

        h.Scheduler.Submit(Request(1, centerX: 0));
        await h.WaitStartedAsync(1);
        h.Scheduler.Submit(Request(2, centerX: 10_000));
        await h.WaitStartedAsync(2);
        h.Complete(2);
        await h.WaitUntilIdleAsync();

        Assert.Equal(new long[] { 2 }, h.Delivered);
        Assert.Equal(1, h.Scheduler.Stats.Cancelled);
    }

    [Fact]
    public async Task ChangingTheSourceCancelsTheRunningRenderAndOldResultsAreNeverApplied()
    {
        using var h = new Harness();

        h.Scheduler.Submit(Request(1, source: "a"));
        await h.WaitStartedAsync(1);
        h.Scheduler.Submit(Request(2, source: "b"));
        await h.WaitStartedAsync(2);
        h.Complete(2);
        await h.WaitUntilIdleAsync();

        Assert.Equal(new long[] { 2 }, h.Delivered);
        Assert.Equal(1, h.Scheduler.Stats.Cancelled);
    }

    [Fact]
    public async Task AStyleChangeAfterAFinishedRenderAppliesTheNewStyleResult()
    {
        using var h = new Harness();

        h.Scheduler.Submit(Request(1, style: 1));
        await h.WaitStartedAsync(1);
        h.Complete(1);
        await h.WaitUntilIdleAsync();
        Assert.Equal(new long[] { 1 }, h.Delivered);

        h.Scheduler.Submit(Request(2, style: 2));
        await h.WaitStartedAsync(2);
        h.Complete(2);
        await h.WaitUntilIdleAsync();
        Assert.Equal(new long[] { 1, 2 }, h.Delivered);
    }

    [Fact]
    public async Task ACancelledRendersImageIsNeverDeliveredAndEveryUndeliveredImageIsReleased()
    {
        using var h = new Harness();

        h.Scheduler.Submit(Request(1, style: 1));
        await h.WaitStartedAsync(1);
        h.Scheduler.Submit(Request(2, style: 2));
        await h.WaitStartedAsync(2);
        h.Complete(2);
        await h.WaitUntilIdleAsync();

        Assert.Equal(new long[] { 2 }, h.Delivered);
        var delivered = h.DeliveredImages;
        Assert.All(h.AllImages.Where(i => !delivered.Contains(i)), i => Assert.True(i.IsDisposed));
    }

    [Fact]
    public async Task AFinishedResultIncompatibleWithTheLatestRequestIsDiscardedAndReleased()
    {
        using var h = new Harness { IgnoreCancellation = true };

        h.Scheduler.Submit(Request(1, style: 1));
        await h.WaitStartedAsync(1);
        h.Scheduler.Submit(Request(2, style: 2));
        h.Complete(1);
        await h.WaitStartedAsync(2);
        h.Complete(2);
        await h.WaitUntilIdleAsync();

        Assert.Equal(new long[] { 2 }, h.Delivered);
        Assert.Equal(1, h.Scheduler.Stats.DiscardedStale);
        var delivered = h.DeliveredImages;
        var discarded = h.AllImages.Single(i => !delivered.Contains(i));
        Assert.True(discarded.IsDisposed);
    }

    [Fact]
    public async Task AFinishedResultThatNoLongerOverlapsTheLatestViewIsDiscarded()
    {
        using var h = new Harness { IgnoreCancellation = true };

        h.Scheduler.Submit(Request(1, centerX: 0));
        await h.WaitStartedAsync(1);
        h.Scheduler.Submit(Request(2, centerX: 10_000));
        h.Complete(1);
        await h.WaitStartedAsync(2);
        h.Complete(2);
        await h.WaitUntilIdleAsync();

        Assert.Equal(new long[] { 2 }, h.Delivered);
        Assert.Equal(1, h.Scheduler.Stats.DiscardedStale);
    }

    [Fact]
    public async Task AnOlderGenerationSubmittedLaterIsNeverAppliedOverANewerOne()
    {
        using var h = new Harness();

        h.Scheduler.Submit(Request(10));
        await h.WaitStartedAsync(10);
        h.Complete(10);
        await h.WaitUntilIdleAsync();

        h.Scheduler.Submit(Request(7));
        await h.WaitStartedAsync(7);
        h.Complete(7);
        await h.WaitUntilIdleAsync();

        Assert.Equal(new long[] { 10 }, h.Delivered);
        Assert.Equal(1, h.Scheduler.Stats.DiscardedStale);
    }

    [Fact]
    public async Task ResultsAreAppliedMonotonicallyByGeneration()
    {
        using var h = new Harness();

        for (var g = 1L; g <= 20; g++)
        {
            h.Scheduler.Submit(Request(g, centerX: g));
            if (g % 3 == 0)
            {
                var running = h.RenderedGenerations.Last();
                await h.WaitStartedAsync(running);
                h.Complete(running);
            }
        }

        foreach (var g in Enumerable.Range(1, 20))
        {
            h.Complete(g);
        }

        await h.WaitUntilIdleAsync();
        var delivered = h.Delivered;
        Assert.Equal(delivered.OrderBy(x => x).ToArray(), delivered);
        Assert.Equal(delivered.Distinct().Count(), delivered.Length);
        Assert.Equal(20, delivered.Last());
    }

    [Fact]
    public async Task DisposeCancelsTheRunningRenderDeliversNothingAndRejectsNewRequests()
    {
        var h = new Harness();

        h.Scheduler.Submit(Request(1));
        await h.WaitStartedAsync(1);
        h.Scheduler.Dispose();
        h.Complete(1);
        await h.WaitUntilIdleAsync();

        Assert.Empty(h.Delivered);
        Assert.Throws<ObjectDisposedException>(() => h.Scheduler.Submit(Request(2)));
    }

    [Fact]
    public async Task ARenderFailureIsCountedAndTheNextRequestStillRuns()
    {
        using var h = new Harness();
        h.FailOn = r => r.Generation == 1;

        h.Scheduler.Submit(Request(1));
        await h.WaitStartedAsync(1);
        h.Scheduler.Submit(Request(2));
        h.Complete(1);
        await h.WaitStartedAsync(2);
        h.Complete(2);
        await h.WaitUntilIdleAsync();

        Assert.Equal(new long[] { 2 }, h.Delivered);
        Assert.Equal(1, h.Scheduler.Stats.Failed);
    }

    [Fact]
    public async Task ADeliveredImageIsReleasedExactlyOnceByTheDeliveryTarget()
    {
        using var h = new Harness();

        h.Scheduler.Submit(Request(1));
        await h.WaitStartedAsync(1);
        h.Complete(1);
        await h.WaitUntilIdleAsync();

        var image = Assert.Single(h.DeliveredImages);
        Assert.False(image.IsDisposed);
        image.Release();
        Assert.True(image.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => image.Release());
    }
}

public sealed class RefCountedImageTests
{
    private static RefCountedImage NewImage()
    {
        using var surface = SKSurface.Create(new SKImageInfo(2, 2));
        return new RefCountedImage(surface.Snapshot());
    }

    [Fact]
    public void TheImageSurvivesUntilTheLastReferenceIsReleased()
    {
        var image = NewImage();
        image.Acquire();
        Assert.Equal(2, image.ReferenceCount);

        image.Release();
        Assert.False(image.IsDisposed);
        _ = image.Image.Width;

        image.Release();
        Assert.True(image.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => image.Acquire());
        Assert.Throws<ObjectDisposedException>(() => image.Image);
    }

    [Fact]
    public async Task ConcurrentAcquireAndReleaseKeepTheCountConsistent()
    {
        var image = NewImage();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                image.Acquire();
                image.Release();
            }
        })));

        Assert.Equal(1, image.ReferenceCount);
        Assert.False(image.IsDisposed);
        image.Release();
        Assert.True(image.IsDisposed);
    }
}

public sealed class DecodedTileCacheTests
{
    private static TileKey Key(int x) => new("s", 14, x, 0);

    [Fact]
    public void LeastRecentlyUsedEntriesAreEvictedWhenTheByteLimitIsExceeded()
    {
        var cache = new DecodedTileCache(1000);
        cache.Add(Key(1), null, 400);
        cache.Add(Key(2), null, 400);

        Assert.True(cache.TryGet(Key(1), out _)); // 1 を最近使ったものにする
        cache.Add(Key(3), null, 400);              // 合計 1200 > 1000 → 古い 2 を捨てる

        Assert.True(cache.TryGet(Key(1), out _));
        Assert.False(cache.TryGet(Key(2), out _));
        Assert.True(cache.TryGet(Key(3), out _));
        Assert.Equal(1, cache.Stats.Evictions);
        Assert.Equal(800, cache.Bytes);
    }

    [Fact]
    public void AnEntryLargerThanTheLimitIsStillKeptAlone()
    {
        var cache = new DecodedTileCache(100);
        cache.Add(Key(1), null, 500);

        Assert.True(cache.TryGet(Key(1), out _));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ReplacingAKeyUpdatesTheByteTotal()
    {
        var cache = new DecodedTileCache(10_000);
        cache.Add(Key(1), null, 300);
        cache.Add(Key(1), null, 500);

        Assert.Equal(1, cache.Count);
        Assert.Equal(500, cache.Bytes);
    }

    [Fact]
    public async Task ConcurrentAccessKeepsTheCacheConsistent()
    {
        var cache = new DecodedTileCache(10_000);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                var key = Key((t * 100) + (i % 50));
                if (!cache.TryGet(key, out _))
                {
                    cache.Add(key, null, 200);
                }
            }
        })));

        Assert.True(cache.Bytes <= 10_000 + 200);
        Assert.Equal(cache.Count * 200L, cache.Bytes);
    }
}
