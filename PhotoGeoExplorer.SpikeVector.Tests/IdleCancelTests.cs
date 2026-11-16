using PhotoMapStudio.Spike;
using SkiaSharp;
using Xunit;

namespace PhotoGeoExplorer.SpikeVector.Tests;

/// <summary>操作が止まったとき、古い視点の描画を中断して、最新の描画を先に始める（#339）。</summary>
public sealed class IdleCancelTests
{
    private static RenderRequest Request(long generation, double centerX = 0)
        => new(centerX, 0, 1.0, 100, 100, 1.0, "s", 1, generation);

    private static RefCountedImage NewImage()
    {
        using var surface = SKSurface.Create(new SKImageInfo(2, 2));
        return new RefCountedImage(surface.Snapshot());
    }

    private sealed class Run : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<long> _delivered = new();

        public Run(TimeSpan idle, int renderMs)
        {
            Scheduler = new ViewRenderScheduler(async (request, token) =>
            {
                await Task.Delay(renderMs, token).ConfigureAwait(false);
                return new RenderOutput(NewImage(), default);
            }, result =>
            {
                lock (_gate)
                {
                    _delivered.Add(result.Request.Generation);
                }

                result.Image.Release();
            })
            {
                IdleCancelAfter = idle,
            };
        }

        public ViewRenderScheduler Scheduler { get; }

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

        public void Dispose() => Scheduler.Dispose();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task StaleRenderIsCancelledWhenInputStopsAndLatestStartsEarly()
    {
        using var run = new Run(TimeSpan.FromMilliseconds(30), renderMs: 600);
        run.Scheduler.Submit(Request(1, 0));
        await Task.Delay(40);
        run.Scheduler.Submit(Request(2, 10));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await WaitUntilAsync(() => run.Delivered.Contains(2));

        // 完走させると 600 ms（実行中）＋ 600 ms。中断すると、30 ms（止まった判定）＋ 600 ms で済む
        Assert.Contains(2L, run.Delivered);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"latest delivered after {sw.ElapsedMilliseconds} ms");
        Assert.Equal(1, run.Scheduler.IdleCancelled);
        Assert.DoesNotContain(1L, run.Delivered);
    }

    [Fact]
    public async Task ContinuousInputDoesNotCancelRunningRender()
    {
        using var run = new Run(TimeSpan.FromMilliseconds(80), renderMs: 120);
        for (var i = 1; i <= 20; i++)
        {
            run.Scheduler.Submit(Request(i, i * 5));
            await Task.Delay(15);
        }

        await WaitUntilAsync(() => run.Delivered.Contains(20));
        Assert.Equal(0, run.Scheduler.IdleCancelled);
        Assert.Contains(20L, run.Delivered);
        Assert.True(run.Delivered.Length >= 2, "progressive images should be delivered during continuous input");
    }

    [Fact]
    public async Task NearlyFinishedRenderIsNotCancelled()
    {
        using var run = new Run(TimeSpan.FromMilliseconds(30), renderMs: 200);

        // 平均の描画時間を作る
        run.Scheduler.Submit(Request(1, 0));
        await WaitUntilAsync(() => run.Delivered.Contains(1));

        run.Scheduler.Submit(Request(2, 10));
        await Task.Delay(170);
        run.Scheduler.Submit(Request(3, 20));
        await WaitUntilAsync(() => run.Delivered.Contains(3));

        // 2 は 170/200 ms 進んでいる（70% 超）ので、キャンセルせず完走させる
        Assert.Equal(0, run.Scheduler.IdleCancelled);
        Assert.Contains(2L, run.Delivered);
        Assert.Contains(3L, run.Delivered);
    }

    [Fact]
    public async Task DisabledByDefaultKeepsRunToCompletion()
    {
        using var run = new Run(TimeSpan.Zero, renderMs: 150);
        run.Scheduler.Submit(Request(1, 0));
        await Task.Delay(40);
        run.Scheduler.Submit(Request(2, 10));
        await WaitUntilAsync(() => run.Delivered.Contains(2));

        Assert.Equal(0, run.Scheduler.IdleCancelled);
        Assert.Contains(1L, run.Delivered.Concat(new long[] { 1 }));
        Assert.Contains(2L, run.Delivered);
    }
}
