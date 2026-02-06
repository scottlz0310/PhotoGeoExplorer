using System;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapPaneSelectionHelperTests
{
    [Fact]
    public void SelectPhotosInRectangleReturnsEmptyWhenFeaturesIsNull()
    {
        var bounds = new MRect(0, 0, 10, 10);

        var result = MapPaneSelectionHelper.SelectPhotosInRectangle(null, bounds, "PhotoItem");

        Assert.Empty(result);
    }

    [Fact]
    public void SelectPhotosInRectangleThrowsWhenPhotoItemKeyIsEmpty()
    {
        var bounds = new MRect(0, 0, 10, 10);

        Assert.Throws<ArgumentException>(() =>
            MapPaneSelectionHelper.SelectPhotosInRectangle(Array.Empty<IFeature>(), bounds, string.Empty));
    }

    [Fact]
    public void SelectPhotosInRectangleReturnsUniquePhotoItemsInBounds()
    {
        var bounds = new MRect(0, 0, 20, 20);
        var first = CreatePhotoItem(@"C:\Photos\a.jpg");
        var duplicateWithDifferentCase = CreatePhotoItem(@"C:\Photos\A.JPG");
        var second = CreatePhotoItem(@"C:\Photos\b.jpg");
        var outOfBounds = CreatePhotoItem(@"C:\Photos\c.jpg");

        var features = new IFeature[]
        {
            CreatePointFeature(10, 10, first),
            CreatePointFeature(15, 15, duplicateWithDifferentCase),
            CreatePointFeature(20, 20, second),
            CreatePointFeature(100, 100, outOfBounds),
            CreatePointFeatureWithoutPhotoItem(12, 12)
        };

        var result = MapPaneSelectionHelper.SelectPhotosInRectangle(features, bounds, "PhotoItem");

        Assert.Collection(
            result,
            item => Assert.Equal(first.FilePath, item.FilePath),
            item => Assert.Equal(second.FilePath, item.FilePath));
    }

    [Fact]
    public void SelectPhotosInRectangleIncludesBoundaryPoints()
    {
        var bounds = new MRect(0, 0, 20, 20);
        var minPointItem = CreatePhotoItem(@"C:\Photos\min.jpg");
        var maxPointItem = CreatePhotoItem(@"C:\Photos\max.jpg");
        var features = new IFeature[]
        {
            CreatePointFeature(0, 0, minPointItem),
            CreatePointFeature(20, 20, maxPointItem)
        };

        var result = MapPaneSelectionHelper.SelectPhotosInRectangle(features, bounds, "PhotoItem");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void IsPointerMovementWithinThresholdReturnsTrueWhenWithinLimit()
    {
        var start = new Windows.Foundation.Point(100, 100);
        var current = new Windows.Foundation.Point(106, 94);

        var result = MapPaneSelectionHelper.IsPointerMovementWithinThreshold(start, current);

        Assert.True(result);
    }

    [Fact]
    public void IsPointerMovementWithinThresholdReturnsFalseWhenExceedingLimit()
    {
        var start = new Windows.Foundation.Point(100, 100);
        var current = new Windows.Foundation.Point(106.1, 100);

        var result = MapPaneSelectionHelper.IsPointerMovementWithinThreshold(start, current);

        Assert.False(result);
    }

    [Fact]
    public void IsPointerMovementWithinThresholdThrowsWhenThresholdIsNegative()
    {
        var start = new Windows.Foundation.Point(0, 0);
        var current = new Windows.Foundation.Point(0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MapPaneSelectionHelper.IsPointerMovementWithinThreshold(start, current, -1));
    }

    private static PhotoItem CreatePhotoItem(string filePath)
    {
        return new PhotoItem(
            filePath,
            sizeBytes: 1024,
            modifiedAt: DateTimeOffset.Now,
            isFolder: false,
            thumbnailPath: null,
            pixelWidth: 4000,
            pixelHeight: 3000);
    }

    private static PointFeature CreatePointFeature(double x, double y, PhotoItem photoItem)
    {
        var feature = new PointFeature(new MPoint(x, y));
        feature["PhotoItem"] = photoItem;
        return feature;
    }

    private static PointFeature CreatePointFeatureWithoutPhotoItem(double x, double y)
    {
        return new PointFeature(new MPoint(x, y));
    }
}
