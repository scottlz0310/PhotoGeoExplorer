using System;
using System.Collections.Generic;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.MapRendering;
using Windows.Foundation;

namespace PhotoGeoExplorer.Panes.Map;

internal static class MapPaneSelectionHelper
{
    /// <summary>
    /// 対角の 2 点で囲んだ矩形（境界を含む）の中にあるマーカーの写真を、重複なく返す。
    /// 緯度・経度のまま比べても、Web メルカトルで比べても結果は同じ（どちらも単調な変換のため）。
    /// </summary>
    public static IReadOnlyList<PhotoItem> SelectPhotosInRectangle(
        IEnumerable<MapMarker>? markers,
        GeoPoint corner1,
        GeoPoint corner2)
    {
        if (markers is null)
        {
            return Array.Empty<PhotoItem>();
        }

        var minLatitude = Math.Min(corner1.Latitude, corner2.Latitude);
        var maxLatitude = Math.Max(corner1.Latitude, corner2.Latitude);
        var minLongitude = Math.Min(corner1.Longitude, corner2.Longitude);
        var maxLongitude = Math.Max(corner1.Longitude, corner2.Longitude);

        var selectedPhotos = new List<PhotoItem>();
        var seenFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var marker in markers)
        {
            if (marker.Longitude < minLongitude || marker.Longitude > maxLongitude
                || marker.Latitude < minLatitude || marker.Latitude > maxLatitude)
            {
                continue;
            }

            if (seenFilePaths.Add(marker.Item.FilePath))
            {
                selectedPhotos.Add(marker.Item);
            }
        }

        return selectedPhotos;
    }

    public static bool IsPointerMovementWithinThreshold(Point startPoint, Point currentPoint, double threshold = 6)
    {
        if (threshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be greater than or equal to zero.");
        }

        var deltaX = currentPoint.X - startPoint.X;
        var deltaY = currentPoint.Y - startPoint.Y;
        return Math.Abs(deltaX) <= threshold && Math.Abs(deltaY) <= threshold;
    }
}
