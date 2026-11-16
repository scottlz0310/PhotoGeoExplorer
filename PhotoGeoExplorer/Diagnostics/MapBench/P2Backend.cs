#pragma warning disable CA2213, CA1031, CA1869, CA1822, CA1305, CA1848, CA2007, CA1859, CA1860, CA1861, CA1304, CA1311, CA1307, CA1308, CA2000, CA1812, CA5392, CA1401, CA2201
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.UI;
using Mapsui.UI.WinUI;
using Microsoft.UI.Dispatching;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Spike;

namespace PhotoGeoExplorer.Diagnostics.MapBench;

/// <summary>
/// スパイク #293 の P2: 実アプリの MapControl の背景を、保存済み MVT の表示範囲画像にする。
/// 視点が変わるたびに描画要求を出し（<see cref="ViewRenderScheduler"/> が背景スレッドで集約して描く）、結果を UI スレッドで差し替える。
/// 操作中は、直前の画像が Mapsui の変形で描かれる（注入方式 A）。PGE_P2_MVT があるときだけ、MapBenchRunner が使う。
/// </summary>
internal sealed class P2Backend : IDisposable
{
    private readonly MapControl _control;
    private readonly Mapsui.Map _map;
    private readonly DispatcherQueue _queue;
    private readonly string _sourceId;
    private readonly DecodedTileCache? _cache;
    private readonly ViewRenderScheduler _scheduler;
    private readonly ViewExtentVectorRenderer _renderer;
    private readonly int _prefetchMs;
    private System.Threading.Timer? _prefetchTimer;
    private RenderRequest? _lastRequest;
    private (int Z, int X, int Y) _lastPrefetched = (-1, -1, -1);
    private readonly string _mvtSourceId;
    private readonly object _gate = new();
    private MemoryLayer? _layer;
    private RefCountedImage? _image;
    private long _generation;
    private long _shownGeneration;
    private int _pendingApply;
    private bool _disposed;
    private long _applied;
    private readonly List<(long Tick, long Generation)> _appliedTicks = new();
    private readonly Dictionary<long, long> _submitTicks = new();
    private readonly Dictionary<long, (long Tick, double RenderMs)> _deliveredTicks = new();
    private readonly Dictionary<long, RenderTimings> _timings = new();

    /// <summary>計測用: 要求ごとの時系列（絶対 ms、Stopwatch）。[世代, 要求, 描画開始（推定）, 描画終了（反映の引き渡し）, UI での反映, 描画時間 ms]。</summary>
    public List<double[]> Timeline { get; } = new();

    public P2Backend(MapControl control, Mapsui.Map map, string mvtRoot, bool useCache)
    {
        _control = control;
        _map = map;
        _queue = control.DispatcherQueue;
        _sourceId = "saved-mvt:" + mvtRoot;
        _cache = useCache ? new DecodedTileCache((int.TryParse(Environment.GetEnvironmentVariable("PGE_P3_CACHE_MB"), out var cacheMb) ? cacheMb : 64) * 1024L * 1024) : null;
        // #293 の P3a: PGE_P3_FAST=1 で、確定描画の高速化（範囲外を描かない・破線の切り詰め・帯の並列・注記の並列）を有効にする。帯の数は論理コア数の半分（最大 8。PGE_P3_BANDS で上書き）
        IVectorMapStyle style = new OpenMapTilesStyle();
        var render = new ViewRenderOptions();
        if (Environment.GetEnvironmentVariable("PGE_P3_FAST") == "1")
        {
            style = new FastVectorStyle(new FastStyleOptions { Cull = true, ClipVertices = true });
            var bands = int.TryParse(Environment.GetEnvironmentVariable("PGE_P3_BANDS"), out var b) ? b : Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
            render = new ViewRenderOptions { Bands = bands, ParallelLabels = true, ParallelDecode = true };
        }

        var renderer = new ViewExtentVectorRenderer(new SavedMvtTileSource(mvtRoot), style, _cache, render);
        // 操作が止まったとき、古い視点の実行中の描画を中断して、最新の描画を先に始める（#339。PGE_P3_IDLE_MS で時間を指定。既定は無効）
        var idleMs = int.TryParse(Environment.GetEnvironmentVariable("PGE_P3_IDLE_MS"), out var im) ? im : 0;
        _scheduler = new ViewRenderScheduler((request, token) => renderer.RenderAsync(request, token), OnRendered)
        {
            IdleCancelAfter = TimeSpan.FromMilliseconds(idleMs),
        };
        _renderer = renderer;
        _mvtSourceId = _sourceId;
        _prefetchMs = Environment.GetEnvironmentVariable("PGE_P3_PREFETCH") == "1" ? (int.TryParse(Environment.GetEnvironmentVariable("PGE_P3_PREFETCH_DELAY_MS"), out var pd) ? pd : 120) : 0;
    }

    /// <summary>視点に対して、画像が追いついていない（または描画中）。</summary>
    public bool Busy
    {
        get
        {
            lock (_gate)
            {
                return _scheduler.IsBusy || _pendingApply > 0 || _shownGeneration < _generation;
            }
        }
    }

    public ViewRenderStats Stats => _scheduler.Stats;

    public DecodedTileCache? Cache => _cache;

    public long Applied
    {
        get
        {
            lock (_gate)
            {
                return _applied;
            }
        }
    }

