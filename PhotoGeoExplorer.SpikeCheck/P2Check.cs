using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mapsui;
using Mapsui.Projections;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Spike;

// スパイク #293 の P2: 背景スレッド描画・要求の集約・世代管理・デコード済みキャッシュの応答性を、保存済み MVT で測る（ヘッドレス）。
// P0 の操作手順（ジャンプ・ズーム切り替え・連続ズーム・パン・再訪）を、描画の同期（約 60 Hz）ごとに 1 回の要求として再現する。
// UI スレッドのフレーム時間は、ここでは測れない（実アプリの計測で行う）。ここで測るのは、画像の確定までの時間・要求の集約・白紙化の有無・メモリ。
internal static class P2Check
{
    private const double Width = 683;
    private const double Height = 610;

    private static readonly (string Name, double Lat, double Lon)[] Locations =
    {
        ("dense-odori", 43.0606, 141.3469),
        ("suburb-okadama", 43.1180, 141.3900),
    };

    public static async Task<int> RunAsync(string mvtRoot, string outDir, double density)
    {
        Directory.CreateDirectory(outDir);
        var rows = new List<Dictionary<string, object?>>();
        foreach (var useCache in new[] { false, true })
        {
            foreach (var (name, lat, lon) in Locations)
            {
                rows.AddRange(await RunLocationAsync(mvtRoot, name, lat, lon, density, useCache).ConfigureAwait(false));
            }
        }

        var report = new
        {
            density,
            width = Width,
            height = Height,
            skiaSharp = typeof(SkiaSharp.SKCanvas).Assembly.GetName().Version?.ToString(),
            runtime = Environment.Version.ToString(),
            processors = Environment.ProcessorCount,
            rows,
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var name2 = "p2-headless-d" + density.ToString("0.00", CultureInfo.InvariantCulture) + ".json";
        await File.WriteAllTextAsync(Path.Combine(outDir, name2), json).ConfigureAwait(false);
        Console.WriteLine(Summarize(rows));
        return 0;
    }

    private static async Task<List<Dictionary<string, object?>>> RunLocationAsync(string mvtRoot, string name, double lat, double lon, double density, bool useCache)
    {
        var rows = new List<Dictionary<string, object?>>();
        var cache = useCache ? new DecodedTileCache(64L * 1024 * 1024) : null;
        var renderer = new ViewExtentVectorRenderer(new SavedMvtTileSource(mvtRoot), new OpenMapTilesStyle(), cache);
        var sourceId = "saved-mvt:" + mvtRoot;
        var generation = 0L;
        var center = SphericalMercator.FromLonLat(new MPoint(lon, lat));
        var mode = useCache ? "cache" : "nocache";

        RenderRequest Make(double cx, double cy, double zoom, string source = null!)
            => new(cx, cy, ViewExtentInjection.ResolutionFromZoom(zoom, density), Width, Height, density, source ?? sourceId, 1, ++generation);

        // 1. 確定描画（ジャンプ・ズーム切り替え）: 要求を 1 つ出して、画像が反映されるまで
        var settle = new List<(string Step, double Ms, RenderTimings T)>();
        using (var session = new Session(renderer))
        {
            foreach (var zoom in new[] { 18.0, 15.0, 16.0, 17.0, 18.0, 19.0 })
            {
                var request = Make(center.X, center.Y, zoom);
                var sw = Stopwatch.StartNew();
                session.Scheduler.Submit(request);
                var result = await session.WaitDeliveredAsync(request.Generation).ConfigureAwait(false);
                settle.Add(($"z{zoom:0}", sw.Elapsed.TotalMilliseconds, result.Timings));
            }

            // 再訪（同じ地点・同じズーム）
            var again = Make(center.X, center.Y, 18.0);
            var sw2 = Stopwatch.StartNew();
            session.Scheduler.Submit(again);
            var r2 = await session.WaitDeliveredAsync(again.Generation).ConfigureAwait(false);
            settle.Add(("revisit-z18", sw2.Elapsed.TotalMilliseconds, r2.Timings));
        }

        foreach (var (step, ms, t) in settle)
        {
            rows.Add(Row(name, mode, "settle:" + step, ("settleMs", Math.Round(ms, 1)), ("fetchMs", Ms(t.Fetch)), ("decodeMs", Ms(t.Decode)), ("drawMs", Ms(t.Draw)), ("tiles", t.Tiles), ("cacheHits", t.CacheHits), ("polygonsMs", Ms(t.Polygons)), ("linesMs", Ms(t.Lines)), ("symbolsMs", Ms(t.Symbols)), ("lineLabelsMs", Ms(t.LineLabels)), ("labelsMs", Ms(t.Labels))));
        }

        // 2. 連続操作（約 60 Hz で 150 回）: 連続ズーム z15→z19、パン（z17 で 6px ずつ）
        rows.Add(await RunContinuousAsync(renderer, name, mode, "continuous-zoom", sourceId, density, i =>
        {
            var zoom = 15.0 + (4.0 * i / 149.0);
            return (center.X, center.Y, zoom);
        }, () => ++generation).ConfigureAwait(false));
        rows.Add(await RunContinuousAsync(renderer, name, mode, "pan", sourceId, density, i =>
        {
            var res = ViewExtentInjection.ResolutionFromZoom(17.0, density);
            return (center.X + (i * 6.0 * res), center.Y, 17.0);
        }, () => ++generation).ConfigureAwait(false));

        // 3. 描画中のソース切り替え・破棄
        rows.Add(await RunSourceSwitchAsync(renderer, name, mode, sourceId, density, center, () => ++generation).ConfigureAwait(false));

        // 4. 解放の確認（参照カウントの漏れ）と、メモリ
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        rows.Add(Row(name, mode, "memory", ("liveImages", RefCountedImage.LiveCount), ("cacheBytesMB", cache is null ? 0 : Math.Round(cache.Bytes / 1048576.0, 1)), ("cacheTiles", cache?.Count ?? 0), ("gcHeapMB", Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1)), ("workingSetMB", Math.Round(Environment.WorkingSet / 1048576.0, 1))));
        return rows;
    }

