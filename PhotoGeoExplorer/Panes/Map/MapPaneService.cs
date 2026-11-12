using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BruTile.Cache;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// Map Pane 専用のサービス
/// I/O処理とビジネスロジックを分離
/// </summary>
internal sealed class MapPaneService : IMapPaneService
{
    private const int MetadataLoadMaxConcurrency = 4;
    private readonly string _tileCacheRootDirectory;
    private readonly ICrashReportService _crashReportService;

    public MapPaneService()
        : this(GetDefaultTileCacheRootDirectory(), new CrashReportService())
    {
    }

    internal MapPaneService(string tileCacheRootDirectory)
        : this(tileCacheRootDirectory, new CrashReportService())
    {
    }

    internal MapPaneService(string tileCacheRootDirectory, ICrashReportService crashReportService)
    {
        if (string.IsNullOrWhiteSpace(tileCacheRootDirectory))
        {
            throw new ArgumentException("Tile cache root directory is required.", nameof(tileCacheRootDirectory));
        }

        _tileCacheRootDirectory = tileCacheRootDirectory;
        _crashReportService = crashReportService ?? throw new ArgumentNullException(nameof(crashReportService));
    }

    /// <inheritdoc/>
    public (Mapsui.Map Map, TileLayer TileLayer, MemoryLayer MarkerLayer) InitializeMap(MapBackgroundRequest background)
    {
        ArgumentNullException.ThrowIfNull(background);

        // レイヤー生成が失敗したときに Map を取り残さないよう、先にレイヤーを作る。
        var tileLayer = CreateTileLayer(background);
        var map = new Mapsui.Map();
        map.Layers.Add(tileLayer);

        var markerLayer = new MemoryLayer
        {
            Name = "PhotoMarkers",
            Features = Array.Empty<IFeature>(),
            Style = null
        };
        map.Layers.Add(markerLayer);

        return (map, tileLayer, markerLayer);
    }

    /// <inheritdoc/>
    public TileLayer CreateTileLayer(MapBackgroundRequest background)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(background.UserAgent);

        return background.SourceType switch
        {
            MapTileSourceType.MapboxSatellite => CreateMapboxSatelliteLayer(background),
            MapTileSourceType.EsriWorldImagery => CreateEsriWorldImageryLayer(
                background.UserAgent,
                CreatePersistentCache(background.SourceType)),
            _ => CreateOpenStreetMapLayer(
                background.UserAgent,
                CreatePersistentCache(background.SourceType))
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
        IReadOnlyList<PhotoListItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return Array.Empty<(PhotoListItem Item, PhotoMetadata? Metadata)>();
        }

        var concurrency = Math.Clamp(Environment.ProcessorCount, 1, MetadataLoadMaxConcurrency);
        concurrency = Math.Min(concurrency, items.Count);

        // semaphore は WhenAll で全タスク完了後に破棄されるため安全
#pragma warning disable CA2025
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var tasks = new Task<(PhotoListItem Item, PhotoMetadata? Metadata)>[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            tasks[index] = LoadMetadataForItemAsync(item, semaphore, cancellationToken);
        }
#pragma warning restore CA2025

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    /// <inheritdoc/>
    public string GetTileCacheRootDirectory()
    {
        return _tileCacheRootDirectory;
    }

    public string GetPinImagePath(PhotoMetadata metadata)
    {
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins");
        if (metadata.TakenAt is DateTimeOffset takenAt)
        {
            var age = DateTimeOffset.Now - takenAt;
            if (age <= TimeSpan.FromDays(30))
            {
                return Path.Combine(assetsRoot, "green_pin.png");
            }

            if (age <= TimeSpan.FromDays(365))
            {
                return Path.Combine(assetsRoot, "blue_pin.png");
            }
        }

        return Path.Combine(assetsRoot, "red_pin.png");
    }

    public bool FileExistsAtPath(string path)
        => File.Exists(path);

    private FileCache? CreatePersistentCache(MapTileSourceType sourceType)
    {
        var cacheDirectory = GetTileCacheRootDirectory();
        var sourceDirectory = Path.Combine(cacheDirectory, sourceType.ToString());
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            return new FileCache(sourceDirectory, "png");
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            AppLog.Error($"Failed to initialize tile cache directory '{sourceDirectory}'. Continuing without persistent cache.", ex);
            return null;
        }
    }

    private async Task<(PhotoListItem Item, PhotoMetadata? Metadata)> LoadMetadataForItemAsync(
        PhotoListItem item,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            if (cancellationToken.IsCancellationRequested)
            {
                return (item, null);
            }

            var metadata = await ExifReader.GetMetadataAsync(item.Item.FilePath, cancellationToken).ConfigureAwait(false);
            return (item, metadata);
        }
        catch (OperationCanceledException)
        {
            return (item, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Error($"Failed to load metadata for {item.Item.FilePath}", ex);
            return (item, null);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            // MetadataExtractor 等、想定外の例外でクラッシュする代わりに当該ファイルをスキップする。
            // WriteCrashLog で running.lock を維持し、次回起動時の報告フローへ情報を引き渡す。
            AppLog.Error($"Unexpected exception loading metadata for {item.Item.FilePath}", ex);
            _crashReportService.WriteCrashLog(ex);
            return (item, null);
        }
#pragma warning restore CA1031
        finally
        {
            if (acquired)
            {
                semaphore.Release();
            }
        }
    }

    private static string GetDefaultTileCacheRootDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoGeoExplorer",
            "Cache",
            "Tiles");
    }

    private static TileLayer CreateOpenStreetMapLayer(string userAgent, IPersistentCache<byte[]>? persistentCache = null)
    {
        var tileSource = new BruTile.Web.HttpTileSource(
            new BruTile.Predefined.GlobalSphericalMercator(),
            "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            name: "OpenStreetMap",
            attribution: new BruTile.Attribution(MapAttribution.OpenStreetMapText, MapAttribution.OpenStreetMapUrl),
            configureHttpRequestMessage: (r) => r.Headers.TryAddWithoutValidation("User-Agent", userAgent),
            persistentCache: persistentCache);

        return new TileLayer(tileSource) { Name = "OpenStreetMap" };
    }

    private static TileLayer CreateEsriWorldImageryLayer(string userAgent, IPersistentCache<byte[]>? persistentCache = null)
    {
        var tileSource = new BruTile.Web.HttpTileSource(
            new BruTile.Predefined.GlobalSphericalMercator(),
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            name: "Esri WorldImagery",
            attribution: new BruTile.Attribution(MapAttribution.EsriText, MapAttribution.EsriUrl),
            configureHttpRequestMessage: (r) => r.Headers.TryAddWithoutValidation("User-Agent", userAgent),
            persistentCache: persistentCache);

        return new TileLayer(tileSource) { Name = "Esri WorldImagery" };
    }

    private static TileLayer CreateMapboxSatelliteLayer(MapBackgroundRequest background)
    {
        if (string.IsNullOrWhiteSpace(background.MapboxAccessToken))
        {
            throw new ArgumentException("Mapbox access token is required for the Mapbox Satellite background.", nameof(background));
        }

        var tileSource = new MapboxSatelliteTileSource(
            background.MapboxAccessToken,
            background.UserAgent,
            background.AuthenticationFailed);

        return new TileLayer(tileSource) { Name = MapboxSatelliteTileSource.SourceName };
    }
}
