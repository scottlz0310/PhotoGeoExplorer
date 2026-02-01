using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// MapPaneService のテスト
/// </summary>
public class MapPaneServiceTests
{
    [Fact]
    public void InitializeMapReturnsMapWithLayers()
    {
        // Arrange
        var service = new MapPaneService();
        const string userAgent = "PhotoGeoExplorer/Test";

        // Act
        var (map, tileLayer, markerLayer) = service.InitializeMap(MapTileSourceType.OpenStreetMap, userAgent);

        // Assert
        Assert.NotNull(map);
        Assert.NotNull(tileLayer);
        Assert.NotNull(markerLayer);
        Assert.Equal(2, map.Layers.Count); // タイルレイヤー + マーカーレイヤー
        Assert.Equal("PhotoMarkers", markerLayer.Name);

        // Cleanup
        map.Dispose();
    }

    [Fact]
    public void CreateTileLayerThrowsWhenUserAgentIsNull()
    {
        // Arrange
        var service = new MapPaneService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.CreateTileLayer(MapTileSourceType.OpenStreetMap, null!));
    }

    [Fact]
    public void CreateTileLayerReturnsOpenStreetMapLayer()
    {
        // Arrange
        var service = new MapPaneService();
        const string userAgent = "PhotoGeoExplorer/Test";

        // Act
        var layer = service.CreateTileLayer(MapTileSourceType.OpenStreetMap, userAgent);

        // Assert
        Assert.NotNull(layer);
        Assert.Equal("OpenStreetMap", layer.Name);

        // Cleanup
        layer.Dispose();
    }

    [Fact]
    public void CreateTileLayerReturnsEsriWorldImageryLayer()
    {
        // Arrange
        var service = new MapPaneService();
        const string userAgent = "PhotoGeoExplorer/Test";

        // Act
        var layer = service.CreateTileLayer(MapTileSourceType.EsriWorldImagery, userAgent);

        // Assert
        Assert.NotNull(layer);
        Assert.Equal("Esri WorldImagery", layer.Name);

        // Cleanup
        layer.Dispose();
    }

    [Fact]
    public void GetTileCacheRootDirectoryReturnsValidPath()
    {
        // Arrange
        var service = new MapPaneService();

        // Act
        var path = service.GetTileCacheRootDirectory();

        // Assert
        Assert.NotNull(path);
        Assert.Contains("PhotoGeoExplorer", path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cache", path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tiles", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadPhotoMetadataAsyncThrowsWhenItemsIsNull()
    {
        // Arrange
        var service = new MapPaneService();
        using var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.LoadPhotoMetadataAsync(null!, cts.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task LoadPhotoMetadataAsyncReturnsEmptyListForEmptyInput()
    {
        // Arrange
        var service = new MapPaneService();
        var items = Array.Empty<PhotoGeoExplorer.ViewModels.PhotoListItem>();
        using var cts = new CancellationTokenSource();

        // Act
        var result = await service.LoadPhotoMetadataAsync(items, cts.Token).ConfigureAwait(true);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
