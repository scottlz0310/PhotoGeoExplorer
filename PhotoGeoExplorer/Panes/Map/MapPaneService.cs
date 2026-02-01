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

    public MapPaneService()
        : this(GetDefaultTileCacheRootDirectory())
    {
    }

    internal MapPaneService(string tileCacheRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(tileCacheRootDirectory))
        {
            throw new ArgumentException("Tile cache root directory is required.", nameof(tileCacheRootDirectory));
        }

        _tileCacheRootDirectory = tileCacheRootDirectory;
    }

    /// <inheritdoc/>
    public (Mapsui.Map Map, TileLayer TileLayer, MemoryLayer MarkerLayer) InitializeMap(
        MapTileSourceType tileSource,
        string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        var map = new Mapsui.Map();
        var tileLayer = CreateTileLayer(tileSource, userAgent);
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
    public TileLayer CreateTileLayer(MapTileSourceType sourceType, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        var cacheDirectory = GetTileCacheRootDirectory();
        var sourceDirectory = Path.Combine(cacheDirectory, sourceType.ToString());
        Directory.CreateDirectory(sourceDirectory);
        var persistentCache = new FileCache(sourceDirectory, "png");

        return sourceType switch
        {
            MapTileSourceType.EsriWorldImagery => CreateEsriWorldImageryLayer(userAgent, persistentCache),
            _ => CreateOpenStreetMapLayer(userAgent, persistentCache)
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

        using var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var tasks = new Task<(PhotoListItem Item, PhotoMetadata? Metadata)>[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            tasks[index] = LoadMetadataForItemAsync(item, semaphore, cancellationToken);
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    /// <inheritdoc/>
    public string GetTileCacheRootDirectory()
    {
        return _tileCacheRootDirectory;
    }

    private static async Task<(PhotoListItem Item, PhotoMetadata? Metadata)> LoadMetadataForItemAsync(
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

            var metadata = await ExifService.GetMetadataAsync(item.Item.FilePath, cancellationToken).ConfigureAwait(false);
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
            attribution: new BruTile.Attribution("© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright"),
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
            attribution: new BruTile.Attribution("Esri, i-cubed, USDA, USGS, AEX, GeoEye, Getmapping, Aerogrid, IGN, IGP, UPR-EGP, and the GIS User Community"),
            configureHttpRequestMessage: (r) => r.Headers.TryAddWithoutValidation("User-Agent", userAgent),
            persistentCache: persistentCache);

        return new TileLayer(tileSource) { Name = "Esri WorldImagery" };
    }
}
