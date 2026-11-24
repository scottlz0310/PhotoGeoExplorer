using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using Mapsui;
using Mapsui.Extensions;
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
    private Mapsui.Map? _map;
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

    public void Attach(Mapsui.Map map, Func<double> getPixelDensity)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(getPixelDensity);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Volatile.Write(ref _map, map);
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
        Volatile.Write(ref _map, null);
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
        if (previousBusy && !isBusy && HasRenderedViewportTiles())
        {
            FrameRendered?.Invoke(this, EventArgs.Empty);
        }

        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    // Mapsui の TileFetchPlanner は最後のタイル取得が例外で失敗した場合も Busy を false に戻す。
    // 空の地図や低ズームのフォールバックタイルを完了扱いにしないよう、対象ビューポートの解像度で
    // 必要な全タイルの RasterFeature がレイヤーに反映されていることを検証する。
    private bool HasRenderedViewportTiles()
    {
        var map = Volatile.Read(ref _map);
        if (map is null)
        {
            return false;
        }

        var viewport = map.Navigator.Viewport;
        if (!viewport.HasSize() || !double.IsFinite(viewport.Resolution) || viewport.Resolution <= 0)
        {
            return false;
        }

        var schema = _layer.TileSource?.Schema;
        if (schema is null || schema.Resolutions.Count == 0)
        {
            return false;
        }

        var viewportExtent = viewport.ToExtent();
        if (viewportExtent is null)
        {
            return false;
        }

        var bruTileExtent = new Extent(viewportExtent.MinX, viewportExtent.MinY, viewportExtent.MaxX, viewportExtent.MaxY);
        var clipped = bruTileExtent.Intersect(schema.Extent);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return false;
        }

        var levelId = BruTile.Utilities.GetNearestLevel(schema.Resolutions, viewport.Resolution);
        var requiredTiles = schema.GetTileInfos(clipped, levelId).ToList();
        if (requiredTiles.Count == 0)
        {
            return false;
        }

        var rasterExtents = _layer
            .GetFeatures(viewportExtent, viewport.Resolution)
            .OfType<RasterFeature>()
            .Select(feature => feature.Extent)
            .Where(extent => extent is not null)
            .ToList();

        if (rasterExtents.Count < requiredTiles.Count)
        {
            return false;
        }

        foreach (var tile in requiredTiles)
        {
            if (!rasterExtents.Any(extent => MatchesTileExtent(extent!, tile.Extent)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesTileExtent(MRect actual, Extent expected)
    {
        var toleranceX = Math.Max(1e-6, expected.Width * 1e-6);
        var toleranceY = Math.Max(1e-6, expected.Height * 1e-6);
        return Math.Abs(actual.MinX - expected.MinX) <= toleranceX
            && Math.Abs(actual.MinY - expected.MinY) <= toleranceY
            && Math.Abs(actual.MaxX - expected.MaxX) <= toleranceX
            && Math.Abs(actual.MaxY - expected.MaxY) <= toleranceY;
    }
}
