#pragma warning disable CA1031, CA1869, CA1822, CA1305, CA1848, CA2007, CA1859, CA1860, CA1861
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Tiling.Layers;
using Mapsui.UI.WinUI;
using Microsoft.UI.Dispatching;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services;
using PhotoMapStudio.Spike;

namespace PhotoGeoExplorer.Diagnostics.MapBench;

/// <summary>
/// 地図ペインの応答性を、決まった手順で計測する（スパイク #293 の P0。環境変数 PGE_BENCH_OUT があるときだけ動く）。
/// 現行版と PoC を同じ手順で比べるため、手順は <see cref="Locations"/> と各 Run* に固定している。
/// </summary>
internal sealed class MapBenchRunner
{
    private const int DefaultZoomLevel = 18;
    private const int ContinuousTicks = 150;
    private const int PanTicks = 150;
    private const double PanPixelsPerTick = 6;
    private const int IdleTimeoutMs = 60_000;

    private static readonly (string Name, double Lat, double Lon)[] Locations =
    {
        ("dense-odori", 43.0606, 141.3469),
        ("suburb-okadama", 43.1180, 141.3900),
        ("tile-boundary-z15", double.NaN, double.NaN),
    };

    private readonly MapControl _mapControl;
    private readonly Mapsui.Map _map;
    private readonly TileLayer _tileLayer;
    private readonly MemoryLayer _markerLayer;
    private readonly BenchMapRenderer _renderer;
    private readonly P2Backend? _p2;
    private readonly string _outDir;
    private readonly string _label;
    private readonly DispatcherQueue _queue;
    private readonly MapMarkerPresenter _markers = new(new MapPaneService());
    private readonly Dictionary<string, object?> _result = new();
    private readonly List<Dictionary<string, object?>> _phases = new();

    private MapBenchRunner(MapControl mapControl, Mapsui.Map map, TileLayer tileLayer, MemoryLayer markerLayer, string outDir, string label, P2Backend? p2)
    {
        _p2 = p2;
        _mapControl = mapControl;
        _map = map;
        _tileLayer = tileLayer;
        _markerLayer = markerLayer;
        _outDir = outDir;
        _label = label;
        _queue = mapControl.DispatcherQueue;
        _renderer = new BenchMapRenderer(new MapRenderer());
        _mapControl.SetMapRenderer(_renderer);
    }

    public static void TryStart(MapControl mapControl, Mapsui.Map map)
    {
        var outDir = Environment.GetEnvironmentVariable("PGE_BENCH_OUT");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            return;
        }

        var tileLayer = map.Layers.OfType<TileLayer>().FirstOrDefault();
        var markerLayer = map.Layers.OfType<MemoryLayer>().FirstOrDefault(layer => layer.Name == "PhotoMarkers");
        if (tileLayer is null || markerLayer is null)
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        var label = Environment.GetEnvironmentVariable("PGE_BENCH_LABEL") ?? "run";
        // スパイク #293 の P2: PGE_P2_MVT があれば、OSM のラスタを無効にして、保存済み MVT の表示範囲画像（背景スレッド描画）を背景にする
        P2Backend? p2 = null;
        if (Environment.GetEnvironmentVariable("PGE_P2_MVT") is { Length: > 0 } mvt)
        {
            // ズームの段階表（Resolutions）は、有効なタイルレイヤーから決まる。OSM を無効にしても、同じ段階（z0〜20）で操作できるようにする
            map.Navigator.OverrideResolutions = Enumerable.Range(0, 21).Select(i => ViewExtentInjection.ResolutionAtZoom0 / Math.Pow(2, i)).ToArray();
            tileLayer.Enabled = false;
            p2 = new P2Backend(mapControl, map, mvt, Environment.GetEnvironmentVariable("PGE_P2_NOCACHE") != "1");
            p2.Start();
        }

        var runner = new MapBenchRunner(mapControl, map, tileLayer, markerLayer, outDir, label, p2);
        _ = runner.RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            await WaitUntilAsync(() => _map.Navigator.Viewport.HasSize() && _mapControl.ActualWidth > 0, 30_000).ConfigureAwait(true);
            if (_p2 is not null && Environment.GetEnvironmentVariable("PGE_P3_WARMUP") == "1")
            {
                // 一回限りのコストを、最初の操作より前に済ませる（本番の地点とは別のタイルで。デコード済みキャッシュを汚さない）
                var warm = Stopwatch.StartNew();
                await _p2.WarmUpAsync(43.1180, 141.3900).ConfigureAwait(true);
                _result["warmUpMs"] = Math.Round(warm.Elapsed.TotalMilliseconds, 1);
            }

