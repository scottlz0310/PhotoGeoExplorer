using System;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapPaneSelectionHelperTests
{
    private static readonly GeoPoint LowerLeft = new(Latitude: 0, Longitude: 0);
    private static readonly GeoPoint UpperRight = new(Latitude: 20, Longitude: 20);

    [Fact]
    public void SelectPhotosInRectangleReturnsEmptyWhenMarkersIsNull()
    {
        var result = MapPaneSelectionHelper.SelectPhotosInRectangle(null, LowerLeft, UpperRight);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectPhotosInRectangleReturnsUniquePhotoItemsInBounds()
    {
        var first = CreatePhotoItem(@"C:\Photos\a.jpg");
        var duplicateWithDifferentCase = CreatePhotoItem(@"C:\Photos\A.JPG");
        var second = CreatePhotoItem(@"C:\Photos\b.jpg");
        var outOfBounds = CreatePhotoItem(@"C:\Photos\c.jpg");

        var markers = new[]
        {
            CreateMarker(10, 10, first),
            CreateMarker(15, 15, duplicateWithDifferentCase),
            CreateMarker(20, 20, second),
            CreateMarker(80, 80, outOfBounds)
        };

        var result = MapPaneSelectionHelper.SelectPhotosInRectangle(markers, LowerLeft, UpperRight);

        Assert.Collection(
            result,
            item => Assert.Equal(first.FilePath, item.FilePath),
            item => Assert.Equal(second.FilePath, item.FilePath));
    }

    [Fact]
    public void SelectPhotosInRectangleIncludesBoundaryPoints()
    {
        var markers = new[]
        {
            CreateMarker(0, 0, CreatePhotoItem(@"C:\Photos\min.jpg")),
            CreateMarker(20, 20, CreatePhotoItem(@"C:\Photos\max.jpg"))
        };

        var result = MapPaneSelectionHelper.SelectPhotosInRectangle(markers, LowerLeft, UpperRight);

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(0, 0, 20, 20)]
    [InlineData(20, 20, 0, 0)]
    [InlineData(0, 20, 20, 0)]
    [InlineData(20, 0, 0, 20)]
    public void SelectPhotosInRectangleAcceptsCornersInAnyOrder(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        var markers = new[] { CreateMarker(10, 10, CreatePhotoItem(@"C:\Photos\a.jpg")) };

        var result = MapPaneSelectionHelper.SelectPhotosInRectangle(
            markers,
            new GeoPoint(latitude1, longitude1),
            new GeoPoint(latitude2, longitude2));

        Assert.Single(result);
    }

    [Theory]
    [InlineData(10, 25)]
    [InlineData(25, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void SelectPhotosInRectangleExcludesMarkersOutsideOnEitherAxis(double latitude, double longitude)
    {
        var markers = new[] { CreateMarker(latitude, longitude, CreatePhotoItem(@"C:\Photos\a.jpg")) };

        var result = MapPaneSelectionHelper.SelectPhotosInRectangle(markers, LowerLeft, UpperRight);

        Assert.Empty(result);
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

    private static MapMarker CreateMarker(double latitude, double longitude, PhotoItem photoItem)
    {
        var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, latitude, longitude);
        return new MapMarker(latitude, longitude, photoItem, metadata, PinImagePath: null);
    }
}
