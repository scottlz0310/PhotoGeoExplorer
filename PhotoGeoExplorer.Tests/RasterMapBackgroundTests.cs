using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using BruTile.Predefined;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Tiling.Extensions;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.E2E;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class RasterMapBackgroundTests
{
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

        Assert.Equal(0, raised);
        Assert.Equal(1, layer.DisposeCount);
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
        var targetLevelErrorNotified = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var layer = new TileLayer(
            tileSource,
            fetchTileAsFeature: tile =>
            {
                if (Volatile.Read(ref failTargetLevel) == 1 && tile.Index.Level >= targetLevel)
                {
                    return Task.FromException<IFeature?>(new HttpRequestException("Simulated partial failure."));
                }

                return Task.FromResult<IFeature?>(CreateDummyRasterFeature(tile));
            });
        layer.DataChanged += (_, args) =>
        {
            if (Volatile.Read(ref failTargetLevel) == 1 && args.Error is not null)
            {
                targetLevelErrorNotified.TrySetResult(args.Error);
            }
        };

        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        using var map = CreateMapWithLayer(layer, 512, 512);
        var controller = new MapViewController();

        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) => controller.NotifyFrameRendered();

        controller.Attach(map, MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);

        // 先に低ズーム（z10）のタイルをキャッシュに入れておく
        await controller.SetViewAsync(new MapViewTarget(43.768436, 142.362436, 10.0)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy && controller.CurrentStatus.RenderedSeq >= 1).ConfigureAwait(true);
        var seqAfterLowZoom = controller.CurrentStatus.RenderedSeq;

        // z16 へ移動し、z16 タイル取得をすべて失敗させる（z10 のフォールバックタイルだけが存在する状態）
        Volatile.Write(ref failTargetLevel, 1);
        var infoLogs = new List<string>();
        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 16.0)).ConfigureAwait(true);

        _ = await targetLevelErrorNotified.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);

        // フォールバックタイルだけでは採番も完了ログも行われない
        Assert.False(background.IsBusy);
        Assert.Equal(seqAfterLowZoom, controller.CurrentStatus.RenderedSeq);
        Assert.DoesNotContain(infoLogs, line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartupApplierEmitsCompletionOnceWhenAllRasterViewportTilesSucceedAsync()
    {
        var tileSource = KnownTileSources.Create(KnownTileSource.OpenStreetMap);
        using var layer = new TileLayer(
            tileSource,
            fetchTileAsFeature: tile => Task.FromResult<IFeature?>(CreateDummyRasterFeature(tile)));
        using var background = new RasterMapBackground(layer, MapTileSourceType.OpenStreetMap);
        using var map = CreateMapWithLayer(layer, 512, 512);
        var controller = new MapViewController();

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

        var statusLine = await statusLogged.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);

        List<string> statusLines;
        lock (infoLogs)
        {
            statusLines = infoLogs.FindAll(line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
        }

        Assert.Single(statusLines);
        Assert.Equal(statusLine, statusLines[0]);
        Assert.False(background.IsBusy);
        Assert.True(controller.CurrentStatus.RenderedSeq >= 1);
        Assert.Contains("\"styleZoom\":16", statusLine, StringComparison.Ordinal);
    }

    private static Mapsui.Map CreateMapWithLayer(TileLayer layer, double width, double height)
    {
        var map = new Mapsui.Map();
        map.Layers.Add(layer);
        map.Navigator.SetSize(width, height);
        return map;
    }

    private static RasterFeature CreateDummyRasterFeature(TileInfo tile)
        => new(new MRaster([1, 2, 3, 4], tile.Extent.ToMRect()));

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

    private sealed class TestExportLayers(ILayer[] layers) : IMapExportLayers
    {
        public System.Collections.Generic.IReadOnlyList<ILayer> Layers { get; } = layers;

        public void Dispose()
        {
        }
    }
}
