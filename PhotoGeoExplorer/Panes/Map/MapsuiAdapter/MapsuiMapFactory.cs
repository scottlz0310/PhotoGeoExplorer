using System;
using System.IO;
using BruTile.Cache;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoMapStudio.Tiles;

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

        var descriptor = MapBackgroundCatalog.Get(background.SourceType);
        return descriptor.Kind switch
        {
            MapBackgroundKind.Vector => CreateVectorBackground(background, tileCacheRootDirectory),
            MapBackgroundKind.Composite => CreateCompositeBackground(background, descriptor, tileCacheRootDirectory),
            _ => CreateRasterBackground(background, tileCacheRootDirectory)
        };
    }

    private static RasterMapBackground CreateRasterBackground(MapBackgroundRequest background, string tileCacheRootDirectory)
        => new(CreateTileLayer(background, tileCacheRootDirectory), background.SourceType);

    // 下の航空写真は、単独の背景と同じ取得とキャッシュを使う。上の道路は、ベクター背景（OpenFreeMap）とタイルのキャッシュを共有する
    private static CompositeMapBackground CreateCompositeBackground(
        MapBackgroundRequest background,
        MapBackgroundDescriptor descriptor,
        string tileCacheRootDirectory)
    {
        var imagerySource = descriptor.ImagerySource
            ?? throw new InvalidOperationException($"The composite background has no imagery source: {descriptor.Type}.");
        var overlay = CreateVectorBackground(background, tileCacheRootDirectory);
        try
        {
            var imagery = CreateRasterBackground(background with { SourceType = imagerySource }, tileCacheRootDirectory);
            return new CompositeMapBackground(imagery, overlay, background.SourceType);
        }
        catch
        {
            overlay.Dispose();
            throw;
        }
    }

    /// <summary>
    /// ベクターで描く部分のタイルの配信元。タイルのキャッシュの置き場も、この名前にする
    /// </summary>
    internal static MapTileSourceType GetVectorTileSource(MapTileSourceType type)
        => MapBackgroundCatalog.Get(type).Kind == MapBackgroundKind.Composite ? MapTileSourceType.OpenFreeMapVector : type;

    private static VectorMapBackground CreateVectorBackground(MapBackgroundRequest background, string tileCacheRootDirectory)
    {
        var tryPostToUi = background.TryPostToUi
            ?? throw new ArgumentException("TryPostToUi is required for the vector background.", nameof(background));
        var roadOverlay = MapBackgroundCatalog.Get(background.SourceType).Kind == MapBackgroundKind.Composite;
        var tileSource = GetVectorTileSource(background.SourceType);

        // ラスタと同じく、タイルのキャッシュのルートの下に背景ごとのディレクトリを置く（形式はラスタと別なので混ざらない）
        var renderer = new TileVectorMapRenderer(
            Path.Combine(tileCacheRootDirectory, tileSource.ToString()),
            background.UserAgent,
            sourceType: tileSource,
            roadOverlay: roadOverlay);
        return new VectorMapBackground(renderer, tryPostToUi, renderer, sourceType: background.SourceType);
    }

    private static TileLayer CreateTileLayer(MapBackgroundRequest background, string tileCacheRootDirectory)
    {
        return background.SourceType switch
        {
            MapTileSourceType.MapboxSatellite => CreateMapboxSatelliteLayer(background),
            MapTileSourceType.EsriWorldImagery => CreateEsriWorldImageryLayer(
                background.UserAgent,
                CreatePersistentCache(tileCacheRootDirectory, background.SourceType)),
            MapTileSourceType.GsiStandard => CreateGsiLayer(background, tileCacheRootDirectory, TileSources.GsiStandard),
            MapTileSourceType.GsiPale => CreateGsiLayer(background, tileCacheRootDirectory, TileSources.GsiPale),
            MapTileSourceType.GsiPhoto => CreateGsiLayer(background, tileCacheRootDirectory, GsiPhoto),
            _ => CreateOpenStreetMapLayer(
                background.UserAgent,
                CreatePersistentCache(tileCacheRootDirectory, background.SourceType))
        };
    }

    private static readonly TileSource GsiPhoto = new(
        "GSI Aerial Imagery",
        "https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg",
        minZoom: 2,
        maxZoom: 18,
        "Geospatial Information Authority of Japan",
        TileRateLimit.Relaxed,
        new Uri(MapBackgroundCatalog.GsiAttributionUrl));

    private static TileLayer CreateGsiLayer(MapBackgroundRequest background, string cacheRoot, TileSource source)
    {
        var tileSource = new GsiRasterTileSource(
            source,
            background.UserAgent,
            CreatePersistentCache(cacheRoot, background.SourceType, background.SourceType == MapTileSourceType.GsiPhoto ? "jpg" : "png"));
        return new TileLayer(tileSource) { Name = source.Name };
    }

    private static FileCache? CreatePersistentCache(string tileCacheRootDirectory, MapTileSourceType sourceType, string format = "png")
    {
        var sourceDirectory = Path.Combine(tileCacheRootDirectory, sourceType.ToString());
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            return new FileCache(sourceDirectory, format);
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
