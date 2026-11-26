using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
            request => MapsuiMapFactory.CreateBackground(request, Path.Combine(root, (calls++).ToString(System.Globalization.CultureInfo.InvariantCulture))),
            new HttpClient(handler, disposeHandler: false));
#pragma warning restore CA2000
    }

    private static async Task<SKBitmap> RenderAsync(MapsuiMapImageExporter exporter, MapImageExportRequest request)
    {
        using var stream = await exporter.RenderPngAsync(request, CancellationToken.None).ConfigureAwait(true);
        return SKBitmap.Decode(stream);
    }

    private sealed class TileHandler(Func<int, HttpStatusCode> status) : HttpMessageHandler
    {
        private static readonly byte[] Tile = CreateTile();
        private int _count;

        public ConcurrentQueue<Uri?> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(status(Interlocked.Increment(ref _count)))
            {
                Content = new ByteArrayContent(Tile)
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
