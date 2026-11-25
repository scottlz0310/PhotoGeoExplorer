using System;
using System.Collections.Generic;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// <see cref="IMapSurface"/> の Mapsui 実装。Mapsui の <see cref="Navigator"/>・<see cref="MemoryLayer"/> との変換をここに閉じ込める。
/// </summary>
/// <remarks>
/// 購読先（<see cref="Navigator"/>）は <see cref="Mapsui.Map"/> の一部で、この表面と同じ寿命なので、解除は要らない。
/// 写真マーカー用のレイヤーは、この表面が地図の最前面（背景の上）へ追加し、破棄するときに外す。
/// </remarks>
internal sealed class MapsuiMapSurface : IMapSurface
{
    internal const string MarkerLayerName = "PhotoMarkers";
    internal const string MarkerKey = "MapMarker";

    private const string SelectionLayerName = "RectangleSelection";

    private static readonly Color SelectionFillColor = Color.FromArgb(64, 0, 120, 215);
    private static readonly Color SelectionOutlineColor = Color.FromArgb(255, 0, 120, 215);

    private readonly Mapsui.Map _map;
    private readonly MemoryLayer _markerLayer;
    private MemoryLayer? _selectionLayer;
    private IReadOnlyList<MapMarker> _markers = [];
    private bool _panLockBeforeLock;
    private bool _panLockActive;

    internal MapsuiMapSurface(Mapsui.Map map)
    {
        ArgumentNullException.ThrowIfNull(map);

        _map = map;
        _markerLayer = new MemoryLayer
        {
            Name = MarkerLayerName,
            Features = Array.Empty<IFeature>(),
            Style = null
        };
        map.Layers.Add(_markerLayer);
        map.Navigator.ViewportChanged += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public MapViewState View
    {
        get
        {
            var viewport = _map.Navigator.Viewport;
            return new MapViewState(
                viewport.HasSize(),
                viewport.CenterX,
                viewport.CenterY,
                viewport.Resolution,
                viewport.Width,
                viewport.Height);
        }
    }

    public event EventHandler? ViewChanged;

    public IReadOnlyList<MapMarker> Markers => _markers;

    public void SetView(double centerX, double centerY, double resolution)
    {
        _map.Navigator.CenterOnAndZoomTo(new MPoint(centerX, centerY), resolution, duration: 0);
        _map.Refresh();
    }

    public void ShowMarker(MapMarker marker, int zoomLevel)
    {
        ArgumentNullException.ThrowIfNull(marker);

        _markers = [marker];
        _markerLayer.Features = new[] { CreateFeature(marker) };
        _map.Refresh();

        var navigator = _map.Navigator;
        navigator.CenterOn(ToWorld(marker), 0, Mapsui.Animations.Easing.CubicOut);
        if (navigator.Resolutions.Count > 0)
        {
            var targetLevel = Math.Clamp(zoomLevel, 0, navigator.Resolutions.Count - 1);
            navigator.ZoomToLevel(targetLevel);
        }
    }

    public void ShowMarkers(IReadOnlyList<MapMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        _markers = markers;
        var features = new List<IFeature>(markers.Count);
        var hasBounds = false;
        var minX = 0d;
        var minY = 0d;
        var maxX = 0d;
        var maxY = 0d;

        foreach (var marker in markers)
        {
            var position = ToWorld(marker);
            if (!hasBounds)
            {
                minX = maxX = position.X;
                minY = maxY = position.Y;
                hasBounds = true;
            }
            else
            {
                minX = Math.Min(minX, position.X);
                maxX = Math.Max(maxX, position.X);
                minY = Math.Min(minY, position.Y);
                maxY = Math.Max(maxY, position.Y);
            }

            features.Add(CreateFeature(marker));
        }

        _markerLayer.Features = features;
        _map.Refresh();

        if (!hasBounds)
        {
            return;
        }

        var spanX = maxX - minX;
        var spanY = maxY - minY;
        var padding = Math.Max(spanX, spanY) * 0.1;
        if (padding <= 0)
        {
            padding = 500;
        }

        var bounds = new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
        _map.Navigator.ZoomToBox(bounds, MBoxFit.Fit, 0, Mapsui.Animations.Easing.CubicOut);
    }

    public void ClearMarkers()
    {
        _markers = [];
        _markerLayer.Features = Array.Empty<IFeature>();
        _map.Refresh();
    }

    public bool TryGetGeoPosition(double screenX, double screenY, out GeoPoint position)
    {
        var world = _map.Navigator.Viewport.ScreenToWorld(screenX, screenY);
        var lonLat = SphericalMercator.ToLonLat(world);
        position = new GeoPoint(lonLat.Y, lonLat.X);
        return double.IsFinite(position.Latitude) && double.IsFinite(position.Longitude);
    }

    public void LockPan()
    {
        var navigator = _map.Navigator;
        if (!_panLockActive)
        {
            _panLockBeforeLock = navigator.PanLock;
            _panLockActive = true;
        }

        navigator.PanLock = true;
    }

    public void RestorePan()
    {
        if (!_panLockActive)
        {
            return;
        }

        _map.Navigator.PanLock = _panLockBeforeLock;
        _panLockActive = false;
    }

    public void ShowSelectionRectangle(GeoPoint corner1, GeoPoint corner2)
    {
        var start = SphericalMercator.FromLonLat(new MPoint(corner1.Longitude, corner1.Latitude));
        var end = SphericalMercator.FromLonLat(new MPoint(corner2.Longitude, corner2.Latitude));
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);

        var polygon = new Polygon(new LinearRing(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY)
        }));

        var feature = new GeometryFeature
        {
            Geometry = polygon
        };
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush(SelectionFillColor),
            Outline = new Pen(SelectionOutlineColor, 2)
        });

        if (_selectionLayer is null)
        {
            _selectionLayer = new MemoryLayer
            {
                Name = SelectionLayerName,
                Features = new[] { feature },
                Style = null
            };
            _map.Layers.Add(_selectionLayer);
        }
        else
        {
            _selectionLayer.Features = new[] { feature };
        }

        _map.Refresh();
    }

    public void ClearSelectionRectangle()
    {
        if (_selectionLayer is null)
        {
            return;
        }

        _map.Layers.Remove(_selectionLayer);
        _selectionLayer.Dispose();
        _selectionLayer = null;
        _map.Refresh();
    }

    public void Dispose()
    {
        ClearSelectionRectangle();
        _markerLayer.Dispose();
    }

    private static MPoint ToWorld(MapMarker marker)
        => SphericalMercator.FromLonLat(new MPoint(marker.Longitude, marker.Latitude));

    private static PointFeature CreateFeature(MapMarker marker)
    {
        var feature = new PointFeature(ToWorld(marker));
        feature.Styles.Clear();
        feature.Styles.Add(MapsuiMarkerStyle.Create(marker.PinImagePath));
        feature[MarkerKey] = marker;
        return feature;
    }
}
