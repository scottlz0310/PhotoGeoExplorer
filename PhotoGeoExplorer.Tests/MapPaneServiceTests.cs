using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services;
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
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);
        const string userAgent = "PhotoGeoExplorer/Test";

        try
        {
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
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
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
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);
        const string userAgent = "PhotoGeoExplorer/Test";

        try
        {
            // Act
            var layer = service.CreateTileLayer(MapTileSourceType.OpenStreetMap, userAgent);

            // Assert
            Assert.NotNull(layer);
            Assert.Equal("OpenStreetMap", layer.Name);

            // Cleanup
            layer.Dispose();
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void CreateTileLayerReturnsEsriWorldImageryLayer()
    {
        // Arrange
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);
        const string userAgent = "PhotoGeoExplorer/Test";

        try
        {
            // Act
            var layer = service.CreateTileLayer(MapTileSourceType.EsriWorldImagery, userAgent);

            // Assert
            Assert.NotNull(layer);
            Assert.Equal("Esri WorldImagery", layer.Name);

            // Cleanup
            layer.Dispose();
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void CreateTileLayerFallsBackWhenCacheRootIsAFile()
    {
        // Arrange
        var cacheRoot = CreateTempCacheRoot();
        var cacheRootFile = Path.Combine(cacheRoot, "cache-root-file.txt");
        File.WriteAllText(cacheRootFile, "cache root file");
        var service = new MapPaneService(cacheRootFile);
        const string userAgent = "PhotoGeoExplorer/Test";

        try
        {
            // Act
            var layer = service.CreateTileLayer(MapTileSourceType.OpenStreetMap, userAgent);

            // Assert
            Assert.NotNull(layer);
            Assert.Equal("OpenStreetMap", layer.Name);

            // Cleanup
            layer.Dispose();
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
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

    // =========================================================
    // GetPinImagePath
    // =========================================================

    [Fact]
    public void GetPinImagePath_TakenAt10DaysAgo_ReturnsGreenPin()
    {
        var service = new MapPaneService();
        var metadata = new PhotoMetadata(DateTimeOffset.Now.AddDays(-10), null, null, 35.0, 135.0);

        var path = service.GetPinImagePath(metadata);

        Assert.EndsWith("green_pin.png", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPinImagePath_TakenAt100DaysAgo_ReturnsBluePin()
    {
        var service = new MapPaneService();
        var metadata = new PhotoMetadata(DateTimeOffset.Now.AddDays(-100), null, null, 35.0, 135.0);

        var path = service.GetPinImagePath(metadata);

        Assert.EndsWith("blue_pin.png", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPinImagePath_TakenAt400DaysAgo_ReturnsRedPin()
    {
        var service = new MapPaneService();
        var metadata = new PhotoMetadata(DateTimeOffset.Now.AddDays(-400), null, null, 35.0, 135.0);

        var path = service.GetPinImagePath(metadata);

        Assert.EndsWith("red_pin.png", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPinImagePath_TakenAtNull_ReturnsRedPin()
    {
        var service = new MapPaneService();
        var metadata = new PhotoMetadata(null, null, null, 35.0, 135.0);

        var path = service.GetPinImagePath(metadata);

        Assert.EndsWith("red_pin.png", path, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================
    // ICrashReportService DI / ライフサイクル
    // =========================================================

    [Fact]
    public void Constructor_WithCrashReportService_DoesNotThrow()
    {
        var cacheRoot = CreateTempCacheRoot();
        try
        {
            var fakeCrashReporter = new FakeCrashReportService();
            var ex = Record.Exception(() => new MapPaneService(cacheRoot, fakeCrashReporter));
            Assert.Null(ex);
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void Constructor_WithNullCrashReportService_ThrowsArgumentNullException()
    {
        var cacheRoot = CreateTempCacheRoot();
        try
        {
            Assert.Throws<ArgumentNullException>(() => new MapPaneService(cacheRoot, null!));
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public async Task LoadPhotoMetadataAsync_EmptyItems_DoesNotCallWriteCrashLog()
    {
        var cacheRoot = CreateTempCacheRoot();
        try
        {
            var fakeCrashReporter = new FakeCrashReportService();
            var service = new MapPaneService(cacheRoot, fakeCrashReporter);
            var items = Array.Empty<PhotoGeoExplorer.ViewModels.PhotoListItem>();

            await service.LoadPhotoMetadataAsync(items, CancellationToken.None).ConfigureAwait(true);

            Assert.Equal(0, fakeCrashReporter.WriteCallCount);
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    private static string CreateTempCacheRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempCacheRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FakeCrashReportService : ICrashReportService
    {
        public bool PreviouslyTerminatedAbnormally => false;
        public string CrashReportsDirectoryPath => string.Empty;
        public int WriteCallCount { get; private set; }

        public void RecordStartup() { }
        public void RecordNormalExit() { }
        public void WriteCrashLog(Exception? exception) => WriteCallCount++;
        public string? GetLatestCrashLogContent() => null;
    }
}