    private void OnPrefetchIdle(object? state)
    {
        var request = _lastRequest;
        if (_disposed || request is null || _scheduler.IsBusy)
        {
            return;
        }

        var center = Mapsui.Projections.SphericalMercator.ToLonLat(new MPoint(request.CenterX, request.CenterY));
        var zoom = request.Zoom;
        var tileZoom = Math.Min((int)Math.Floor(zoom), 14);
        var n = Math.Pow(2, tileZoom);
        var key = (tileZoom, (int)Math.Floor((center.X + 180) / 360 * n), (int)Math.Floor((1 - (Math.Asinh(Math.Tan(center.Y * Math.PI / 180)) / Math.PI)) / 2 * n));
        if (key == _lastPrefetched)
        {
            return;
        }

        _lastPrefetched = key;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _renderer.PrefetchAsync(center.Y, center.X, zoom).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 事前デコードは最適化。失敗しても、描画の中でデコードされる
            }
        });
    }

    /// <summary>
    /// 起動時のウォームアップ（PGE_P3_WARMUP=1）: 一回限りのコスト（JIT・フォント・スレッドプール・ネイティブの初期化）を、最初の操作より前に済ませる。
    /// 計測では、本番の地点とは別のタイルで描くため、デコード済みキャッシュには本番のタイルを入れない。
    /// </summary>
    public async System.Threading.Tasks.Task WarmUpAsync(double lat, double lon)
    {
        var density = ((IMapControl)_control).GetPixelDensity() ?? 1.0;
        var center = Mapsui.Projections.SphericalMercator.FromLonLat(new MPoint(lon, lat));
        foreach (var zoom in new[] { 16.0, 18.0 })
        {
            var request = new RenderRequest(center.X, center.Y, ViewExtentInjection.ResolutionFromZoom(zoom, density), 256, 256, density, _mvtSourceId, 1, 0);
            using var output = await _renderer.RenderAsync(request).ConfigureAwait(false) is { } o ? new Releaser(o.Image) : null;
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly RefCountedImage _image;

        public Releaser(RefCountedImage image) => _image = image;

        public void Dispose() => _image.Release();
    }

    public void Start()
    {
        ViewExtentImageStyleRenderer.Register();
        _map.Navigator.ViewportChanged += OnViewportChanged;
        Submit();
    }

    public void Dispose()
    {
        _disposed = true;
        _prefetchTimer?.Dispose();
        _map.Navigator.ViewportChanged -= OnViewportChanged;
        _scheduler.Dispose();
    }

    private void OnViewportChanged(object? sender, EventArgs e) => Submit();

    private void Submit()
    {
        var vp = _map.Navigator.Viewport;
        if (_disposed || vp.Width <= 0 || vp.Height <= 0)
        {
            return;
        }

        var density = ((IMapControl)_control).GetPixelDensity() ?? 1.0;
        long generation;
        lock (_gate)
        {
            generation = ++_generation;
        }

        lock (_gate)
        {
            _submitTicks[generation] = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        var request = new RenderRequest(vp.CenterX, vp.CenterY, vp.Resolution, vp.Width, vp.Height, density, _sourceId, 1, generation);
        _scheduler.Submit(request);
        if (_prefetchMs > 0)
        {
            // 操作が止まって _prefetchMs 経ったら、中心付近のタイルを先にデコードする
            _lastRequest = request;
            (_prefetchTimer ??= new System.Threading.Timer(OnPrefetchIdle)).Change(_prefetchMs, System.Threading.Timeout.Infinite);
        }
    }

    private void OnRendered(RenderResult result)
    {
        var deliveredTick = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _pendingApply++;
            _deliveredTicks[result.Request.Generation] = (deliveredTick, result.Duration.TotalMilliseconds);
            _timings[result.Request.Generation] = result.Timings;
        }

        if (!_queue.TryEnqueue(() => Apply(result)))
        {
            result.Image.Release();
            lock (_gate)
            {
                _pendingApply--;
            }
        }
    }

    // UI スレッド。新しい画像で、レイヤーを置き換える。置き換えたあとで、直前の画像の参照を手放す
    private void Apply(RenderResult result)
    {
        var previous = _image;
        var previousLayer = _layer;
        try
        {
            if (_disposed || result.Request.Generation <= _shownGeneration)
            {
                result.Image.Release();
                return;
            }

            var layer = ViewExtentInjection.CreateLayerA(result.Image.Image, result.Request.Extent);
            _map.Layers.Insert(0, layer);
            if (previousLayer is not null)
            {
                _map.Layers.Remove(previousLayer);
            }

            _layer = layer;
            _image = result.Image;
            previous?.Release();
            lock (_gate)
            {
                _shownGeneration = result.Request.Generation;
                _applied++;
                var appliedTick = System.Diagnostics.Stopwatch.GetTimestamp();
                _appliedTicks.Add((appliedTick, result.Request.Generation));
                if (_submitTicks.TryGetValue(result.Request.Generation, out var submitted) && _deliveredTicks.TryGetValue(result.Request.Generation, out var delivered))
                {
                    static double Ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    Timeline.Add(new[] { result.Request.Generation, Ms(submitted), Ms(delivered.Tick) - delivered.RenderMs, Ms(delivered.Tick), Ms(appliedTick), delivered.RenderMs, _timings[result.Request.Generation].Decode.TotalMilliseconds, _timings[result.Request.Generation].Fetch.TotalMilliseconds, _timings[result.Request.Generation].Tiles, _timings[result.Request.Generation].CacheHits });
                }
            }

            _map.Refresh();
        }
        finally
        {
            lock (_gate)
            {
                _pendingApply--;
            }
        }
    }
}
