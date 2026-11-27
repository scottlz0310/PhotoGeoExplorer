using System;
using System.Collections.Generic;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// 航空写真の背景の上へ、道路だけを描いた透過の背景を重ねた 1 つの背景。2 つの背景は、この背景が所有する。
/// </summary>
/// <remarks>
/// 描画完了は、今の視点の航空写真と道路の両方が画面へ反映されたときに通知する。通知は任意のスレッドから呼ばれる。
/// </remarks>
internal sealed class CompositeMapBackground : IMapsuiBackground
{
    private readonly Lock _gate = new();
    private Mapsui.Map? _map;
    private bool _imageryShown;
    private bool _overlayShown;
    private bool _disposed;

    internal CompositeMapBackground(IMapsuiBackground imagery, IMapsuiBackground overlay, MapTileSourceType sourceType)
    {
        ArgumentNullException.ThrowIfNull(imagery);
        ArgumentNullException.ThrowIfNull(overlay);

        Imagery = imagery;
        Overlay = overlay;
        SourceType = sourceType;
        Layers = [.. imagery.Layers, .. overlay.Layers];
        AttributionText = string.Join(' ', MapBackgroundCatalog.Get(sourceType).AttributionLinks.Select(link => link.Text));
        imagery.BusyChanged += OnChildBusyChanged;
        overlay.BusyChanged += OnChildBusyChanged;
        imagery.FrameRendered += OnImageryFrameRendered;
        overlay.FrameRendered += OnOverlayFrameRendered;
    }

    /// <summary>下に敷く航空写真</summary>
    internal IMapsuiBackground Imagery { get; }

    /// <summary>上に重ねる道路</summary>
    internal IMapsuiBackground Overlay { get; }

    public IReadOnlyList<ILayer> Layers { get; }

    public MapTileSourceType SourceType { get; }

    public string AttributionText { get; }

    public bool IsBusy => Imagery.IsBusy || Overlay.IsBusy;

    public event EventHandler? BusyChanged;

    public event EventHandler? FrameRendered;

    public void Attach(Mapsui.Map map, Func<double> getPixelDensity)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(getPixelDensity);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_map is not null)
        {
            throw new InvalidOperationException("The composite map background is already attached.");
        }

        _map = map;

        // 子より先に購読し、視点が変わったら、前の視点の描画完了を忘れる
        map.Navigator.ViewportChanged += OnViewportChanged;
        Imagery.Attach(map, getPixelDensity);
        Overlay.Attach(map, getPixelDensity);
    }

    public void OnPixelDensityChanged()
    {
        ForgetShownFrames();
        Imagery.OnPixelDensityChanged();
        Overlay.OnPixelDensityChanged();
    }

    public Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService)
    {
        var imagery = Imagery.BeginMapRender(viewport, layers, renderService);
        var overlay = Overlay.BeginMapRender(viewport, layers, renderService);
        return imagery is null && overlay is null
            ? null
            : () =>
            {
                imagery?.Invoke();
                overlay?.Invoke();
            };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_map is { } map)
        {
            map.Navigator.ViewportChanged -= OnViewportChanged;
        }

        Imagery.BusyChanged -= OnChildBusyChanged;
        Overlay.BusyChanged -= OnChildBusyChanged;
        Imagery.FrameRendered -= OnImageryFrameRendered;
        Overlay.FrameRendered -= OnOverlayFrameRendered;
        Overlay.Dispose();
        Imagery.Dispose();
    }

    private void OnViewportChanged(object? sender, EventArgs e) => ForgetShownFrames();

    private void ForgetShownFrames()
    {
        lock (_gate)
        {
            _imageryShown = false;
            _overlayShown = false;
        }
    }

    private void OnChildBusyChanged(object? sender, EventArgs e) => BusyChanged?.Invoke(this, EventArgs.Empty);

    private void OnImageryFrameRendered(object? sender, EventArgs e) => OnChildFrameRendered(imagery: true);

    private void OnOverlayFrameRendered(object? sender, EventArgs e) => OnChildFrameRendered(imagery: false);

    private void OnChildFrameRendered(bool imagery)
    {
        lock (_gate)
        {
            _imageryShown |= imagery;
            _overlayShown |= !imagery;
            if (!_imageryShown || !_overlayShown)
            {
                return;
            }
        }

        FrameRendered?.Invoke(this, EventArgs.Empty);
    }
}
