using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BruTile.Predefined;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
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