    private static async Task<Dictionary<string, object?>> RunContinuousAsync(
        ViewExtentVectorRenderer renderer,
        string name,
        string mode,
        string step,
        string sourceId,
        double density,
        Func<int, (double X, double Y, double Zoom)> view,
        Func<long> nextGeneration)
    {
        using var session = new Session(renderer);
        var submitted = new List<(long Generation, long Tick, RenderRequest Request)>();
        const int Count = 150;
        var interval = Stopwatch.Frequency / 60.0;
        var blank = 0;
        var maxStaleMs = 0.0;

        // 操作の前に、最初の視点の画像が出ている状態にする（P0 の手順は、直前の画面が出ている状態から始める）
        var (x0, y0, z0) = view(0);
        var prime = new RenderRequest(x0, y0, ViewExtentInjection.ResolutionFromZoom(z0, density), Width, Height, density, sourceId, 1, nextGeneration());
        session.Scheduler.Submit(prime);
        await session.WaitDeliveredAsync(prime.Generation).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < Count; i++)
        {
            var target = start + (long)(i * interval);
            while (Stopwatch.GetTimestamp() < target)
            {
                Thread.SpinWait(50);
            }

            var (x, y, zoom) = view(i);
            var request = new RenderRequest(x, y, ViewExtentInjection.ResolutionFromZoom(zoom, density), Width, Height, density, sourceId, 1, nextGeneration());
            submitted.Add((request.Generation, Stopwatch.GetTimestamp(), request));
            session.Scheduler.Submit(request);

            // 白紙化: いま表示中の画像が、新しい視点と重ならない（または、まだ 1 枚も無い）時間を数える
            var shown = session.Shown;
            if (shown is null || !shown.Extent.Intersects(request.Extent))
            {
                blank++;
            }

            if (shown is not null)
            {
                maxStaleMs = Math.Max(maxStaleMs, (Stopwatch.GetTimestamp() - shown.DeliveredTick) * 1000.0 / Stopwatch.Frequency);
            }
        }

        var last = submitted[^1];
        var final = await session.WaitDeliveredAsync(last.Generation).ConfigureAwait(false);
        var settleAfterLastMs = (Stopwatch.GetTimestamp() - last.Tick) * 1000.0 / Stopwatch.Frequency;
        var stats = session.Scheduler.Stats;

        // 各要求が、その世代以降の画像に反映されるまでの時間（入力→画像）
        var latencies = new List<double>();
        foreach (var s in submitted)
        {
            var delivered = session.DeliveredSnapshot().FirstOrDefault(d => d.Generation >= s.Generation);
            if (delivered is not null)
            {
                latencies.Add((delivered.DeliveredTick - s.Tick) * 1000.0 / Stopwatch.Frequency);
            }
        }

