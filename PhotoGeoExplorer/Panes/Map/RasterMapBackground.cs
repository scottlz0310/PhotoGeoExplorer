using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// ラスタタイル 1 枚のレイヤーを背景として扱う（OpenStreetMap・Esri・Mapbox）。
/// </summary>
internal sealed class RasterMapBackground : IMapBackground
{
    private readonly TileLayer _layer;
    private int _wasBusy;
    private bool _disposed;

    internal RasterMapBackground(TileLayer layer, MapTileSourceType sourceType)
    {
        ArgumentNullException.ThrowIfNull(layer);

        _layer = layer;
        _wasBusy = layer.Busy ? 1 : 0;
        Layers = [layer];
        AttributionText = string.Join(' ', MapAttribution.GetLinks(sourceType).Select(link => link.Text));
        _layer.PropertyChanged += OnLayerPropertyChanged;
    }

    public IReadOnlyList<ILayer> Layers { get; }

    public string AttributionText { get; }

    public bool IsBusy => _layer.Busy;

    public event EventHandler? BusyChanged;

    public event EventHandler? FrameRendered;

    // タイルは Mapsui の TileLayer が視点に応じて取得する。背景側で視点や密度を扱う必要はない
    public void Attach(Mapsui.Map map, Func<double> getPixelDensity)
    {
    }

    public void OnPixelDensityChanged()
    {
    }

    public Task<IMapExportLayers> CreateExportLayersAsync(
        Viewport viewport,
        float pixelDensity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MapExportLayers.Borrowed(Layers));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _layer.PropertyChanged -= OnLayerPropertyChanged;
        _layer.Dispose();
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TileLayer.Busy))
        {
            return;
        }

        var isBusy = _layer.Busy;
        var previousBusy = Interlocked.Exchange(ref _wasBusy, isBusy ? 1 : 0) == 1;
        if (previousBusy && !isBusy)
        {
            FrameRendered?.Invoke(this, EventArgs.Empty);
        }

        BusyChanged?.Invoke(this, EventArgs.Empty);
    }
}
