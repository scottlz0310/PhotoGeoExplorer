using System;
using System.IO;
using BruTile.Cache;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// Mapsui の表示面と背景を作る。<see cref="MapPaneService"/> はここへ委譲し、地図ライブラリの型を知らない。
/// </summary>
internal static class MapsuiMapFactory
{
    /// <summary>
    /// 表示面を作る。地図（<see cref="Mapsui.Map"/>）は表示面が持ち、表示面を破棄するときに破棄する。
    /// </summary>
    internal static IMapSurface CreateSurface() => MapsuiMapSurface.Create();

    /// <summary>
    /// 背景を作る。呼び出し側（表示面）が破棄する。
    /// </summary>
    /// <param name="background">背景の生成要求</param>
    /// <param name="tileCacheRootDirectory">タイルのキャッシュのルート。背景ごとのディレクトリをこの下に置く</param>
    internal static IMapBackground CreateBackground(MapBackgroundRequest background, string tileCacheRootDirectory)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(background.UserAgent);
        ArgumentException.ThrowIfNullOrWhiteSpace(tileCacheRootDirectory);

        if (MapBackgroundCatalog.Get(background.SourceType).Kind == MapBackgroundKind.Vector)
        {
            return CreateVectorBackground(background, tileCacheRootDirectory);
        }

        return new RasterMapBackground(CreateTileLayer(background, tileCacheRootDirectory), background.SourceType);
    }

    private static VectorMapBackground CreateVectorBackground(MapBackgroundRequest background, string tileCacheRootDirectory)
    {
        var tryPostToUi = background.TryPostToUi
            ?? throw new ArgumentException("TryPostToUi is required for the vector background.", nameof(background));

        // ラスタと同じく、タイルのキャッシュのルートの下に背景ごとのディレクトリを置く（形式はラスタと別なので混ざらない）
        var renderer = new OpenFreeMapVectorMapRenderer(
            Path.Combine(tileCacheRootDirectory, background.SourceType.ToString()),
            background.UserAgent);
        return new VectorMapBackground(renderer, tryPostToUi, renderer);
    }

    private static TileLayer CreateTileLayer(MapBackgroundRequest background, string tileCacheRootDirectory)
    {
        return background.SourceType switch
        {
            MapTileSourceType.MapboxSatellite => CreateMapboxSatelliteLayer(background),
            MapTileSourceType.EsriWorldImagery => CreateEsriWorldImageryLayer(
                background.UserAgent,
                CreatePersistentCache(tileCacheRootDirectory, background.SourceType)),
            _ => CreateOpenStreetMapLayer(
                background.UserAgent,
                CreatePersistentCache(tileCacheRootDirectory, background.SourceType))
        };
    }

    private static FileCache? CreatePersistentCache(string tileCacheRootDirectory, MapTileSourceType sourceType)
    {
        var sourceDirectory = Path.Combine(tileCacheRootDirectory, sourceType.ToString());
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
