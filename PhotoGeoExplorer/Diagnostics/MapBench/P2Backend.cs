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
    private readonly object _gate = new();
    private MemoryLayer? _layer;
    private RefCountedImage? _image;
    private long _generation;
    private long _shownGeneration;
    private int _pendingApply;
    private bool _disposed;
    private long _applied;
    private readonly List<(long Tick, long Generation)> _appliedTicks = new();

    public P2Backend(MapControl control, Mapsui.Map map, string mvtRoot, bool useCache)
    {
        _control = control;
        _map = map;
        _queue = control.DispatcherQueue;
        _sourceId = "saved-mvt:" + mvtRoot;
        _cache = useCache ? new DecodedTileCache(64L * 1024 * 1024) : null;
        var renderer = new ViewExtentVectorRenderer(new SavedMvtTileSource(mvtRoot), new OpenMapTilesStyle(), _cache);
        _scheduler = new ViewRenderScheduler((request, token) => renderer.RenderAsync(request, token), OnRendered);
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

    public void Start()
    {
        ViewExtentImageStyleRenderer.Register();
        _map.Navigator.ViewportChanged += OnViewportChanged;
        Submit();
    }

    public void Dispose()
    {
        _disposed = true;
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

        _scheduler.Submit(new RenderRequest(vp.CenterX, vp.CenterY, vp.Resolution, vp.Width, vp.Height, density, _sourceId, 1, generation));
    }

    private void OnRendered(RenderResult result)
    {
        lock (_gate)
        {
            _pendingApply++;
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
                _appliedTicks.Add((System.Diagnostics.Stopwatch.GetTimestamp(), result.Request.Generation));
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