        latencies.Sort();
        var durations = session.DeliveredSnapshot().Select(d => d.Duration.TotalMilliseconds).OrderBy(x => x).ToList();
        return Row(
            name,
            mode,
            step,
            ("requests", stats.Submitted),
            ("replaced", stats.Replaced),
            ("rendered", stats.Started),
            ("delivered", stats.Delivered),
            ("discardedStale", stats.DiscardedStale),
            ("cancelled", stats.Cancelled),
            ("failed", stats.Failed),
            ("blankFrames", blank),
            ("maxStaleMs", Math.Round(maxStaleMs, 1)),
            ("inputToImageP50Ms", Math.Round(Percentile(latencies, 0.5), 1)),
            ("inputToImageP95Ms", Math.Round(Percentile(latencies, 0.95), 1)),
            ("renderMsP50", Math.Round(Percentile(durations, 0.5), 1)),
            ("renderMsP95", Math.Round(Percentile(durations, 0.95), 1)),
            ("settleAfterLastInputMs", Math.Round(settleAfterLastMs, 1)),
            ("finalCacheHits", final.Timings.CacheHits));
    }

    private static async Task<Dictionary<string, object?>> RunSourceSwitchAsync(
        ViewExtentVectorRenderer renderer,
        string name,
        string mode,
        string sourceId,
        double density,
        MPoint center,
        Func<long> nextGeneration)
    {
        using var session = new Session(renderer);
        var res = ViewExtentInjection.ResolutionFromZoom(17.0, density);
        // 描画を始めさせてから、すぐ別のソース・スタイルの要求を出す（古いソースの結果は反映されない）
        for (var i = 0; i < 20; i++)
        {
            session.Scheduler.Submit(new RenderRequest(center.X + (i * 5 * res), center.Y, res, Width, Height, density, sourceId, 1, nextGeneration()));
            await Task.Delay(1).ConfigureAwait(false);
        }

        var switched = new RenderRequest(center.X, center.Y, res, Width, Height, density, sourceId + "#other", 2, nextGeneration());
        session.Scheduler.Submit(switched);
        var result = await session.WaitDeliveredAsync(switched.Generation).ConfigureAwait(false);
        var all = session.DeliveredSnapshot();
        var firstNewTick = all.First(d => d.SourceId != sourceId).DeliveredTick;
        var staleApplied = all.Count(d => d.SourceId == sourceId && d.DeliveredTick > firstNewTick);
        var stats = session.Scheduler.Stats;
        return Row(name, mode, "source-switch", ("delivered", stats.Delivered), ("cancelled", stats.Cancelled), ("discardedStale", stats.DiscardedStale), ("oldSourceAppliedAfterSwitch", staleApplied), ("finalSourceIsNew", result.SourceId == switched.SourceId));
    }

    private static Dictionary<string, object?> Row(string location, string mode, string step, params (string Key, object? Value)[] values)
    {
        var d = new Dictionary<string, object?> { ["location"] = location, ["mode"] = mode, ["step"] = step };
        foreach (var (k, v) in values)
        {
            d[k] = v;
        }

        return d;
    }

    private static double Ms(TimeSpan t) => Math.Round(t.TotalMilliseconds, 1);

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static string Summarize(List<Dictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            sb.Append(string.Join("  ", r.Select(kv => kv.Key + "=" + Convert.ToString(kv.Value, CultureInfo.InvariantCulture)))).AppendLine();
        }

        return sb.ToString();
    }

    private sealed record Delivered(long Generation, string SourceId, MRect Extent, long DeliveredTick, TimeSpan Duration, RenderTimings Timings);

    /// <summary>UI の代わり。結果を受け取り、直前の画像の参照を手放して、最新の参照だけを持つ。</summary>
    private sealed class Session : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<Delivered> _delivered = new();
        private readonly Dictionary<long, TaskCompletionSource<Delivered>> _waiters = new();
        private RefCountedImage? _current;
        private Delivered? _shown;

        public Session(ViewExtentVectorRenderer renderer)
        {
            Scheduler = new ViewRenderScheduler((request, token) => renderer.RenderAsync(request, token), Deliver);
        }

        public ViewRenderScheduler Scheduler { get; }

        public Delivered? Shown
        {
            get
            {
                lock (_gate)
                {
                    return _shown;
                }
            }
        }

        public List<Delivered> DeliveredSnapshot()
        {
            lock (_gate)
            {
                return _delivered.ToList();
            }
        }

        public async Task<Delivered> WaitDeliveredAsync(long generation)
        {
            TaskCompletionSource<Delivered> tcs;
            lock (_gate)
            {
                var existing = _delivered.FirstOrDefault(d => d.Generation >= generation);
                if (existing is not null)
                {
                    return existing;
                }

                if (!_waiters.TryGetValue(generation, out tcs!))
                {
                    _waiters[generation] = tcs = new TaskCompletionSource<Delivered>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Scheduler.Dispose();
            lock (_gate)
            {
                _current?.Release();
                _current = null;
            }
        }

        private void Deliver(RenderResult result)
        {
            var delivered = new Delivered(result.Request.Generation, result.Request.SourceId, result.Request.Extent, Stopwatch.GetTimestamp(), result.Duration, result.Timings);
            lock (_gate)
            {
                _current?.Release();
                _current = result.Image;
                _shown = delivered;
                _delivered.Add(delivered);
                foreach (var key in _waiters.Keys.Where(k => k <= delivered.Generation).ToList())
                {
                    _waiters[key].TrySetResult(delivered);
                    _waiters.Remove(key);
                }
            }
        }
    }
}
