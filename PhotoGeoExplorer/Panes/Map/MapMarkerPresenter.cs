using System;
using System.Collections.Generic;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図上の写真マーカー（ピンスタイル生成・単一/複数配置・地図フィット）を担う
/// </summary>
internal sealed class MapMarkerPresenter
{
    private const string PhotoMetadataKey = "PhotoMetadata";
    private const string PhotoItemKey = "PhotoItem";

    private readonly IMapPaneService _service;

    public MapMarkerPresenter(IMapPaneService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public static void ClearMarkers(Mapsui.Map map, MemoryLayer markerLayer)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(markerLayer);

        markerLayer.Features = Array.Empty<IFeature>();
        map.Refresh();
    }

    public void SetMarker(
        Mapsui.Map map,
        MemoryLayer markerLayer,
        double latitude,
        double longitude,
        PhotoMetadata metadata,
        PhotoItem photoItem,
        int defaultZoomLevel)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(markerLayer);

        var position = SphericalMercator.FromLonLat(new MPoint(longitude, latitude));
        var feature = new PointFeature(position);
        feature.Styles.Clear();
        foreach (var style in CreatePinStyles(metadata))
        {
            feature.Styles.Add(style);
        }
        feature[PhotoMetadataKey] = metadata;
        feature[PhotoItemKey] = photoItem;
        markerLayer.Features = new[] { feature };
        map.Refresh();

        var navigator = map.Navigator;
        navigator.CenterOn(position, 0, Mapsui.Animations.Easing.CubicOut);
        if (navigator.Resolutions.Count > 0)
        {
            var targetLevel = Math.Clamp(defaultZoomLevel, 0, navigator.Resolutions.Count - 1);
            navigator.ZoomToLevel(targetLevel);
        }
    }

    public void SetMarkers(
        Mapsui.Map map,
        MemoryLayer markerLayer,
        IReadOnlyList<(double Latitude, double Longitude, PhotoMetadata Metadata, PhotoItem Item)> items)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(markerLayer);
        ArgumentNullException.ThrowIfNull(items);

        var features = new List<IFeature>(items.Count);
        var hasBounds = false;
        var minX = 0d;
        var minY = 0d;
        var maxX = 0d;
        var maxY = 0d;

        foreach (var item in items)
        {
            var position = SphericalMercator.FromLonLat(new MPoint(item.Longitude, item.Latitude));
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

            var feature = new PointFeature(position);
            feature.Styles.Clear();
            foreach (var style in CreatePinStyles(item.Metadata))
            {
                feature.Styles.Add(style);
            }
            feature[PhotoMetadataKey] = item.Metadata;
            feature[PhotoItemKey] = item.Item;
            features.Add(feature);
        }

        markerLayer.Features = features;
        map.Refresh();

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
        map.Navigator.ZoomToBox(bounds, MBoxFit.Fit, 0, Mapsui.Animations.Easing.CubicOut);
    }

    private IStyle[] CreatePinStyles(PhotoMetadata metadata)
    {
        var pinPath = _service.GetPinImagePath(metadata);
        if (TryCreatePinStyle(pinPath, out var pinStyle))
        {
            return new IStyle[] { pinStyle };
        }

        return new IStyle[] { CreateFallbackMarkerStyle() };
    }

    private bool TryCreatePinStyle(string imagePath, out ImageStyle pinStyle)
    {
        pinStyle = null!;
        if (string.IsNullOrWhiteSpace(imagePath) || !_service.FileExistsAtPath(imagePath))
        {
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                AppLog.Info($"Pin image missing: {imagePath}");
            }
            return false;
        }

        var imageUri = new Uri(imagePath).AbsoluteUri;
        pinStyle = new ImageStyle
        {
            Image = new Mapsui.Styles.Image { Source = imageUri },
            SymbolScale = 1,
            RelativeOffset = new RelativeOffset(0, 0.5)
        };
        return true;
    }

    private static SymbolStyle CreateFallbackMarkerStyle()
    {
        return new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.8,
            Fill = new Brush(Color.FromArgb(255, 32, 128, 255)),
            Outline = new Pen(Color.White, 2)
        };
    }
}
