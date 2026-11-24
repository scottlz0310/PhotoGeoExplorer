using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Styles;
using Mapsui.Widgets;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// <see cref="IMapRenderer"/> の描画完了を観測するための薄いデコレータ。
/// 全ビューポート描画の開始直前にコールバックを取得し、内側の描画が正常終了した直後のみ完了通知を呼び出す。
/// </summary>
internal sealed class ObservingMapRenderer : IMapRenderer
{
    private readonly IMapRenderer _inner;
    private readonly Func<Viewport, IReadOnlyList<ILayer>, Action?> _onRenderStarting;

    internal ObservingMapRenderer(
        IMapRenderer inner,
        Func<Viewport, IReadOnlyList<ILayer>, Action?> onRenderStarting)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onRenderStarting = onRenderStarting ?? throw new ArgumentNullException(nameof(onRenderStarting));
    }

    public void UpdateDrawables(Viewport viewport, ILayer layer, RenderService renderService)
        => _inner.UpdateDrawables(viewport, layer, renderService);

    public IDrawable? CreateDrawableForFeature(
        Viewport viewport,
        ILayer layer,
        IFeature feature,
        IStyle style,
        RenderService renderService)
        => _inner.CreateDrawableForFeature(viewport, layer, feature, style, renderService);

    public void Render(
        object target,
        Viewport viewport,
        IEnumerable<ILayer> layers,
        IEnumerable<IWidget> widgets,
        RenderService renderService,
        Color? background = null,
        MRect? dirtyRegion = null,
        CoordinateSpace coordinateSpace = CoordinateSpace.World)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var layerSnapshot = layers as IReadOnlyList<ILayer> ?? layers.ToList();
        var onCompleted = dirtyRegion is null ? _onRenderStarting(viewport, layerSnapshot) : null;
        _inner.Render(target, viewport, layerSnapshot, widgets, renderService, background, dirtyRegion, coordinateSpace);
        onCompleted?.Invoke();
    }

    public MemoryStream RenderToBitmapStream(
        Viewport viewport,
        IEnumerable<ILayer> layers,
        RenderService renderService,
        Color? background = null,
        float pixelDensity = 1,
        IEnumerable<IWidget>? widgets = null,
        RenderFormat renderFormat = RenderFormat.Png,
        int quality = 100)
        => _inner.RenderToBitmapStream(viewport, layers, renderService, background, pixelDensity, widgets, renderFormat, quality);

    public bool TryGetWidgetRenderer(Type widgetType, out IWidgetRenderer? widgetRenderer)
        => _inner.TryGetWidgetRenderer(widgetType, out widgetRenderer);

    public bool TryGetStyleRenderer(Type styleType, out IStyleRenderer? styleRenderer)
        => _inner.TryGetStyleRenderer(styleType, out styleRenderer);

    public MapInfo GetMapInfo(
        ScreenPosition screenPosition,
        Viewport viewport,
        IEnumerable<ILayer> layers,
        RenderService renderService,
        int margin = 0)
        => _inner.GetMapInfo(screenPosition, viewport, layers, renderService, margin);
}
