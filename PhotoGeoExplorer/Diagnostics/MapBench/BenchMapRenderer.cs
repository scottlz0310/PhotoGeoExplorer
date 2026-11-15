#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Styles;
using Mapsui.Widgets;

namespace PhotoGeoExplorer.Diagnostics.MapBench;

internal readonly record struct RenderSample(long StartTicks, long EndTicks, double Resolution, double CenterX, double CenterY);

/// <summary>
/// 既存の描画を変えずに、1 回の描画の開始・終了時刻を記録する（計測専用。スパイク #293 の P0）。
/// </summary>
internal sealed class BenchMapRenderer : IMapRenderer
{
    private readonly IMapRenderer _inner;
    private readonly object _gate = new();
    private readonly List<RenderSample> _samples = new();

    public BenchMapRenderer(IMapRenderer inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IReadOnlyList<RenderSample> Snapshot()
    {
        lock (_gate)
        {
            return _samples.ToArray();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count;
            }
        }
    }

    public void Render(
        object target,
        Viewport viewport,
        IEnumerable<ILayer> layers,
        IEnumerable<IWidget> widgets,
        RenderService renderService,
        Color? background,
        MRect extent,
        CoordinateSpace coordinateSpace)
    {
        var start = Stopwatch.GetTimestamp();
        _inner.Render(target, viewport, layers, widgets, renderService, background, extent, coordinateSpace);
        var end = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _samples.Add(new RenderSample(start, end, viewport.Resolution, viewport.CenterX, viewport.CenterY));
        }
    }

    public void UpdateDrawables(Viewport viewport, ILayer layer, RenderService renderService)
        => _inner.UpdateDrawables(viewport, layer, renderService);

    public IDrawable CreateDrawableForFeature(Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService)
        => _inner.CreateDrawableForFeature(viewport, layer, feature, style, renderService);

    public MemoryStream RenderToBitmapStream(
        Viewport viewport,
        IEnumerable<ILayer> layers,
        RenderService renderService,
        Color? background,
        float pixelDensity,
        IEnumerable<IWidget> widgets,
        RenderFormat renderFormat,
        int quality)
        => _inner.RenderToBitmapStream(viewport, layers, renderService, background, pixelDensity, widgets, renderFormat, quality);

    public bool TryGetWidgetRenderer(Type widgetType, out IWidgetRenderer widgetRenderer)
        => _inner.TryGetWidgetRenderer(widgetType, out widgetRenderer);

    public bool TryGetStyleRenderer(Type styleType, out IStyleRenderer styleRenderer)
        => _inner.TryGetStyleRenderer(styleType, out styleRenderer);

    public MapInfo GetMapInfo(ScreenPosition screenPosition, Viewport viewport, IEnumerable<ILayer> layers, RenderService renderService, int margin)
        => _inner.GetMapInfo(screenPosition, viewport, layers, renderService, margin);
}
