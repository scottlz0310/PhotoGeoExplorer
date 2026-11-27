using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using BruTile.Cache;
using Mapsui.Projections;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoMapStudio.Rendering;
using SkiaSharp;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapsuiMapImageExporterTests : IDisposable
{
    private static readonly SKColor TileColor = new(30, 140, 70);

    private readonly DirectoryInfo _cache = Directory.CreateTempSubdirectory("pge-map-export-");

    public void Dispose() => _cache.Delete(recursive: true);

    [Theory]
    [InlineData(800, 600, 1.0)]
    [InlineData(1280, 720, 1.25)]
    [InlineData(1000, 700, 1.5)]
    [InlineData(641, 479, 1.75)]
    [InlineData(640, 480, 5.0)]
    public async Task RenderPngAsyncCreatesTheRequestedPixelSizeFilledWithTiles(int width, int height, double density)
    {
        // 中央と上端の画素は、右下の帰属表示にかからない大きさで確かめる
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);

        using var bitmap = await RenderAsync(exporter, Request(width: width, height: height, density: density)).ConfigureAwait(true);

        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
        Assert.Equal(TileColor, bitmap.GetPixel(1, 1));
        Assert.Equal(TileColor, bitmap.GetPixel(width / 2, height / 2));
        Assert.Equal(TileColor, bitmap.GetPixel(width - 2, 1));
    }

    [Fact]
    public async Task RenderPngAsyncAcceptsTheSmallestImageAtTheHighestDensity()
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);
        var request = Request(width: 64, height: 64, density: MapImageExportLimits.MaxPixelDensity);

        using var bitmap = await RenderAsync(exporter, request).ConfigureAwait(true);

        Assert.Equal(64, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, 14.0, 1.0)]
    [InlineData(MapTileSourceType.OpenStreetMap, 14.6, 1.5)]
    [InlineData(MapTileSourceType.GsiPhoto, 9.2, 2.0)]
    internal async Task PlanCountsTheTilesThatRenderingRequests(MapTileSourceType sourceType, double zoom, double density)
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);
        var request = Request(sourceType, zoom, density: density);

        var plan = exporter.Plan(request);
        Assert.Empty(handler.Requests);

        using var bitmap = await RenderAsync(exporter, request).ConfigureAwait(true);

        Assert.Equal(MapImageExportProblem.None, plan.Problem);
        Assert.InRange(plan.TileCount, 1, MapImageExportLimits.MaxTileCount);
        Assert.Equal(plan.TileCount, handler.Requests.Distinct().Count());
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap)]
    [InlineData(MapTileSourceType.OpenFreeMapVector)]
    [InlineData(MapTileSourceType.GsiVector)]
    internal async Task ConditionsThatNeedTooManyTilesAreRefusedWithoutFetching(MapTileSourceType sourceType)
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);

        // ベクターは配信元の最大ズーム以下でタイルが増えるため、その範囲のズームにする
        var request = Request(sourceType, zoom: 12, width: 4096, height: 4096) with { LogicalZoom = 12 };
        var plan = exporter.Plan(request);

        if (MapBackgroundCatalog.Get(sourceType).Kind == MapBackgroundKind.Raster)
        {
            Assert.Equal(MapImageExportProblem.TooManyTiles, plan.Problem);
            Assert.True(plan.TileCount > MapImageExportLimits.MaxTileCount);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => exporter.RenderPngAsync(request, CancellationToken.None)).ConfigureAwait(true);
        }
        else
        {
            Assert.True(plan.TileCount > MapImageExportLimits.MaxTileCount);
            Assert.Equal(MapImageExportProblem.TooManyTiles, plan.Problem);
        }

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenFreeMapVector, 10.0, 1.0, 800, 600)]
    [InlineData(MapTileSourceType.GsiVector, 18.0, 1.5, 800, 600)]
    internal void PlanCountsVectorTilesWithinTheLimit(MapTileSourceType sourceType, double zoom, double density, int width, int height)
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);

        var plan = exporter.Plan(Request(sourceType, zoom, width, height, density));

        Assert.Equal(MapImageExportProblem.None, plan.Problem);
        Assert.InRange(plan.TileCount, 1, 30);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task RenderPngAsyncFailsInsteadOfLeavingHolesWhenATileCannotBeFetched(HttpStatusCode status)
    {
        using var handler = new TileHandler(index => index == 2 ? status : HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => exporter.RenderPngAsync(Request(), CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(status, Assert.IsType<HttpRequestException>(error.InnerException).StatusCode);
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, HttpStatusCode.OK, TileBody.NotAnImage)]
    [InlineData(MapTileSourceType.OpenStreetMap, HttpStatusCode.OK, TileBody.Truncated)]
    [InlineData(MapTileSourceType.OpenStreetMap, HttpStatusCode.NoContent, TileBody.Empty)]
    [InlineData(MapTileSourceType.GsiStandard, HttpStatusCode.OK, TileBody.NotAnImage)]
    [InlineData(MapTileSourceType.GsiPhoto, HttpStatusCode.OK, TileBody.Truncated)]
    internal async Task RenderPngAsyncFailsInsteadOfLeavingHolesWhenATileImageIsBroken(
        MapTileSourceType sourceType, HttpStatusCode status, TileBody body)
    {
        using var handler = new TileHandler(
            index => index == 2 ? status : HttpStatusCode.OK,
            index => index == 2 ? body : TileBody.Image);
        using var exporter = CreateExporter(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => exporter.RenderPngAsync(Request(sourceType), CancellationToken.None)).ConfigureAwait(true);

        // 空の応答は、通信の層で失敗になる場合がある
        Assert.NotNull(error.InnerException);
    }

    [Theory]
    [InlineData(TileBody.NotAnImage)]
    [InlineData(TileBody.Truncated)]
    internal async Task RenderPngAsyncFetchesABrokenTileAgainInsteadOfKeepingItInTheCache(TileBody body)
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK, index => index == 1 ? body : TileBody.Image);
        using var exporter = CreateExporterWithCache(handler, _cache.FullName);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => exporter.RenderPngAsync(Request(), CancellationToken.None)).ConfigureAwait(true);

        using var bitmap = await RenderAsync(exporter, Request()).ConfigureAwait(true);

        Assert.Equal(2, handler.Requests.Count(uri => uri == handler.BrokenRequest));
        Assert.Equal(TileColor, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2));
    }

    [Fact]
    public async Task RenderPngAsyncKeepsATileStoredByTheDisplayWhenItsOwnResponseIsBroken()
    {
        // 表示と保存が同じタイルを持っていない状態で通信し、表示側が先に正常な画像を書く順序
        var shared = SharedCache(_cache.FullName);
        using var handler = new TileHandler(
            _ => HttpStatusCode.OK,
            index => index == 1 ? TileBody.NotAnImage : TileBody.Image,
            uri => shared.Add(TileIndexOf(uri), TileHandler.Tile));
        using var exporter = CreateExporterWithCache(handler, _cache.FullName);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => exporter.RenderPngAsync(Request(), CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(TileHandler.Tile, shared.Find(TileIndexOf(handler.BrokenRequest!)));
    }

    [Fact]
    public async Task RenderPngAsyncReusesStoredTilesAndReplacesABrokenOne()
    {
        var shared = SharedCache(_cache.FullName);
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporterWithCache(handler, _cache.FullName);
        using var first = await RenderAsync(exporter, Request()).ConfigureAwait(true);
        var fetched = handler.Requests.Count;

        // 2 回目は、保存したタイルだけで描ける
        using var second = await RenderAsync(exporter, Request()).ConfigureAwait(true);
        Assert.Equal(fetched, handler.Requests.Count);

        var index = TileIndexOf(handler.Requests.First()!);
        shared.Remove(index);
        shared.Add(index, [1, 2, 3, 4]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => exporter.RenderPngAsync(Request(), CancellationToken.None)).ConfigureAwait(true);
        Assert.Null(shared.Find(index));

        using var third = await RenderAsync(exporter, Request()).ConfigureAwait(true);
        Assert.Equal(fetched + 1, handler.Requests.Count);
        Assert.Equal(TileHandler.Tile, shared.Find(index));
    }

    [Fact]
    public void ExportTileCacheDoesNotWriteAResponseBeforeItIsVerified()
    {
        var shared = SharedCache(_cache.FullName);
        var index = new TileIndex(1, 2, 3);

        new ExportTileCache(shared).Add(index, [1, 2, 3, 4]);

        Assert.Null(shared.Find(index));
    }

    [Fact]
    public async Task RenderPngAsyncSucceedsWhenTheCacheCannotBeWritten()
    {
        // ズーム段階の名前のファイルがあると、その下へタイルを書けない
        var source = Directory.CreateDirectory(Path.Combine(_cache.FullName, nameof(MapTileSourceType.OpenStreetMap)));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "14"), "blocked").ConfigureAwait(true);
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporterWithCache(handler, _cache.FullName);

        using var bitmap = await RenderAsync(exporter, Request()).ConfigureAwait(true);

        Assert.Equal(TileColor, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2));
    }

    [Fact]
    public async Task RenderPngAsyncFetchesATileThatTheDisplayHasLocked()
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporterWithCache(handler, _cache.FullName);
        using var first = await RenderAsync(exporter, Request()).ConfigureAwait(true);
        var fetched = handler.Requests.Count;
        var cachedFile = Directory.EnumerateFiles(_cache.FullName, "*.png", SearchOption.AllDirectories).First();
        using var locked = new FileStream(cachedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        using var second = await RenderAsync(exporter, Request()).ConfigureAwait(true);

        Assert.Equal(fetched + 1, handler.Requests.Count);
        Assert.Equal(TileColor, second.GetPixel(second.Width / 2, second.Height / 2));
    }

    [Fact]
    public async Task RenderPngAsyncLeavesTilesOutsideTheGsiCoverageBlank()
    {
        using var handler = new TileHandler(_ => HttpStatusCode.NotFound);
        using var exporter = CreateExporter(handler);

        using var bitmap = await RenderAsync(exporter, Request(MapTileSourceType.GsiStandard)).ConfigureAwait(true);

        Assert.Equal(SKColors.White, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2));
    }

    [Fact]
    public async Task RenderPngAsyncDrawsTheAttributionOfTheRequestedBackground()
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);

        using var bitmap = await RenderAsync(exporter, Request()).ConfigureAwait(true);

        // 右下の帰属表示は、タイルより明るい下地に描かれる
        Assert.NotEqual(TileColor, bitmap.GetPixel(bitmap.Width - 8, bitmap.Height - 8));
    }

    [Fact]
    public async Task RenderPngAsyncDrawsThePhotoPinAtItsPosition()
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);
        const double latitude = 35.681236;
        const double longitude = 139.767125;
        var marker = new MapMarker(
            latitude,
            longitude,
            new PhotoItem(@"C:\Photos\pin.jpg", 0, DateTimeOffset.UnixEpoch, isFolder: false),
            new PhotoMetadata(null, null, null, latitude, longitude),
            new MapPaneService(_cache.FullName).GetPinImagePath(new PhotoMetadata(null, null, null, latitude, longitude)));
        var request = Request() with { Markers = [marker] };

        using var bitmap = await RenderAsync(exporter, request).ConfigureAwait(true);

        // ピンは、下端の中央が撮影位置になる
        Assert.NotEqual(TileColor, bitmap.GetPixel(bitmap.Width / 2, (bitmap.Height / 2) - 12));
    }

    [Fact]
    public async Task RenderPngAsyncStopsWhenCanceled()
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => exporter.RenderPngAsync(Request(), canceled.Token)).ConfigureAwait(true);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(13.4, 1.0)]
    [InlineData(15.0, 1.5)]
    public async Task RenderPngAsyncAsksTheVectorRendererForTheRequestedView(double zoom, double density)
    {
        var renderer = new RecordingVectorRenderer();
        using var client = new HttpClient();
#pragma warning disable CA2000 // 背景と通信は exporter が破棄する
        using var exporter = new MapsuiMapImageExporter(
            request => new VectorMapBackground(renderer, request.TryPostToUi!, sourceType: request.SourceType),
            new HttpClient());
#pragma warning restore CA2000
        var request = Request(MapTileSourceType.GsiVector, zoom, 900, 600, density);

        using var bitmap = await RenderAsync(exporter, request).ConfigureAwait(true);

        var view = Assert.Single(renderer.Requests);
        Assert.Equal(900, view.PixelWidth);
        Assert.Equal(600, view.PixelHeight);
        Assert.Equal(density, view.PixelRatio, 1e-6);
        Assert.Equal(zoom + Math.Log2(density), view.Zoom, 1e-6);
        Assert.Equal(35.681236, view.CenterLatitude, 1e-6);
        Assert.Equal(139.767125, view.CenterLongitude, 1e-6);
        Assert.False(view.AllowIncompleteMaps);
    }

    [Theory]
    [InlineData(13.4, 1.0)]
    [InlineData(15.0, 1.5)]
    public async Task RenderPngAsyncLaysTheRoadsOverTheImagery(double zoom, double density)
    {
        var renderer = new RoadStripeRenderer();
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateCompositeExporter(handler, renderer);
        var request = Request(MapTileSourceType.EsriWorldImageryRoads, zoom, 900, 600, density);

        using var bitmap = await RenderAsync(exporter, request).ConfigureAwait(true);

        // 道路の帯は上端の行だけに描く。帯の外は航空写真が見える
        Assert.Equal(RoadStripeRenderer.RoadColor, bitmap.GetPixel(450, 5));
        Assert.Equal(TileColor, bitmap.GetPixel(450, 300));
        Assert.All(handler.Requests, uri => Assert.Contains("World_Imagery", uri!.AbsoluteUri, StringComparison.Ordinal));
        var view = Assert.Single(renderer.Requests);
        Assert.Equal(900, view.PixelWidth);
        Assert.Equal(zoom + Math.Log2(density), view.Zoom, 1e-6);
        Assert.False(view.AllowIncompleteMaps);
    }

    [Fact]
    public async Task RenderPngAsyncFailsWhenTheRoadsOfACompositeBackgroundCannotBeDrawn()
    {
        var renderer = new RoadStripeRenderer { Failure = new InvalidOperationException("roads") };
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateCompositeExporter(handler, renderer);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => exporter.RenderPngAsync(Request(MapTileSourceType.EsriWorldImageryRoads), CancellationToken.None)).ConfigureAwait(true);

        Assert.Same(renderer.Failure, error);
    }

    [Theory]
    [InlineData(MapTileSourceType.EsriWorldImageryRoads, 14.0, 1.0, MapImageExportProblem.None)]
    [InlineData(MapTileSourceType.MapboxSatelliteRoads, 12.0, 1.5, MapImageExportProblem.None)]
    internal void PlanCountsBothTheImageryAndRoadTiles(
        MapTileSourceType sourceType, double zoom, double density, MapImageExportProblem expected)
    {
        using var handler = new TileHandler(_ => HttpStatusCode.OK);
        using var exporter = CreateExporter(handler);
        var imagery = MapBackgroundCatalog.Get(sourceType).ImagerySource!.Value;
        MapImageExportRequest With(MapTileSourceType type)
        {
            var request = Request(type, zoom, density: density);
            return request with { Background = request.Background with { MapboxAccessToken = "test-token" } };
        }

        var plan = exporter.Plan(With(sourceType));

        Assert.Equal(expected, plan.Problem);
        Assert.Equal(
            exporter.Plan(With(imagery)).TileCount + exporter.Plan(With(MapTileSourceType.OpenFreeMapVector)).TileCount,
            plan.TileCount);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(63, 600, 14.0, 1.0, MapImageExportProblem.SizeOutOfRange)]
    [InlineData(800, 4097, 14.0, 1.0, MapImageExportProblem.SizeOutOfRange)]
    [InlineData(800, 600, -0.1, 1.0, MapImageExportProblem.ZoomOutOfRange)]
    [InlineData(800, 600, 22.1, 1.0, MapImageExportProblem.ZoomOutOfRange)]
    [InlineData(800, 600, double.NaN, 1.0, MapImageExportProblem.ZoomOutOfRange)]
    [InlineData(800, 600, 14.0, 0.9, MapImageExportProblem.PixelDensityOutOfRange)]
    [InlineData(800, 600, 14.0, 5.1, MapImageExportProblem.PixelDensityOutOfRange)]
    [InlineData(800, 600, 14.0, double.NaN, MapImageExportProblem.PixelDensityOutOfRange)]
    [InlineData(64, 4096, 0.0, 5.0, MapImageExportProblem.None)]
    [InlineData(4096, 64, 22.0, 1.0, MapImageExportProblem.None)]
    internal void ValidateChecksSizeZoomAndDensity(
        int width, int height, double zoom, double density, MapImageExportProblem expected)
    {
        Assert.Equal(expected, MapImageExportLimits.Validate(Request(zoom: zoom, width: width, height: height, density: density)));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(20037509, 0)]
    public void ValidateRejectsACenterOutsideTheWorld(double centerX, double centerY)
    {
        var request = Request() with { CenterX = centerX, CenterY = centerY };

        Assert.Equal(MapImageExportProblem.CenterOutOfRange, MapImageExportLimits.Validate(request));
    }

    private static MapImageExportRequest Request(
        MapTileSourceType sourceType = MapTileSourceType.OpenStreetMap,
        double zoom = 14,
        int width = 800,
        int height = 600,
        double density = 1)
        => new(
            new MapBackgroundRequest(sourceType, "PhotoGeoExplorer.Tests"),
            SphericalMercator.FromLonLat(139.767125, 35.681236).x,
            SphericalMercator.FromLonLat(139.767125, 35.681236).y,
            zoom,
            width,
            height,
            density,
            []);

    private MapsuiMapImageExporter CreateExporter(TileHandler handler)
    {
        // キャッシュを使うと取得の回数が変わるため、テストごとに別の場所にする
        var root = Path.Combine(_cache.FullName, Guid.NewGuid().ToString("N"));
        var calls = 0;
#pragma warning disable CA2000 // 通信は exporter が破棄する
        return new MapsuiMapImageExporter(
            request => MapsuiMapFactory.CreateBackground(request, Path.Combine(root, (calls++).ToString(CultureInfo.InvariantCulture))),
            new HttpClient(handler, disposeHandler: false));
#pragma warning restore CA2000
    }

    // 航空写真は本物の背景を使い、道路はレンダラーを差し替える（OpenFreeMap へ通信しない）
    private MapsuiMapImageExporter CreateCompositeExporter(TileHandler handler, IVectorMapRenderer renderer)
    {
        var root = Path.Combine(_cache.FullName, Guid.NewGuid().ToString("N"));
#pragma warning disable CA2000 // 背景と通信は exporter が破棄する
        return new MapsuiMapImageExporter(
            request => MapBackgroundCatalog.Get(request.SourceType) is { ImagerySource: { } imagery }
                ? new CompositeMapBackground(
                    (IMapsuiBackground)MapsuiMapFactory.CreateBackground(request with { SourceType = imagery }, root),
                    new VectorMapBackground(renderer, request.TryPostToUi!, sourceType: request.SourceType),
                    request.SourceType)
                : MapsuiMapFactory.CreateBackground(request, root),
            new HttpClient(handler, disposeHandler: false));
#pragma warning restore CA2000
    }

    private sealed class RoadStripeRenderer : IVectorMapRenderer
    {
        internal static readonly SKColor RoadColor = new(250, 200, 40);

        public Exception? Failure { get; init; }

        public ConcurrentQueue<VectorViewRequest> Requests { get; } = new();

        public Task<VectorViewResult> RenderViewAsync(VectorViewRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            if (Failure is { } failure)
            {
                return Task.FromException<VectorViewResult>(failure);
            }

            using var bitmap = new SKBitmap(request.PixelWidth, request.PixelHeight);
            bitmap.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(bitmap))
            {
                using var paint = new SKPaint { Color = RoadColor };
                canvas.DrawRect(0, 0, request.PixelWidth, 20, paint);
            }

            return Task.FromResult(new VectorViewResult(SKImage.FromBitmap(bitmap)));
        }
    }

    private static MapsuiMapImageExporter CreateExporterWithCache(TileHandler handler, string root)
    {
#pragma warning disable CA2000 // 通信は exporter が破棄する
        return new MapsuiMapImageExporter(
            request => MapsuiMapFactory.CreateBackground(request, root),
            new HttpClient(handler, disposeHandler: false));
#pragma warning restore CA2000
    }

    /// <summary>表示側と同じ置き場を読み書きする、別のキャッシュ</summary>
    private static FileCache SharedCache(string root)
        => new(Path.Combine(root, nameof(MapTileSourceType.OpenStreetMap)), "png");

    /// <summary>タイルの URL（.../ズーム/列/行.png）が指すタイル</summary>
    private static TileIndex TileIndexOf(Uri uri)
    {
        var parts = uri.AbsolutePath.Split('/')[^3..];
        return new TileIndex(
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(Path.GetFileNameWithoutExtension(parts[2]), CultureInfo.InvariantCulture),
            int.Parse(parts[0], CultureInfo.InvariantCulture));
    }

    private static async Task<SKBitmap> RenderAsync(MapsuiMapImageExporter exporter, MapImageExportRequest request)
    {
        using var stream = await exporter.RenderPngAsync(request, CancellationToken.None).ConfigureAwait(true);
        return SKBitmap.Decode(stream);
    }

    internal enum TileBody
    {
        Image,
        NotAnImage,
        Truncated,
        Empty,
    }

    private sealed class TileHandler(
        Func<int, HttpStatusCode> status,
        Func<int, TileBody>? body = null,
        Action<Uri>? beforeBrokenResponse = null) : HttpMessageHandler
    {
        internal static readonly byte[] Tile = CreateTile();
        private int _count;

        public ConcurrentQueue<Uri?> Requests { get; } = new();

        /// <summary>壊れた内容を返した最後の要求。同時の要求では、受け付けた順と番号の順がそろわない</summary>
        public Uri? BrokenRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(request.RequestUri);
            var index = Interlocked.Increment(ref _count);
            var kind = body?.Invoke(index) ?? TileBody.Image;
            if (kind != TileBody.Image)
            {
                BrokenRequest = request.RequestUri;
                beforeBrokenResponse?.Invoke(request.RequestUri!);
            }

            byte[] content = kind switch
            {
                TileBody.NotAnImage => [1, 2, 3, 4],
                TileBody.Truncated => Tile[..(Tile.Length / 2)],
                TileBody.Empty => [],
                _ => Tile
            };
            return Task.FromResult(new HttpResponseMessage(status(index))
            {
                Content = new ByteArrayContent(content)
            });
        }

        private static byte[] CreateTile()
        {
            using var bitmap = new SKBitmap(256, 256);
            bitmap.Erase(TileColor);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    private sealed class RecordingVectorRenderer : IVectorMapRenderer
    {
        public ConcurrentQueue<VectorViewRequest> Requests { get; } = new();

        public Task<VectorViewResult> RenderViewAsync(VectorViewRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            using var bitmap = new SKBitmap(request.PixelWidth, request.PixelHeight);
            bitmap.Erase(TileColor);
            return Task.FromResult(new VectorViewResult(SKImage.FromBitmap(bitmap)));
        }
    }
}
