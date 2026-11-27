using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
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
            var (background, surface) = service.InitializeMap(new MapBackgroundRequest(MapTileSourceType.OpenStreetMap, userAgent));

            // Assert
            var map = Assert.IsType<MapsuiMapSurface>(surface).Map;
            Assert.NotNull(background);
            Assert.NotNull(surface);
            Assert.Equal(1, map.Layers.Count); // マーカーレイヤーだけ（背景のレイヤーは表示面の ReplaceBackground が載せる）
            Assert.Equal(MapsuiMapSurface.MarkerLayerName, map.Layers.Last().Name);

            // Cleanup
            surface.Dispose();
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void CreateBackgroundThrowsWhenUserAgentIsNull()
    {
        // Arrange
        var service = new MapPaneService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.CreateBackground(new MapBackgroundRequest(MapTileSourceType.OpenStreetMap, null!)));
    }

    [Fact]
    public void CreateBackgroundReturnsOpenStreetMapLayer()
    {
        // Arrange
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);
        const string userAgent = "PhotoGeoExplorer/Test";

        try
        {
            // Act
            using var background = service.CreateBackground(new MapBackgroundRequest(MapTileSourceType.OpenStreetMap, userAgent));
            var layer = Assert.IsType<TileLayer>(Assert.Single(((IMapsuiBackground)background).Layers));

            // Assert
            Assert.Equal("OpenStreetMap", layer.Name);
            Assert.Equal("© OpenStreetMap contributors", layer.TileSource.Attribution.Text);
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void CreateBackgroundReturnsEsriWorldImageryLayer()
    {
        // Arrange
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);
        const string userAgent = "PhotoGeoExplorer/Test";

        try
        {
            // Act
            using var background = service.CreateBackground(new MapBackgroundRequest(MapTileSourceType.EsriWorldImagery, userAgent));
            var layer = Assert.IsType<TileLayer>(Assert.Single(((IMapsuiBackground)background).Layers));

            // Assert
            Assert.Equal("Esri WorldImagery", layer.Name);
            Assert.Equal(
                "Source: Esri, Vantor, Earthstar Geographics, and the GIS User Community",
                layer.TileSource.Attribution.Text);
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void CreateBackgroundReturnsTheVectorBackgroundWithItsAttribution()
    {
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);

        try
        {
            using var background = service.CreateBackground(
                new MapBackgroundRequest(MapTileSourceType.OpenFreeMapVector, "PhotoGeoExplorer/Test", TryPostToUi: _ => true));

            Assert.IsType<VectorMapBackground>(background);
            Assert.Single(((IMapsuiBackground)background).Layers);
            Assert.Equal("OpenFreeMap © OpenMapTiles Data from OpenStreetMap", background.AttributionText);
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Theory]
    [InlineData(MapTileSourceType.GsiStandard, MapBackgroundKind.Raster)]
    [InlineData(MapTileSourceType.GsiPale, MapBackgroundKind.Raster)]
    [InlineData(MapTileSourceType.GsiPhoto, MapBackgroundKind.Raster)]
    [InlineData(MapTileSourceType.GsiVector, MapBackgroundKind.Vector)]
    internal void CreateGsiBackgroundUsesTheChosenTypeAndAttribution(MapTileSourceType sourceType, MapBackgroundKind kind)
    {
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);
        try
        {
            using var background = service.CreateBackground(
                new MapBackgroundRequest(sourceType, "PhotoGeoExplorer/Test", TryPostToUi: _ => true));

            Assert.Equal(sourceType, background.SourceType);
            Assert.Contains("Geospatial Information Authority of Japan", background.AttributionText, StringComparison.Ordinal);
            var layer = Assert.Single(((IMapsuiBackground)background).Layers);
            if (kind == MapBackgroundKind.Raster)
            {
                Assert.IsType<GsiRasterTileSource>(Assert.IsType<TileLayer>(layer).TileSource);
            }
            else
            {
                Assert.IsType<VectorMapBackground>(background);
            }
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Theory]
    [InlineData(MapTileSourceType.EsriWorldImageryRoads, MapTileSourceType.EsriWorldImagery, null)]
    [InlineData(MapTileSourceType.MapboxSatelliteRoads, MapTileSourceType.MapboxSatellite, "test-token")]
    internal void CreateCompositeBackgroundStacksRoadsOverTheImagery(
        MapTileSourceType sourceType, MapTileSourceType imagery, string? token)
    {
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);
        try
        {
            using var background = service.CreateBackground(
                new MapBackgroundRequest(sourceType, "PhotoGeoExplorer/Test", token, TryPostToUi: _ => true));

            var composite = Assert.IsType<CompositeMapBackground>(background);
            Assert.Equal(sourceType, composite.SourceType);
            Assert.Equal(imagery, Assert.IsType<RasterMapBackground>(composite.Imagery).SourceType);
            Assert.IsType<VectorMapBackground>(composite.Overlay);
            Assert.Equal(2, composite.Layers.Count);
            Assert.IsType<TileLayer>(composite.Layers[0]);

            // 合成の背景の名前の置き場は作らない（航空写真と道路は、単独の背景と同じ置き場を使う）
            Assert.False(Directory.Exists(Path.Combine(cacheRoot, sourceType.ToString())));
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector)]
    [InlineData(MapTileSourceType.GsiVector, MapTileSourceType.GsiVector)]
    [InlineData(MapTileSourceType.EsriWorldImageryRoads, MapTileSourceType.OpenFreeMapVector)]
    [InlineData(MapTileSourceType.MapboxSatelliteRoads, MapTileSourceType.OpenFreeMapVector)]
    internal void VectorTilesOfACompositeBackgroundComeFromOpenFreeMap(MapTileSourceType type, MapTileSourceType expected)
        => Assert.Equal(expected, MapsuiMapFactory.GetVectorTileSource(type));

    [Theory]
    [InlineData(MapTileSourceType.EsriWorldImageryRoads, false)]
    [InlineData(MapTileSourceType.MapboxSatelliteRoads, true)]
    internal void CreateCompositeBackgroundFailsWithoutItsRequirements(MapTileSourceType sourceType, bool hasUiThread)
    {
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);
        try
        {
            // 道路には UI スレッドへの入口、Mapbox の航空写真にはアクセストークンが要る
            Assert.Throws<ArgumentException>(() => service.CreateBackground(new MapBackgroundRequest(
                sourceType, "PhotoGeoExplorer/Test", TryPostToUi: hasUiThread ? _ => true : null)));
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void CreateBackgroundRejectsTheVectorBackgroundWithoutAWayToReachTheUiThread()
    {
        var cacheRoot = CreateTempCacheRoot();
        var service = new MapPaneService(cacheRoot);

        try
        {
            Assert.Throws<ArgumentException>(() => service.CreateBackground(
                new MapBackgroundRequest(MapTileSourceType.OpenFreeMapVector, "PhotoGeoExplorer/Test")));
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void CreateBackgroundForTheVectorBackgroundDoesNotThrowWhenCacheRootIsAFile()
    {
        var cacheRoot = CreateTempCacheRoot();
        var cacheRootFile = Path.Combine(cacheRoot, "cache-root-file.txt");
        File.WriteAllText(cacheRootFile, "cache root file");
        var service = new MapPaneService(cacheRootFile);

        try
        {
            using var background = service.CreateBackground(
                new MapBackgroundRequest(MapTileSourceType.OpenFreeMapVector, "PhotoGeoExplorer/Test", TryPostToUi: _ => true));

            Assert.IsType<VectorMapBackground>(background);
        }
        finally
        {
            DeleteTempCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public void CreateBackgroundFallsBackWhenCacheRootIsAFile()
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
            using var background = service.CreateBackground(new MapBackgroundRequest(MapTileSourceType.OpenStreetMap, userAgent));
            var layer = Assert.IsType<TileLayer>(Assert.Single(((IMapsuiBackground)background).Layers));

            // Assert
            Assert.Equal("OpenStreetMap", layer.Name);
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
        public bool HasReportableCrash => false;
        public string CrashReportsDirectoryPath => string.Empty;
        public int WriteCallCount { get; private set; }

        public void RecordStartup() { }
        public void RecordNormalExit() { }
        public void WriteCrashLog(Exception? exception) => WriteCallCount++;
        public string? GetLatestCrashLogContent() => null;
    }
}
