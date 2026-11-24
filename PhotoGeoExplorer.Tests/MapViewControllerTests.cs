using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mapsui;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapViewControllerTests
{
    [Theory]
    [InlineData(43.768436, 142.362436, 16.0, 16)]
    [InlineData(43.768436, 142.362436, 17.5, 17)]
    [InlineData(35.681236, 139.767125, 18.6, 18)]
    [InlineData(-33.8688, 151.2093, 12.25, 12)]
    public async Task SetViewAsyncMovesMapToExactCoordinatesAndContinuousZoom(
        double latitude,
        double longitude,
        double zoom,
        int expectedStyleZoom)
    {
        using var map = new Map();
        map.Navigator.SetSize(800, 600);
        var controller = new MapViewController();
        var viewAppliedCount = 0;

        controller.Attach(
            map,
            MapTileSourceType.OpenFreeMapVector,
            MapTileSourceType.OpenFreeMapVector,
            "OpenFreeMap",
            pixelDensity: 1.5,
            isBusy: false,
            onViewApplied: () => viewAppliedCount++);

        var result = await controller.SetViewAsync(new MapViewTarget(latitude, longitude, zoom));

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal(1, viewAppliedCount);
        Assert.True(result.Status.HasValidViewport);
        Assert.NotNull(result.Status.Center);
        Assert.Equal(latitude, result.Status.Center.Latitude, 1e-6);
        Assert.Equal(longitude, result.Status.Center.Longitude, 1e-6);
        Assert.NotNull(result.Status.Zoom);
        Assert.Equal(zoom, result.Status.Zoom.Value, 1e-6);
        Assert.Equal(expectedStyleZoom, result.Status.StyleZoom);
        Assert.Equal(1.5, result.Status.PixelDensity);
        Assert.Equal(1200, result.Status.ViewportSize.PhysicalWidth);
        Assert.Equal(900, result.Status.ViewportSize.PhysicalHeight);
    }

    [Fact]
    public async Task SetViewAsyncBeforeViewportHasSizeDefersAndAppliesWhenSized()
    {
        using var map = new Map();
        var controller = new MapViewController();
        var observed = new List<MapViewStatus>();
        controller.StatusChanged += (_, status) => observed.Add(status);

        controller.Attach(
            map,
            MapTileSourceType.OpenStreetMap,
            MapTileSourceType.OpenStreetMap,
            "© OpenStreetMap contributors",
            pixelDensity: 1.0,
            isBusy: false);

        var target = new MapViewTarget(43.455153, 142.599269, 15.5);
        var beforeSizeResult = await controller.SetViewAsync(target);
        Assert.True(beforeSizeResult.Succeeded);
        Assert.False(beforeSizeResult.Status.HasValidViewport);

        map.Navigator.SetSize(640, 480);

        var current = controller.CurrentStatus;
        Assert.True(current.HasValidViewport);
        Assert.NotNull(current.Center);
        Assert.Equal(target.Latitude, current.Center.Latitude, 1e-6);
        Assert.Equal(target.Longitude, current.Center.Longitude, 1e-6);
        Assert.NotNull(current.Zoom);
        Assert.Equal(target.Zoom, current.Zoom.Value, 1e-6);
        Assert.Equal(15, current.StyleZoom);
        Assert.Contains(observed, status => status.HasValidViewport && status.StyleZoom == 15);
    }

    [Fact]
    public async Task SetViewAsyncFailsWhenDetachedOrInvalid()
    {
        var controller = new MapViewController();

        var detachedResult = await controller.SetViewAsync(new MapViewTarget(35.0, 139.0, 14.0));
        Assert.False(detachedResult.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(detachedResult.Error));

        using var map = new Map();
        map.Navigator.SetSize(400, 300);
        controller.Attach(map, MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, string.Empty, 1.0, isBusy: false);

        var outOfRange = await controller.SetViewAsync(new MapViewTarget(95.0, 139.0, 14.0));
        Assert.False(outOfRange.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(outOfRange.Error));

        controller.Detach();
        Assert.False(controller.CurrentStatus.HasValidViewport);
    }

    [Fact]
    public void UpdateBusyIncrementsRenderedSeqOnlyOnTrueToFalseTransition()
    {
        using var map = new Map();
        map.Navigator.SetSize(400, 300);
        var controller = new MapViewController();
        controller.Attach(map, MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector, "OpenFreeMap", 1.0, isBusy: false);

        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);

        controller.UpdateBusy(true);
        Assert.True(controller.CurrentStatus.IsBusy);
        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);

        controller.UpdateBusy(false);
        Assert.False(controller.CurrentStatus.IsBusy);
        Assert.Equal(1, controller.CurrentStatus.RenderedSeq);

        // 既に false の状態で再度 false になっても増えない
        controller.UpdateBusy(false);
        Assert.Equal(1, controller.CurrentStatus.RenderedSeq);

        controller.UpdateBusy(true);
        controller.UpdateTileSource(
            MapTileSourceType.MapboxSatellite,
            MapTileSourceType.OpenStreetMap,
            "© OpenStreetMap contributors",
            isBusy: false);
        Assert.Equal(2, controller.CurrentStatus.RenderedSeq);
        Assert.Equal("MapboxSatellite", controller.CurrentStatus.TileSource.Configured);
        Assert.Equal("OpenStreetMap", controller.CurrentStatus.TileSource.Displayed);
    }

    [Fact]
    public void UpdatePixelDensityUpdatesPhysicalViewportSizeWithoutChangingLogicalZoom()
    {
        using var map = new Map();
        map.Navigator.SetSize(500, 400);
        var controller = new MapViewController();
        controller.Attach(map, MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector, "OpenFreeMap", 1.0, isBusy: false);

        var resolution = WebMercator.ResolutionFromLogicalZoom(17.2);
        map.Navigator.CenterOnAndZoomTo(new MPoint(WebMercator.FromLongitude(142.36), WebMercator.FromLatitude(43.76)), resolution);

        Assert.Equal(500, controller.CurrentStatus.ViewportSize.PhysicalWidth);
        Assert.Equal(400, controller.CurrentStatus.ViewportSize.PhysicalHeight);

        controller.UpdatePixelDensity(2.0);

        Assert.Equal(2.0, controller.CurrentStatus.PixelDensity);
        Assert.Equal(1000, controller.CurrentStatus.ViewportSize.PhysicalWidth);
        Assert.Equal(800, controller.CurrentStatus.ViewportSize.PhysicalHeight);
        Assert.Equal(17.2, controller.CurrentStatus.Zoom!.Value, 1e-6);
        Assert.Equal(17, controller.CurrentStatus.StyleZoom);
    }
}
