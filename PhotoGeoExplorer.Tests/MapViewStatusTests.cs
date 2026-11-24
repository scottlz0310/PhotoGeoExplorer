using System.Text.Json;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapViewStatusTests
{
    [Theory]
    [InlineData(43.768436, 142.362436, 17.5, 17)]
    [InlineData(35.681236, 139.767125, 16.0, 16)]
    [InlineData(-33.8688, 151.2093, 14.99, 14)]
    [InlineData(0.0, 0.0, 0.0, 0)]
    public void CreateComputesCenterZoomStyleZoomAndPhysicalViewportSize(
        double latitude,
        double longitude,
        double logicalZoom,
        int expectedStyleZoom)
    {
        var centerX = WebMercator.FromLongitude(longitude);
        var centerY = WebMercator.FromLatitude(latitude);
        var resolution = WebMercator.ResolutionFromLogicalZoom(logicalZoom);

        var status = MapViewStatus.Create(
            hasSize: true,
            centerX: centerX,
            centerY: centerY,
            resolution: resolution,
            widthDip: 683.0,
            heightDip: 610.0,
            pixelDensity: 1.5,
            configuredTileSource: MapTileSourceType.OpenFreeMapVector,
            displayedTileSource: MapTileSourceType.OpenFreeMapVector,
            attributionText: "OpenFreeMap © OpenMapTiles © OpenStreetMap contributors",
            isBusy: false,
            renderedSeq: 4);

        Assert.True(status.HasValidViewport);
        Assert.NotNull(status.Center);
        Assert.Equal(latitude, status.Center.Latitude, 1e-6);
        Assert.Equal(longitude, status.Center.Longitude, 1e-6);
        Assert.NotNull(status.Zoom);
        Assert.Equal(logicalZoom, status.Zoom.Value, 1e-6);
        Assert.Equal(expectedStyleZoom, status.StyleZoom);
        Assert.Equal(1.5, status.PixelDensity);
        Assert.Equal(683.0, status.ViewportSize.LogicalWidth);
        Assert.Equal(610.0, status.ViewportSize.LogicalHeight);
        Assert.Equal(1024, status.ViewportSize.PhysicalWidth);
        Assert.Equal(915, status.ViewportSize.PhysicalHeight);
        Assert.Equal("OpenFreeMapVector", status.TileSource.Configured);
        Assert.Equal("OpenFreeMapVector", status.TileSource.Displayed);
        Assert.Contains("OpenFreeMap", status.TileSource.Attribution, StringComparison.Ordinal);
        Assert.False(status.IsBusy);
        Assert.Equal(4, status.RenderedSeq);
    }

    [Theory]
    [InlineData(false, 100.0, 100.0, 10.0)]
    [InlineData(true, 0.0, 100.0, 10.0)]
    [InlineData(true, 100.0, 0.0, 10.0)]
    [InlineData(true, 100.0, 100.0, 0.0)]
    [InlineData(true, 100.0, 100.0, double.NaN)]
    [InlineData(true, 100.0, 100.0, double.PositiveInfinity)]
    public void CreateReturnsInvalidViewportStatusWhenInputsAreNotRenderable(
        bool hasSize,
        double widthDip,
        double heightDip,
        double resolution)
    {
        var status = MapViewStatus.Create(
            hasSize: hasSize,
            centerX: 0,
            centerY: 0,
            resolution: resolution,
            widthDip: widthDip,
            heightDip: heightDip,
            pixelDensity: 1.25,
            configuredTileSource: MapTileSourceType.MapboxSatellite,
            displayedTileSource: MapTileSourceType.OpenStreetMap,
            attributionText: "© OpenStreetMap contributors",
            isBusy: true,
            renderedSeq: 2);

        Assert.False(status.HasValidViewport);
        Assert.Null(status.Center);
        Assert.Null(status.Zoom);
        Assert.Null(status.StyleZoom);
        Assert.Equal(1.25, status.PixelDensity);
        Assert.Equal("MapboxSatellite", status.TileSource.Configured);
        Assert.Equal("OpenStreetMap", status.TileSource.Displayed);
        Assert.True(status.IsBusy);
        Assert.Equal(2, status.RenderedSeq);
    }

    [Fact]
    public void ToJsonEmitsSingleLineCamelCasePayloadWithAllContractFields()
    {
        var resolution = WebMercator.ResolutionFromLogicalZoom(17.5);
        var status = MapViewStatus.Create(
            hasSize: true,
            centerX: WebMercator.FromLongitude(142.362436),
            centerY: WebMercator.FromLatitude(43.768436),
            resolution: resolution,
            widthDip: 800,
            heightDip: 600,
            pixelDensity: 1.5,
            configuredTileSource: MapTileSourceType.OpenFreeMapVector,
            displayedTileSource: MapTileSourceType.OpenFreeMapVector,
            attributionText: "OpenFreeMap",
            isBusy: false,
            renderedSeq: 7);

        var json = status.ToJson();

        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain('\r', json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("hasValidViewport").GetBoolean());
        Assert.Equal(43.768436, root.GetProperty("center").GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(142.362436, root.GetProperty("center").GetProperty("longitude").GetDouble(), 6);
        Assert.Equal(17.5, root.GetProperty("zoom").GetDouble(), 6);
        Assert.Equal(17, root.GetProperty("styleZoom").GetInt32());
        Assert.Equal(1.5, root.GetProperty("pixelDensity").GetDouble(), 6);
        Assert.Equal(1200, root.GetProperty("viewportSize").GetProperty("physicalWidth").GetInt32());
        Assert.Equal(900, root.GetProperty("viewportSize").GetProperty("physicalHeight").GetInt32());
        Assert.Equal("OpenFreeMapVector", root.GetProperty("tileSource").GetProperty("displayed").GetString());
        Assert.False(root.GetProperty("isBusy").GetBoolean());
        Assert.Equal(7, root.GetProperty("renderedSeq").GetInt64());
    }
}