            await Task.Delay(3000).ConfigureAwait(true);

            // 録画の開始などを待つ合図（スクリプトがファイルを置く）。無ければ待たない
            var flag = Path.Combine(_outDir, $"start-{_label}.flag");
            if (Environment.GetEnvironmentVariable("PGE_BENCH_WAIT_FLAG") == "1")
            {
                await WaitUntilAsync(() => File.Exists(flag), 120_000).ConfigureAwait(true);
                await Task.Delay(1000).ConfigureAwait(true);
            }

            var points = ReadLocations().Select(ResolveLocation).ToArray();
            RecordEnvironment();

            foreach (var (name, lat, lon) in points)
            {
                await RunLocationAsync(name, lat, lon).ConfigureAwait(true);
            }

            // 同じプロセス内での再訪（メモリキャッシュ）
            var first = points[0];
            await MeasureJumpAsync("revisit-" + first.Name, first.Lat, first.Lon, "jump-revisit").ConfigureAwait(true);

            if (_p2 is not null)
            {
                var st = _p2.Stats;
                _result["p2"] = new Dictionary<string, object?>
                {
                    ["submitted"] = st.Submitted,
                    ["replaced"] = st.Replaced,
                    ["rendered"] = st.Started,
                    ["delivered"] = st.Delivered,
                    ["applied"] = _p2.Applied,
                    ["discardedStale"] = st.DiscardedStale,
                    ["cancelled"] = st.Cancelled,
                    ["failed"] = st.Failed,
                    ["liveImages"] = RefCountedImage.LiveCount,
                    ["gpuBackedCanvas"] = PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.GpuBackedCanvas,
                    ["drawImageCount"] = PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.DrawTimesMs.Count,
                    ["drawImageMsP50"] = Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.DrawTimesMs, 0.5), 2),
                    ["drawImageMsP95"] = Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.DrawTimesMs, 0.95), 2),
                    ["firstUse"] = new[] { PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.FirstUseMs.Count, Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.FirstUseMs, 0.5), 2), Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.FirstUseMs, 0.95), 2) },
                    ["reuse"] = new[] { PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ReuseMs.Count, Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ReuseMs, 0.5), 2), Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ReuseMs, 0.95), 2) },
                    ["scaleOne"] = new[] { PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ScaleOneMs.Count, Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ScaleOneMs, 0.5), 2), Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ScaleOneMs, 0.95), 2) },
                    ["scaleOther"] = new[] { PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ScaleOtherMs.Count, Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ScaleOtherMs, 0.5), 2), Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.ScaleOtherMs, 0.95), 2) },
                    ["timeline"] = _p2.Timeline.Select(t => t.Select(x => Math.Round(x, 1)).ToArray()).ToArray(),
                    ["drawImageMsMax"] = Math.Round(Pct(PhotoMapStudio.Spike.ViewExtentImageStyleRenderer.DrawTimesMs, 1.0), 2),
                    ["cacheMB"] = _p2.Cache is { } c ? Math.Round(c.Bytes / 1048576.0, 1) : 0,
                    ["cacheTiles"] = _p2.Cache?.Count ?? 0,
                };
            }

            _result["phases"] = _phases;
            _result["finishedAt"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            _result["error"] = ex.ToString();
        }
        finally
        {
            var path = Path.Combine(_outDir, $"result-{_label}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_result, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(true);
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    private async Task RunLocationAsync(string name, double lat, double lon)
    {
        await MeasureJumpAsync(name, lat, lon, "jump-select").ConfigureAwait(true);
        await SaveStillAsync(name, 18).ConfigureAwait(true);

        foreach (var level in new[] { 15, 16, 17, 18, 19 })
        {
            await MeasureLevelAsync(name, level).ConfigureAwait(true);
        }

        await SaveStillAsync(name, 16).ConfigureAwait(true);

        await MeasureContinuousZoomAsync(name).ConfigureAwait(true);
        await MeasurePanAsync(name).ConfigureAwait(true);
    }

    private async Task MeasureJumpAsync(string name, double lat, double lon, string kind)
    {
        // 離れた場所から写真を選ぶ操作に合わせ、先に遠い場所（z10）へ移して落ち着かせる
        _map.Navigator.CenterOn(SphericalMercator.FromLonLat(new MPoint(lon + 0.5, lat + 0.5)), 0, Mapsui.Animations.Easing.CubicOut);
        _map.Navigator.ZoomToLevel(10);
        await WaitIdleAsync().ConfigureAwait(true);

        var metadata = new PhotoMetadata(DateTimeOffset.Now, null, null, lat, lon, true);
        var item = new PhotoItem(Path.Combine(_outDir, name + ".jpg"), 0, DateTimeOffset.Now, false);
        var before = Snapshot();
        var start = Stopwatch.GetTimestamp();
        _markers.SetMarker(_map, _markerLayer, lat, lon, metadata, item, DefaultZoomLevel);
        var settle = await WaitIdleAsync().ConfigureAwait(true);
        AddPhase($"{kind}:{name}", kind, start, settle, before, resolutionLevel: DefaultZoomLevel);
    }

    private async Task MeasureLevelAsync(string name, int level)
    {
        // 別のズームから目的のズームへ移る確定描画。直前に 2 段階離す
        _map.Navigator.ZoomToLevel(Math.Max(0, level - 2));
        await WaitIdleAsync().ConfigureAwait(true);
        var before = Snapshot();
        var start = Stopwatch.GetTimestamp();
        _map.Navigator.ZoomToLevel(level);
        var settle = await WaitIdleAsync().ConfigureAwait(true);
        AddPhase($"level:{name}:z{level}", "level-change", start, settle, before, resolutionLevel: level);
    }

    private async Task MeasureContinuousZoomAsync(string name)
    {
        var resolutions = _map.Navigator.Resolutions;
        var from = resolutions[15];
        var to = resolutions[19];
        _map.Navigator.ZoomToLevel(15);
        await WaitIdleAsync().ConfigureAwait(true);

        var before = Snapshot();
        var start = Stopwatch.GetTimestamp();
        var commands = new List<long>(ContinuousTicks);
        await RunTicksAsync(ContinuousTicks, tick =>
        {
            var t = tick / (double)(ContinuousTicks - 1);
            var resolution = from * Math.Pow(to / from, t);
            commands.Add(Stopwatch.GetTimestamp());
            _map.Navigator.ZoomTo(resolution, 0, Mapsui.Animations.Easing.Linear);
        }).ConfigureAwait(true);
        var inputEnd = Stopwatch.GetTimestamp();
        var settle = await WaitIdleAsync().ConfigureAwait(true);
        AddPhase($"continuous-zoom:{name}", "continuous-zoom", start, settle, before, inputEnd: inputEnd, commands: commands);
    }

    private async Task MeasurePanAsync(string name)
    {
        _map.Navigator.ZoomToLevel(17);
        await WaitIdleAsync().ConfigureAwait(true);

        var before = Snapshot();
        var start = Stopwatch.GetTimestamp();
        var commands = new List<long>(PanTicks);
        var viewport = _map.Navigator.Viewport;
        var x = viewport.CenterX;
        var y = viewport.CenterY;
        var step = PanPixelsPerTick * viewport.Resolution;
        await RunTicksAsync(PanTicks, tick =>
        {
            commands.Add(Stopwatch.GetTimestamp());
            _map.Navigator.CenterOn(x + (step * (tick + 1)), y, 0, Mapsui.Animations.Easing.Linear);
        }).ConfigureAwait(true);
        var inputEnd = Stopwatch.GetTimestamp();
        var settle = await WaitIdleAsync().ConfigureAwait(true);
        AddPhase($"pan:{name}", "pan", start, settle, before, inputEnd: inputEnd, commands: commands);
    }

    /// <summary>描画の同期（約 60Hz）ごとに 1 回ずつ操作する。タイマーでは分解能が粗く、約 30ms 間隔になるため使わない。</summary>
    private static async Task RunTicksAsync(int count, Action<int> action)
    {
        var tcs = new TaskCompletionSource();
        var tick = 0;
        void OnRendering(object? sender, object e)
        {
            if (tick >= count)
            {
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
                tcs.TrySetResult();
                return;
            }

            action(tick);
            tick++;
        }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
        await tcs.Task.ConfigureAwait(true);
    }

    /// <summary>タイル取得が止まり、その後の描画が 1 回完了するまで待つ。戻り値は (取得が止まった時刻, 描画が完了した時刻)。</summary>
    private async Task<(long IdleTicks, long SharpTicks)> WaitIdleAsync()
    {
        var deadline = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * IdleTimeoutMs / 1000);
        var requested = false;
        var idleTicks = 0L;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            await Task.Delay(5).ConfigureAwait(true);
            if (_p2 is not null ? _p2.Busy : _tileLayer.Busy)
            {
                requested = true;
                idleTicks = 0;
                continue;
            }

            if (idleTicks == 0)
            {
                idleTicks = Stopwatch.GetTimestamp();
                _map.Refresh();
            }

            var samples = _renderer.Snapshot();
            if (samples.Count > 0 && samples[^1].EndTicks >= idleTicks && (Stopwatch.GetTimestamp() - idleTicks) > Stopwatch.Frequency / 10)
            {
                _ = requested;
                return (idleTicks, samples[^1].EndTicks);
            }
        }

        throw new TimeoutException("tile layer did not become idle");
    }

    private (int RenderCount, long Ticks) Snapshot() => (_renderer.Count, Stopwatch.GetTimestamp());

    private void AddPhase(
        string id,
        string kind,
        long startTicks,
        (long IdleTicks, long SharpTicks) settle,
        (int RenderCount, long Ticks) before,
        int? resolutionLevel = null,
        long? inputEnd = null,
        List<long>? commands = null)
    {
        var all = _renderer.Snapshot();
        var inPhase = all.Skip(before.RenderCount).Where(sample => sample.StartTicks <= settle.SharpTicks).ToArray();
        var inputEndTicks = inputEnd ?? settle.SharpTicks;
        var driven = inPhase.Where(sample => sample.StartTicks <= inputEndTicks).ToArray();

        var intervals = new List<double>();
        for (var i = 1; i < driven.Length; i++)
        {
            intervals.Add(ToMs(driven[i].StartTicks - driven[i - 1].StartTicks));
        }

        var durations = driven.Select(sample => ToMs(sample.EndTicks - sample.StartTicks)).ToList();
        var phase = new Dictionary<string, object?>
        {
            ["startAbsMs"] = Math.Round(ToMs(startTicks), 1),
            ["sharpAbsMs"] = Math.Round(ToMs(settle.SharpTicks), 1),
            ["id"] = id,
            ["kind"] = kind,
            ["level"] = resolutionLevel,
            ["renderCount"] = inPhase.Length,
            ["timeToFirstRenderMs"] = inPhase.Length > 0 ? Round(ToMs(inPhase[0].EndTicks - startTicks)) : null,
            ["networkIdleMs"] = Round(ToMs(settle.IdleTicks - startTicks)),
            ["timeToSharpMs"] = Round(ToMs(settle.SharpTicks - startTicks)),
            ["settleAfterInputMs"] = inputEnd is long ie ? Round(ToMs(settle.SharpTicks - ie)) : null,
        };

        if (kind is "pan" or "continuous-zoom")
        {
            phase["frameIntervalMs"] = Stats(intervals);
            phase["drawDurationMs"] = Stats(durations);
            phase["inputIntervalMs"] = Stats(Intervals(commands ?? new List<long>()));
            phase["inputToRenderMs"] = Stats(LatenciesMs(commands ?? new List<long>(), driven));
            phase["longStallsOver50Ms"] = intervals.Count(value => value > 50);
        }
        else
        {
            phase["drawDurationMs"] = Stats(inPhase.Select(sample => ToMs(sample.EndTicks - sample.StartTicks)).ToList());
        }

        phase["workingSetMB"] = Round(Process.GetCurrentProcess().WorkingSet64 / 1048576.0);
        phase["gcHeapMB"] = Round(GC.GetTotalMemory(false) / 1048576.0);
        _phases.Add(phase);
    }

    private static List<double> Intervals(List<long> commands)
    {
        var result = new List<double>();
        for (var i = 1; i < commands.Count; i++)
        {
            result.Add(ToMs(commands[i] - commands[i - 1]));
        }

        return result;
    }

    private static List<double> LatenciesMs(List<long> commands, RenderSample[] renders)
    {
        var result = new List<double>(commands.Count);
        var index = 0;
        foreach (var command in commands)
        {
            while (index < renders.Length && renders[index].EndTicks < command)
            {
                index++;
            }

            if (index >= renders.Length)
            {
                break;
            }

            result.Add(ToMs(renders[index].EndTicks - command));
        }

        return result;
    }

    /// <summary>
    /// 800x600 の基準画像を保存する。実際の地図ペインは 800x600 より小さく、外側のタイルは取得されていないため、
    /// 描く範囲のタイルを先に取得し、取得が止まるのを待ってから描く（待たないと、タイルの継ぎ目に低解像度の代替が混ざる）。
    /// </summary>
    private async Task SaveStillAsync(string name, int level)
    {
        _map.Navigator.ZoomToLevel(level);
        await WaitIdleAsync().ConfigureAwait(true);
        var viewport = _map.Navigator.Viewport;
        var still = new Viewport(viewport.CenterX, viewport.CenterY, viewport.Resolution, 0, 800, 600);
        await _map.RefreshDataAsync(still).ConfigureAwait(true);
        await Task.Delay(300).ConfigureAwait(true);
        await WaitIdleAsync().ConfigureAwait(true);
        using var stream = new MapRenderer().RenderToBitmapStream(
            still,
            _map.Layers,
            _map.RenderService,
            _map.BackColor,
            1f,
            _map.Widgets,
            RenderFormat.Png,
            100);
        var path = Path.Combine(_outDir, $"still-{_label}-{name}-z{level}.png");
        await File.WriteAllBytesAsync(path, stream.ToArray()).ConfigureAwait(true);
    }

    private void RecordEnvironment()
    {
        var viewport = _map.Navigator.Viewport;
        _result["label"] = _label;
        _result["startedAt"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
        _result["os"] = RuntimeInformation.OSDescription;
        _result["arch"] = RuntimeInformation.ProcessArchitecture.ToString();
        _result["framework"] = RuntimeInformation.FrameworkDescription;
        _result["cpu"] = ReadCpuName();
        _result["logicalProcessors"] = Environment.ProcessorCount;
        _result["rasterizationScale"] = _mapControl.XamlRoot?.RasterizationScale;
        _result["controlSizeDip"] = new[] { Round(_mapControl.ActualWidth), Round(_mapControl.ActualHeight) };
        _result["viewportPx"] = new[] { Round(viewport.Width), Round(viewport.Height) };
        _result["resolutions"] = _map.Navigator.Resolutions.Take(21).Select(Round).ToArray();
        _result["commit"] = Environment.GetEnvironmentVariable("PGE_BENCH_COMMIT");
        _result["cacheDir"] = new MapPaneService().GetTileCacheRootDirectory();
        _result["skiaSharp"] = typeof(SkiaSharp.SKCanvas).Assembly.GetName().Version?.ToString();
        _result["inputCadence"] = "CompositionTarget.Rendering (1 command per vsync)";
        _result["pan"] = new { ticks = PanTicks, pixelsPerTick = PanPixelsPerTick, level = 17 };
        _result["continuousZoom"] = new { ticks = ContinuousTicks, from = 15, to = 19 };
    }

    private static (string Name, double Lat, double Lon)[] ReadLocations()
    {
        // 例: PGE_BENCH_LOCATIONS="a:43.1:141.3;b:43.2:141.4"（候補地の確認用。基準値の採取では使わない）
        var text = Environment.GetEnvironmentVariable("PGE_BENCH_LOCATIONS");
        if (string.IsNullOrWhiteSpace(text))
        {
            return Locations;
        }

        return text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(entry =>
        {
            var parts = entry.Split(':');
            return (parts[0], double.Parse(parts[1], CultureInfo.InvariantCulture), double.Parse(parts[2], CultureInfo.InvariantCulture));
        }).ToArray();
    }

    private static (string Name, double Lat, double Lon) ResolveLocation((string Name, double Lat, double Lon) location)
    {
        if (!double.IsNaN(location.Lat))
        {
            return location;
        }

        // z15 のタイル境界（z16〜19 の境界でもある）。大通付近の、隣り合うタイルの角
        const int zoom = 15;
        var n = Math.Pow(2, zoom);
        var latRad = 43.0606 * Math.PI / 180;
        var tileX = Math.Floor((141.3469 + 180) / 360 * n) + 1;
        var tileY = Math.Floor((1 - (Math.Asinh(Math.Tan(latRad)) / Math.PI)) / 2 * n) + 1;
        var lon = (tileX / n * 360) - 180;
        var lat = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * tileY / n)))) * 180 / Math.PI;
        return (location.Name, lat, lon);
    }

    private static string? ReadCpuName()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("ProcessorNameString") as string;
    }

    private async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(50).ConfigureAwait(true);
        }
    }

    private static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static double Pct(List<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(x => x).ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    private static double Round(double value) => Math.Round(value, 2);

    private static Dictionary<string, object?> Stats(List<double> values)
    {
        if (values.Count == 0)
        {
            return new Dictionary<string, object?> { ["n"] = 0 };
        }

        var sorted = values.OrderBy(value => value).ToArray();
        double Percentile(double p) => sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(p * (sorted.Length - 1) + 0.5))];
        return new Dictionary<string, object?>
        {
            ["n"] = sorted.Length,
            ["p50"] = Round(Percentile(0.5)),
            ["p95"] = Round(Percentile(0.95)),
            ["max"] = Round(sorted[^1]),
            ["mean"] = Round(sorted.Average()),
        };
    }
}
