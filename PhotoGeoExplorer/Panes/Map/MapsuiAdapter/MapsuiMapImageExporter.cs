using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoMapStudio.Tiles.Vector;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// <see cref="IMapImageExporter"/> の Mapsui 実装。条件ごとに表示しない地図と背景を作り、描き終えたら破棄する。
/// </summary>
internal sealed class MapsuiMapImageExporter : IMapImageExporter
{
    // 応答のない通信で、画像の作成を待たせ続けない
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<MapBackgroundRequest, IMapBackground> _createBackground;
    private readonly HttpClient _httpClient;

    internal MapsuiMapImageExporter(string tileCacheRootDirectory)
        : this(
            request => MapsuiMapFactory.CreateBackground(request, tileCacheRootDirectory),
            new HttpClient { Timeout = RequestTimeout })
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tileCacheRootDirectory);
    }

    /// <param name="createBackground">背景の生成。テストで通信を差し替える</param>
    /// <param name="httpClient">ラスタタイルの取得に使う通信。このインスタンスが破棄する</param>
    internal MapsuiMapImageExporter(Func<MapBackgroundRequest, IMapBackground> createBackground, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(createBackground);
        ArgumentNullException.ThrowIfNull(httpClient);

        _createBackground = createBackground;
        _httpClient = httpClient;
    }

    public MapImageExportPlan Plan(MapImageExportRequest request)
    {
        var problem = MapImageExportLimits.Validate(request);
        if (problem != MapImageExportProblem.None)
        {
            return new MapImageExportPlan(problem, 0);
        }

        var tileCount = CountTiles(request);
        return new MapImageExportPlan(
            tileCount > MapImageExportLimits.MaxTileCount ? MapImageExportProblem.TooManyTiles : MapImageExportProblem.None,
            tileCount);
    }

    // 大きい画像の描画で UI スレッドを止めないよう、全体をスレッドプールで行う
    public Task<Stream> RenderPngAsync(MapImageExportRequest request, CancellationToken cancellationToken)
        => Task.Run(() => RenderCoreAsync(request, cancellationToken), cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private async Task<Stream> RenderCoreAsync(MapImageExportRequest request, CancellationToken cancellationToken)
    {
        var plan = Plan(request);
        if (plan.Problem != MapImageExportProblem.None)
        {
            throw new InvalidOperationException($"The map image cannot be created: {plan.Problem}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var viewport = CreateViewport(request);
        var pixelDensity = (float)request.PixelDensity;

        using var background = CreateBackground(request);
        using var map = new Mapsui.Map();

        // 地図に最初から入るログ表示などは画像に載せない（帰属表示だけを足す）
        map.Widgets.Clear();
        using var markers = MapsuiMapSurface.CreateMarkerLayer(request.Markers);
        await LoadMarkerImagesAsync(map, markers).ConfigureAwait(true);

        var attribution = new MapAttributionPresenter();
        attribution.Attach(map, request.Background.SourceType);
        attribution.UpdateViewport(viewport);
        if (!await attribution.LoadImagesAsync(map).ConfigureAwait(true))
        {
            throw new InvalidOperationException($"Failed to load the map attribution logo: {MapAttribution.MapboxLogoPath}");
        }

        if (background is RasterMapBackground raster)
        {
            using var tiles = await RasterTileExportLayer
                .CreateAsync(GetTileSource(raster), viewport, _httpClient, cancellationToken)
                .ConfigureAwait(true);
            return Render(map, [tiles, markers], viewport, pixelDensity, cancellationToken);
        }

        var vector = background as VectorMapBackground
            ?? throw new InvalidOperationException("The map background cannot be exported.");
        using var exportLayers = await vector
            .CreateExportLayersAsync(viewport, pixelDensity, cancellationToken)
            .ConfigureAwait(true);
        return Render(map, [.. exportLayers.Layers, markers], viewport, pixelDensity, cancellationToken);
    }

    private static Stream Render(
        Mapsui.Map map,
        ILayer[] layers,
        Viewport viewport,
        float pixelDensity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MapsuiMapImageRenderer.RenderPng(map, layers, viewport, pixelDensity);
    }

    private static Viewport CreateViewport(MapImageExportRequest request)
        => new(
            request.CenterX,
            request.CenterY,
            WebMercator.ResolutionFromLogicalZoom(request.LogicalZoom),
            0,
            request.PixelWidth / request.PixelDensity,
            request.PixelHeight / request.PixelDensity);

    private IMapsuiBackground CreateBackground(MapImageExportRequest request)
    {
        // 保存用の背景は地図へ載せないため、ベクター背景が画面の更新を UI スレッドへ渡すことはない
        var background = _createBackground(request.Background with { TryPostToUi = _ => false });
        return background as IMapsuiBackground
            ?? throw new InvalidOperationException("The map background cannot be exported.");
    }

    private static HttpTileSource GetTileSource(RasterMapBackground background)
        => (background.Layers[0] as TileLayer)?.TileSource as HttpTileSource
            ?? throw new InvalidOperationException("The raster background has no HTTP tile source.");

    private int CountTiles(MapImageExportRequest request)
    {
        var viewport = CreateViewport(request);
        if (MapBackgroundCatalog.Get(request.Background.SourceType).Kind == MapBackgroundKind.Raster)
        {
            using var background = CreateBackground(request);
            return background is RasterMapBackground raster
                ? RasterTileExportLayer.SelectTiles(GetTileSource(raster).Schema, viewport).Count
                : throw new InvalidOperationException("The raster background has no HTTP tile source.");
        }

        // ベクターは、物理ピクセルのズームを切り捨てた段階のタイルを描く。配信元の最大ズームより先は拡大する
        var maxZoom = request.Background.SourceType == MapTileSourceType.GsiVector
            ? GsiVectorTileSource.DefaultMaxZoom
            : OpenFreeMapTileSource.DefaultMaxZoom;
        var zoom = Math.Min((int)Math.Floor(request.LogicalZoom + Math.Log2(request.PixelDensity)), maxZoom);
        var schema = new GlobalSphericalMercator(0, maxZoom);
        var atTileZoom = viewport with { Resolution = WebMercator.ResolutionFromLogicalZoom(zoom) };
        var scale = viewport.Resolution / atTileZoom.Resolution;
        return RasterTileExportLayer.SelectTiles(
            schema,
            atTileZoom with { Width = viewport.Width * scale, Height = viewport.Height * scale }).Count;
    }

    private static async Task LoadMarkerImagesAsync(Mapsui.Map map, MemoryLayer markers)
    {
        var images = markers.Features
            .SelectMany(feature => feature.Styles)
            .OfType<ImageStyle>()
            .Select(style => style.Image!)
            .DistinctBy(image => image.Source);
        foreach (var image in images)
        {
            if (!await map.RenderService.ImageSourceCache.TryRegisterAsync(image.SourceId, image.Source).ConfigureAwait(true))
            {
                throw new InvalidOperationException($"Failed to load the map pin image: {image.Source}");
            }
        }
    }
}
