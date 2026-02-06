using System;
using System.Collections.Generic;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using PhotoGeoExplorer.Models;
using Windows.Foundation;

namespace PhotoGeoExplorer.Panes.Map;

internal static class MapPaneSelectionHelper
{
    public static IReadOnlyList<PhotoItem> SelectPhotosInRectangle(
        IEnumerable<IFeature>? features,
        MRect selectionBounds,
        string photoItemKey)
    {
        if (string.IsNullOrWhiteSpace(photoItemKey))
        {
            throw new ArgumentException("Photo item key must not be empty.", nameof(photoItemKey));
        }

        if (features is null)
        {
            return Array.Empty<PhotoItem>();
        }

        var selectedPhotos = new List<PhotoItem>();
        var seenFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in features)
        {
            if (feature is not PointFeature pointFeature)
            {
                continue;
            }

            var point = pointFeature.Point;
            if (point is null)
            {
                continue;
            }

            if (point.X < selectionBounds.Min.X || point.X > selectionBounds.Max.X
                || point.Y < selectionBounds.Min.Y || point.Y > selectionBounds.Max.Y)
            {
                continue;
            }

            if (feature[photoItemKey] is not PhotoItem photoItem)
            {
                continue;
            }

            if (seenFilePaths.Add(photoItem.FilePath))
            {
                selectedPhotos.Add(photoItem);
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
