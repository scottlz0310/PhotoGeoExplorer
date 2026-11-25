using System;
using System.Collections.Generic;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図上の写真マーカー（ピン画像の解決・単一/複数配置）を担う。地図ライブラリの型は知らない。
/// </summary>
internal sealed class MapMarkerPresenter
{
    private readonly IMapPaneService _service;

    public MapMarkerPresenter(IMapPaneService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public static void ClearMarkers(IMapSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        surface.ClearMarkers();
    }

    public void SetMarker(
        IMapSurface surface,
        double latitude,
        double longitude,
        PhotoMetadata metadata,
        PhotoItem photoItem,
        int defaultZoomLevel)
    {
        ArgumentNullException.ThrowIfNull(surface);

        surface.ShowMarker(CreateMarker(latitude, longitude, metadata, photoItem), defaultZoomLevel);
    }

    public void SetMarkers(
        IMapSurface surface,
        IReadOnlyList<(double Latitude, double Longitude, PhotoMetadata Metadata, PhotoItem Item)> items)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(items);

        var markers = new List<MapMarker>(items.Count);
        foreach (var item in items)
        {
            markers.Add(CreateMarker(item.Latitude, item.Longitude, item.Metadata, item.Item));
        }

        surface.ShowMarkers(markers);
    }

    private MapMarker CreateMarker(double latitude, double longitude, PhotoMetadata metadata, PhotoItem photoItem)
        => new(latitude, longitude, photoItem, metadata, ResolvePinImagePath(metadata));

    // ピン画像が見つからなければ null にして、表示面の既定の印で描かせる
    private string? ResolvePinImagePath(PhotoMetadata metadata)
    {
        var pinPath = _service.GetPinImagePath(metadata);
        if (string.IsNullOrWhiteSpace(pinPath))
        {
            return null;
        }

        if (!_service.FileExistsAtPath(pinPath))
        {
            AppLog.Info($"Pin image missing: {pinPath}");
            return null;
        }

        return pinPath;
    }
}
