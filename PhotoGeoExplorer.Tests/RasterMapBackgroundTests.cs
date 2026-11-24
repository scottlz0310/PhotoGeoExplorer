using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using BruTile.Predefined;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using Mapsui.Tiling.Extensions;
using Mapsui.Tiling.Layers;
using Mapsui.Widgets;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.E2E;
using PhotoGeoExplorer.Services.MapRendering;
using SkiaSharp;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class RasterMapBackgroundTests
{
    private static readonly byte[] ValidTilePngBytes = CreateSolidPngBytes(SKColors.ForestGreen);

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, MapAttribution.OpenStreetMapText)]
    [InlineData(MapTileSourceType.EsriWorldImagery, MapAttribution.EsriText)]
    [InlineData(MapTileSourceType.MapboxSatellite, MapAttribution.MapboxText)]
    internal void ExposesLayerAndAttributionText(MapTileSourceType sourceType, string expectedText)
    {
        using var layer = CreateLayer();
        using var background = new RasterMapBackground(layer, sourceType);

        Assert.Same(layer, Assert.Single(background.Layers));
        Assert.Equal(expectedText, background.AttributionText);
    }

    [Fact]
    public void ForwardsLayerBusyChanges()
    {
        using var layer = new BusyTileLayer();
        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        var raised = 0;
        background.BusyChanged += (_, _) => raised++;

        layer.SetBusy(true);

        Assert.True(background.IsBusy);
        Assert.Equal(1, raised);

        layer.SetBusy(false);

        Assert.False(background.IsBusy);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void StopsForwardingAndDisposesLayerWhenDisposed()
    {
#pragma warning disable CA2000 // 所有権は background に移り、下の Dispose で破棄する。
        var layer = new BusyTileLayer();
#pragma warning restore CA2000
        var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        var raised = 0;
        background.BusyChanged += (_, _) => raised++;

        background.Dispose();
        background.Dispose();
        layer.SetBusy(true);

        using var renderService = new RenderService();
        Assert.Equal(0, raised);
        Assert.Equal(1, layer.DisposeCount);
        Assert.Null(background.BeginMapRender(new Viewport(0, 0, 1, 0, 512, 512), [layer], renderService));
    }

    [Fact]
    public async Task ExportLayersAreTheDisplayedLayersAndDisposingThemKeepsTheLayersAlive()
    {
        using var layer = new BusyTileLayer();
        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);

        var export = await background.CreateExportLayersAsync(new Viewport(0, 0, 1, 0, 100, 50), 1f, CancellationToken.None).ConfigureAwait(true);
        export.Dispose();

        Assert.Same(layer, Assert.Single(export.Layers));
        Assert.Equal(0, layer.DisposeCount);
    }

    [Fact]
    public async Task ExportLayersThrowsWhenCanceled()
    {
        using var layer = CreateLayer();
        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => background.CreateExportLayersAsync(new Viewport(0, 0, 1, 0, 100, 50), 1f, cts.Token)).ConfigureAwait(true);
    }

    [Fact]
    public void ComposeReplacesBackgroundLayersAndKeepsOtherLayersAboveInOrder()
    {
        using var backgroundLayer = new BusyTileLayer();
        using var exportLayer = new MemoryLayer { Name = "export" };
        using var marker = new MemoryLayer { Name = "marker" };
        using var overlay = new MemoryLayer { Name = "overlay" };
        using var background = new RasterMapBackground(backgroundLayer, MapTileSourceType.OpenStreetMap);
        using var export = new TestExportLayers([exportLayer]);

        var layers = MapExportLayers.Compose([backgroundLayer, marker, overlay], background, export);

        Assert.Equal(new ILayer[] { exportLayer, marker, overlay }, layers.ToArray());
    }

    [Fact]
    public void ComposeWithBorrowedLayersEqualsTheMapLayers()
    {
        using var backgroundLayer = new BusyTileLayer();
        using var marker = new MemoryLayer { Name = "marker" };
        using var background = new RasterMapBackground(backgroundLayer, MapTileSourceType.OpenStreetMap);
        using var export = MapExportLayers.Borrowed(background.Layers);
        ILayer[] mapLayers = [backgroundLayer, marker];

        var layers = MapExportLayers.Compose(mapLayers, background, export);

        Assert.Equal(mapLayers, layers.ToArray());
    }

    [Fact]
    public async Task StartupApplierDoesNotEmitCompletionOrAdvanceSeqWhenRasterTileFetchFailsAsync()
    {
        var tileSource = KnownTileSources.Create(KnownTileSource.OpenStreetMap);
        var errorNotified = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var layer = new TileLayer(
            tileSource,
            fetchTileAsFeature: _ => Task.FromException<IFeature?>(new HttpRequestException("Simulated network error.")));
        layer.DataChanged += (_, args) =>
        {
            if (args.Error is not null)
            {
                errorNotified.TrySetResult(args.Error);
            }
        };

        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        using var map = CreateMapWithLayer(layer, 512, 512);
        var controller = new MapViewController();
        var renderer = new ObservingMapRenderer(new MapRenderer(), background.BeginMapRender);
        var frameRenderedCount = 0;

        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) =>
        {
            Interlocked.Increment(ref frameRenderedCount);
            controller.NotifyFrameRendered();
        };

        controller.Attach(map, MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);

        var infoLogs = new List<string>();
        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 16.0)).ConfigureAwait(true);

        _ = await errorNotified.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);
        _ = RenderToSkiaCenterPixel(renderer, map);

        Assert.False(background.IsBusy);
        Assert.Equal(0, Volatile.Read(ref frameRenderedCount));
        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);
        Assert.DoesNotContain(infoLogs, line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[0])]
    public async Task StartupApplierDoesNotEmitCompletionOrAdvanceSeqWhenRasterTilesAreUndecodableWithRealSkiaRendererAsync(byte[] invalidPayload)
    {
        var tileSource = KnownTileSources.Create(KnownTileSource.OpenStreetMap);
        var dataChangedReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var layer = new TileLayer(
            tileSource,
            fetchTileAsFeature: tile => Task.FromResult<IFeature?>(new RasterFeature(new MRaster(invalidPayload, tile.Extent.ToMRect()))));
        layer.DataChanged += (_, args) =>
        {
            if (args.Error is null)
            {
                dataChangedReceived.TrySetResult();
            }
        };

        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        using var map = CreateMapWithLayer(layer, 512, 512);
        var controller = new MapViewController();
        var renderer = new ObservingMapRenderer(new MapRenderer(), background.BeginMapRender);
        var frameRenderedCount = 0;

        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) =>
        {
            Interlocked.Increment(ref frameRenderedCount);
            controller.NotifyFrameRendered();
        };

        controller.Attach(map, MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);

        var infoLogs = new List<string>();
        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 16.0)).ConfigureAwait(true);

        await dataChangedReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy && HasAllViewportFeatures(layer, map.Navigator.Viewport)).ConfigureAwait(true);

        // 実 Skia レンダラーで描画してもデコード不能タイルは描かれず白地のままで、完了通知も採番も行われない
        var centerPixel = RenderToSkiaCenterPixel(renderer, map);

        Assert.Equal(SKColors.White, centerPixel);
        Assert.False(background.IsBusy);
        Assert.Equal(0, Volatile.Read(ref frameRenderedCount));
        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);
        Assert.DoesNotContain(infoLogs, line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartupApplierDoesNotEmitCompletionWhenOnlyPartialOrFallbackRasterTilesExistAsync()
    {
        var tileSource = KnownTileSources.Create(KnownTileSource.OpenStreetMap);
        var failTargetLevel = 0;
        var targetLevel = 16;
        var lowZoomDataChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetLevelErrorNotified = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var layer = new TileLayer(
            tileSource,
            fetchTileAsFeature: tile =>
            {
                if (Volatile.Read(ref failTargetLevel) == 1 && tile.Index.Level >= targetLevel)
                {
                    return Task.FromException<IFeature?>(new HttpRequestException("Simulated partial failure."));
                }

                return Task.FromResult<IFeature?>(CreateValidRasterFeature(tile));
            });
        layer.DataChanged += (_, args) =>
        {
            if (Volatile.Read(ref failTargetLevel) == 0 && args.Error is null)
            {
                lowZoomDataChanged.TrySetResult();
            }

            if (Volatile.Read(ref failTargetLevel) == 1 && args.Error is not null)
            {
                targetLevelErrorNotified.TrySetResult(args.Error);
            }
        };

        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        using var map = CreateMapWithLayer(layer, 512, 512);
        var controller = new MapViewController();
        var renderer = new ObservingMapRenderer(new MapRenderer(), background.BeginMapRender);

        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) => controller.NotifyFrameRendered();

        controller.Attach(map, MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);

        // 先に低ズーム（z10）のタイルをキャッシュに入れて実描画を完了させておく
        await controller.SetViewAsync(new MapViewTarget(43.768436, 142.362436, 10.0)).ConfigureAwait(true);
        await lowZoomDataChanged.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy && HasAllViewportFeatures(layer, map.Navigator.Viewport)).ConfigureAwait(true);
        var lowZoomPixel = RenderToSkiaCenterPixel(renderer, map);
        Assert.Equal(SKColors.ForestGreen, lowZoomPixel);
        Assert.Equal(1, controller.CurrentStatus.RenderedSeq);
        var seqAfterLowZoom = controller.CurrentStatus.RenderedSeq;

        // z16 へ移動し、z16 タイル取得をすべて失敗させる（z10 のフォールバックタイルだけが存在する状態）
        Volatile.Write(ref failTargetLevel, 1);
        var infoLogs = new List<string>();
        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 16.0)).ConfigureAwait(true);

        _ = await targetLevelErrorNotified.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);
        _ = RenderToSkiaCenterPixel(renderer, map);

        // フォールバックタイルだけでは実描画しても採番・完了ログは行われない
        Assert.False(background.IsBusy);
        Assert.Equal(seqAfterLowZoom, controller.CurrentStatus.RenderedSeq);
        Assert.DoesNotContain(infoLogs, line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartupApplierWaitsForMapRenderEvenAfterAllRasterTilesAreFetchedAndNotBusyAsync()
    {
        var tileSource = KnownTileSources.Create(KnownTileSource.OpenStreetMap);
        var dataChangedReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var layer = new TileLayer(
            tileSource,
            fetchTileAsFeature: tile => Task.FromResult<IFeature?>(CreateValidRasterFeature(tile)));
        layer.DataChanged += (_, args) =>
        {
            if (args.Error is null)
            {
                dataChangedReceived.TrySetResult();
            }
        };

        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        using var map = CreateMapWithLayer(layer, 512, 512);
        var controller = new MapViewController();
        long? seqDuringInnerRender = null;
        var innerRenderer = new SpySkiaMapRenderer(
            new MapRenderer(),
            () => seqDuringInnerRender = controller.CurrentStatus.RenderedSeq);
        var renderer = new ObservingMapRenderer(innerRenderer, background.BeginMapRender);

        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) => controller.NotifyFrameRendered();

        controller.Attach(map, MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);

        var infoLogs = new List<string>();
        var statusLogged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var applier = new E2EMapStartupApplier(
            controller,
            line =>
            {
                lock (infoLogs)
                {
                    infoLogs.Add(line);
                }

                if (line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal))
                {
                    statusLogged.TrySetResult(line);
                }
            });

        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 16.0)).ConfigureAwait(true);

        // 全タイルがキャッシュに入り Busy が false・DataChanged 到達済みでも、描画反映前は完了通知しない
        await dataChangedReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy && HasAllViewportFeatures(layer, map.Navigator.Viewport)).ConfigureAwait(true);

        Assert.False(background.IsBusy);
        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);
        Assert.False(statusLogged.Task.IsCompleted);

        // 対象外レイヤー・古いビューポート・部分再描画では完了通知されない
        using var surface = SKSurface.Create(new SKImageInfo(512, 512));
        using var otherLayer = new MemoryLayer { Name = "other" };
        renderer.Render(surface.Canvas, map.Navigator.Viewport, [otherLayer], [], map.RenderService, Color.White);
        var staleViewport = new Viewport(0, 0, map.Navigator.Viewport.Resolution, 0, 512, 512);
        renderer.Render(surface.Canvas, staleViewport, map.Layers, [], map.RenderService, Color.White);
        renderer.Render(
            surface.Canvas,
            map.Navigator.Viewport,
            map.Layers,
            [],
            map.RenderService,
            Color.White,
            dirtyRegion: new MRect(0, 0, 10, 10));

        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);
        Assert.False(statusLogged.Task.IsCompleted);

        // 対象ビューポートの全画面実描画が完了した直後に、タイルが実際に描画され採番・完了 JSON ログ出力される
        var centerPixel = RenderToSkiaCenterPixel(renderer, map);

        var statusLine = await statusLogged.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        Assert.Equal(SKColors.ForestGreen, centerPixel);
        Assert.Equal(0, seqDuringInnerRender);
        Assert.Equal(1, controller.CurrentStatus.RenderedSeq);

        // 同じ状態のまま再描画されても重複採番されない
        _ = RenderToSkiaCenterPixel(renderer, map);
        Assert.Equal(1, controller.CurrentStatus.RenderedSeq);

        List<string> statusLines;
        lock (infoLogs)
        {
            statusLines = infoLogs.FindAll(line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
        }

        Assert.Single(statusLines);
        Assert.Equal(statusLine, statusLines[0]);
        Assert.Contains("\"renderedSeq\":1", statusLine, StringComparison.Ordinal);
        Assert.Contains("\"styleZoom\":16", statusLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderInterruptedByViewportChangeDoesNotNotifyStaleFrameCompletionAsync()
    {
        var tileSource = KnownTileSources.Create(KnownTileSource.OpenStreetMap);
        var dataChangedReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var layer = new TileLayer(
            tileSource,
            fetchTileAsFeature: tile => Task.FromResult<IFeature?>(CreateValidRasterFeature(tile)));
        layer.DataChanged += (_, args) =>
        {
            if (args.Error is null)
            {
                dataChangedReceived.TrySetResult();
            }
        };

        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        using var map = CreateMapWithLayer(layer, 512, 512);
        var controller = new MapViewController();
        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) => controller.NotifyFrameRendered();
        controller.Attach(map, MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);

        await controller.SetViewAsync(new MapViewTarget(43.768436, 142.362436, 16.0)).ConfigureAwait(true);
        await dataChangedReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy && HasAllViewportFeatures(layer, map.Navigator.Viewport)).ConfigureAwait(true);

        // 描画中に別ビューポートへ移動した場合、旧ビューポート描画の完了コールバックは見送られる
        var innerRenderer = new SpySkiaMapRenderer(
            new MapRenderer(),
            () => map.Navigator.CenterOnAndZoomTo(new MPoint(0, 0), WebMercator.ResolutionFromLogicalZoom(12.0), duration: 0));
        var renderer = new ObservingMapRenderer(innerRenderer, background.BeginMapRender);

        _ = RenderToSkiaCenterPixel(renderer, map);

        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);
    }

    private static Mapsui.Map CreateMapWithLayer(TileLayer layer, double width, double height)
    {
        var map = new Mapsui.Map();
        map.Layers.Add(layer);
        map.Navigator.SetSize(width, height);
        return map;
    }

    private static SKColor RenderToSkiaCenterPixel(ObservingMapRenderer renderer, Mapsui.Map map)
    {
        var width = (int)map.Navigator.Viewport.Width;
        var height = (int)map.Navigator.Viewport.Height;
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        renderer.Render(surface.Canvas, map.Navigator.Viewport, map.Layers, map.Widgets, map.RenderService, Color.White);
        using var snapshot = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(snapshot);
        return bitmap.GetPixel(width / 2, height / 2);
    }

    private static bool HasAllViewportFeatures(TileLayer layer, Viewport viewport)
    {
        var schema = layer.TileSource?.Schema;
        var extent = viewport.ToExtent();
        if (schema is null || extent is null)
        {
            return false;
        }

        var clipped = new Extent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY).Intersect(schema.Extent);
        var levelId = BruTile.Utilities.GetNearestLevel(schema.Resolutions, viewport.Resolution);
        var requiredCount = schema.GetTileInfos(clipped, levelId).Count();
        var actualCount = layer.GetFeatures(extent, viewport.Resolution).OfType<RasterFeature>().Count();
        return requiredCount > 0 && actualCount >= requiredCount;
    }

    private static RasterFeature CreateValidRasterFeature(TileInfo tile)
        => new(new MRaster(ValidTilePngBytes, tile.Extent.ToMRect()));

    private static byte[] CreateSolidPngBytes(SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(color);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "条件が満たされないまま待ち時間を超えた。");
            await Task.Delay(10).ConfigureAwait(true);
        }
    }

    private static TileLayer CreateLayer()
        => new(KnownTileSources.Create(KnownTileSource.OpenStreetMap));

    private sealed class BusyTileLayer : TileLayer
    {
        public BusyTileLayer()
            : base(KnownTileSources.Create(KnownTileSource.OpenStreetMap))
        {
        }

        public int DisposeCount { get; private set; }

        public void SetBusy(bool busy) => Busy = busy;

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private sealed class SpySkiaMapRenderer(MapRenderer inner, Action onBeforeInnerRender) : IMapRenderer
    {
        public void UpdateDrawables(Viewport viewport, ILayer layer, RenderService renderService)
            => inner.UpdateDrawables(viewport, layer, renderService);

        public IDrawable? CreateDrawableForFeature(Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService)
            => inner.CreateDrawableForFeature(viewport, layer, feature, style, renderService);

        public void Render(
            object target,
            Viewport viewport,
            IEnumerable<ILayer> layers,
            IEnumerable<IWidget> widgets,
            RenderService renderService,
            Color? background = null,
            MRect? dirtyRegion = null,
            CoordinateSpace coordinateSpace = CoordinateSpace.World)
        {
            onBeforeInnerRender();
            inner.Render(target, viewport, layers, widgets, renderService, background, dirtyRegion, coordinateSpace);
        }

        public MemoryStream RenderToBitmapStream(
            Viewport viewport,
            IEnumerable<ILayer> layers,
            RenderService renderService,
            Color? background = null,
            float pixelDensity = 1,
            IEnumerable<IWidget>? widgets = null,
            RenderFormat renderFormat = RenderFormat.Png,
            int quality = 100)
            => inner.RenderToBitmapStream(viewport, layers, renderService, background, pixelDensity, widgets, renderFormat, quality);

        public bool TryGetWidgetRenderer(Type widgetType, out IWidgetRenderer? widgetRenderer)
            => inner.TryGetWidgetRenderer(widgetType, out widgetRenderer);

        public bool TryGetStyleRenderer(Type styleType, out IStyleRenderer? styleRenderer)
            => inner.TryGetStyleRenderer(styleType, out styleRenderer);

        public MapInfo GetMapInfo(ScreenPosition screenPosition, Viewport viewport, IEnumerable<ILayer> layers, RenderService renderService, int margin = 0)
            => inner.GetMapInfo(screenPosition, viewport, layers, renderService, margin);
    }

    private sealed class TestExportLayers(ILayer[] layers) : IMapExportLayers
    {
        public System.Collections.Generic.IReadOnlyList<ILayer> Layers { get; } = layers;

        public void Dispose()
        {
        }
    }
}
