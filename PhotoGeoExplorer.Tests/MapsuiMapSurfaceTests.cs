using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapsuiMapSurfaceTests
{
    [Fact]
    public void ViewHasNoSizeUntilTheNavigatorHasOne()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);

        Assert.False(surface.View.HasSize);

        map.Navigator.SetSize(800, 600);

        var view = surface.View;
        Assert.True(view.HasSize);
        Assert.Equal(800, view.WidthDip);
        Assert.Equal(600, view.HeightDip);
    }

    [Fact]
    public void SetViewMovesTheNavigatorImmediatelyAndRaisesViewChanged()
    {
        using var map = new Map();
        map.Navigator.SetSize(800, 600);
        using var surface = new MapsuiMapSurface(map);
        var raised = 0;
        surface.ViewChanged += (_, _) => raised++;
        var centerX = WebMercator.FromLongitude(142.362436);
        var centerY = WebMercator.FromLatitude(43.768436);
        var resolution = WebMercator.ResolutionFromLogicalZoom(17.5);

        surface.SetView(centerX, centerY, resolution);

        var view = surface.View;
        Assert.Equal(centerX, view.CenterX, 1e-6);
        Assert.Equal(centerY, view.CenterY, 1e-6);
        Assert.Equal(resolution, view.Resolution, 1e-9);
        Assert.True(raised > 0);
    }

    [Fact]
    public void ViewChangedFollowsNavigatorSizeChanges()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        var raised = 0;
        surface.ViewChanged += (_, _) => raised++;

        map.Navigator.SetSize(640, 480);

        Assert.True(raised > 0);
    }

    [Fact]
    public void ConstructorAddsTheMarkerLayerOnTopOfTheExistingLayers()
    {
        using var map = new Map();
        using var background = new MemoryLayer { Name = "background" };
        map.Layers.Add(background);

        using var surface = new MapsuiMapSurface(map);

        Assert.Equal(["background", MapsuiMapSurface.MarkerLayerName], map.Layers.Select(layer => layer.Name).ToArray());
    }

    [Fact]
    public void ShowMarkerPlacesOneFeatureCarryingTheMarker()
    {
        using var map = new Map();
        map.Navigator.SetSize(800, 600);
        using var surface = new MapsuiMapSurface(map);
        var marker = CreateMarker(35.681236, 139.767125, @"C:\Photos\a.jpg", pinImagePath: null);

        surface.ShowMarker(marker, zoomLevel: 14);

        var feature = Assert.Single(MarkerLayer(map).Features);
        Assert.Same(marker, feature[MapsuiMapSurface.MarkerKey]);
        Assert.Same(marker, Assert.Single(surface.Markers));
    }

    [Fact]
    public void ShowMarkerCentersTheViewOnTheMarker()
    {
        using var map = new Map();
        map.Navigator.SetSize(800, 600);
        using var surface = new MapsuiMapSurface(map);

        surface.ShowMarker(CreateMarker(35.681236, 139.767125, @"C:\Photos\a.jpg", pinImagePath: null), zoomLevel: 14);

        var view = surface.View;
        Assert.Equal(WebMercator.FromLongitude(139.767125), view.CenterX, 1e-3);
        Assert.Equal(WebMercator.FromLatitude(35.681236), view.CenterY, 1e-3);
    }

    [Fact]
    public void ShowMarkerUsesTheImageStyleWhenAPinImageIsGiven()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);

        surface.ShowMarker(CreateMarker(35.0, 139.0, @"C:\Photos\a.jpg", @"C:\Pins\red_pin.png"), zoomLevel: 14);

        var feature = Assert.Single(MarkerLayer(map).Features);
        Assert.IsType<ImageStyle>(Assert.Single(feature.Styles));
    }

    [Fact]
    public void ShowMarkerUsesTheFallbackSymbolWhenNoPinImageIsGiven()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);

        surface.ShowMarker(CreateMarker(35.0, 139.0, @"C:\Photos\a.jpg", pinImagePath: null), zoomLevel: 14);

        var feature = Assert.Single(MarkerLayer(map).Features);
        Assert.IsType<SymbolStyle>(Assert.Single(feature.Styles));
    }

    [Fact]
    public void ShowMarkersPlacesAFeatureForEachMarkerAndFitsTheView()
    {
        using var map = new Map();
        map.Navigator.SetSize(800, 600);
        using var surface = new MapsuiMapSurface(map);
        var tokyo = CreateMarker(35.681236, 139.767125, @"C:\Photos\a.jpg", pinImagePath: null);
        var osaka = CreateMarker(34.702485, 135.495951, @"C:\Photos\b.jpg", pinImagePath: null);

        surface.ShowMarkers([tokyo, osaka]);

        Assert.Equal(2, MarkerLayer(map).Features.Count());
        Assert.Equal([tokyo, osaka], surface.Markers);
        var view = surface.View;
        var expectedCenterX = (WebMercator.FromLongitude(139.767125) + WebMercator.FromLongitude(135.495951)) / 2;
        Assert.Equal(expectedCenterX, view.CenterX, 1);
    }

    [Fact]
    public void ClearMarkersRemovesFeaturesAndMarkers()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        surface.ShowMarker(CreateMarker(35.0, 139.0, @"C:\Photos\a.jpg", pinImagePath: null), zoomLevel: 14);

        surface.ClearMarkers();

        Assert.Empty(MarkerLayer(map).Features);
        Assert.Empty(surface.Markers);
    }

    [Fact]
    public void TryGetGeoPositionConvertsTheViewCenterBackToTheCenterPosition()
    {
        using var map = new Map();
        map.Navigator.SetSize(800, 600);
        using var surface = new MapsuiMapSurface(map);
        surface.SetView(
            WebMercator.FromLongitude(142.362436),
            WebMercator.FromLatitude(43.768436),
            WebMercator.ResolutionFromLogicalZoom(16.0));

        var converted = surface.TryGetGeoPosition(400, 300, out var position);

        Assert.True(converted);
        Assert.Equal(43.768436, position.Latitude, 1e-6);
        Assert.Equal(142.362436, position.Longitude, 1e-6);
    }

    [Fact]
    public void LockPanStopsPanningAndRestorePanBringsBackThePreviousState()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        map.Navigator.PanLock = false;

        surface.LockPan();
        Assert.True(map.Navigator.PanLock);
        surface.RestorePan();

        Assert.False(map.Navigator.PanLock);
    }

    [Fact]
    public void RestorePanKeepsAPanLockThatWasAlreadyOnBeforeLocking()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        map.Navigator.PanLock = true;

        surface.LockPan();
        surface.LockPan();
        surface.RestorePan();

        Assert.True(map.Navigator.PanLock);
    }

    [Fact]
    public void RestorePanWithoutLockDoesNotTouchTheNavigator()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        map.Navigator.PanLock = true;

        surface.RestorePan();

        Assert.True(map.Navigator.PanLock);
    }

    [Fact]
    public void ShowSelectionRectangleAddsALayerAndClearRemovesIt()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        var layerCount = map.Layers.Count;

        surface.ShowSelectionRectangle(new GeoPoint(35.0, 139.0), new GeoPoint(36.0, 140.0));
        Assert.Equal(layerCount + 1, map.Layers.Count);

        // 2 回目は同じレイヤーを置き換える
        surface.ShowSelectionRectangle(new GeoPoint(35.0, 139.0), new GeoPoint(37.0, 141.0));
        Assert.Equal(layerCount + 1, map.Layers.Count);

        surface.ClearSelectionRectangle();
        Assert.Equal(layerCount, map.Layers.Count);
    }

    [Fact]
    public void ClearSelectionRectangleWithoutARectangleDoesNothing()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        var layerCount = map.Layers.Count;

        surface.ClearSelectionRectangle();

        Assert.Equal(layerCount, map.Layers.Count);
    }

    [Fact]
    public void MarkerHitTestReturnsNullWhenThereIsNothingToTest()
    {
        Assert.Null(MapsuiMarkerHitTest.FindMarker(null, null));
    }

    [Fact]
    public void ReplaceBackgroundPlacesTheBackgroundLayersBelowTheMarkerLayerAndAttaches()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        using var background = new FakeMapsuiBackground();

        surface.ReplaceBackground(background);

        Assert.Equal(["background", MapsuiMapSurface.MarkerLayerName], map.Layers.Select(layer => layer.Name).ToArray());
        Assert.Equal(1, background.AttachCount);
        Assert.Same(map, background.AttachedMap);
    }

    [Fact]
    public void ReplaceBackgroundRemovesThePreviousBackgroundLayers()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        using var first = new FakeMapsuiBackground();
        using var second = new FakeMapsuiBackground();
        second.Layers[0] = new MemoryLayer { Name = "second" };

        surface.ReplaceBackground(first);
        surface.ReplaceBackground(second);

        Assert.Equal(["second", MapsuiMapSurface.MarkerLayerName], map.Layers.Select(layer => layer.Name).ToArray());
    }

    [Fact]
    public void ReplaceBackgroundRejectsABackgroundThatIsNotAMapsuiBackground()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);

        Assert.Throws<ArgumentException>(() => surface.ReplaceBackground(new NonMapsuiBackground()));
    }

    [Fact]
    public void BackgroundReadsTheCurrentPixelDensityFromTheSurface()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        using var background = new FakeMapsuiBackground();
        surface.PixelDensity = 1.5;
        surface.ReplaceBackground(background);

        Assert.Equal(1.5, background.GetPixelDensity!());

        surface.PixelDensity = 2.0;

        Assert.Equal(2.0, background.GetPixelDensity());
    }

    [Fact]
    public void PixelDensityNotifiesTheBackgroundOnlyWhenItChanges()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        using var background = new FakeMapsuiBackground();
        surface.ReplaceBackground(background);

        surface.PixelDensity = 1.5;
        surface.PixelDensity = 1.5;
        surface.PixelDensity = 2.0;

        Assert.Equal(2, background.PixelDensityChangedCount);
    }

    [Fact]
    public void BeginMapRenderDelegatesToTheCurrentBackground()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        using var background = new FakeMapsuiBackground();
        Assert.Null(surface.BeginMapRender(map.Navigator.Viewport, map.Layers.ToArray(), map.RenderService));
        surface.ReplaceBackground(background);

        var completed = surface.BeginMapRender(map.Navigator.Viewport, map.Layers.ToArray(), map.RenderService);

        Assert.NotNull(completed);
        Assert.Equal(1, background.BeginMapRenderCount);
    }

    [Fact]
    public async Task RenderPngAsyncRendersTheExportLayersAndDisposesThem()
    {
        using var map = new Map();
        map.Navigator.SetSize(64, 48);
        using var surface = new MapsuiMapSurface(map);
        using var background = new FakeMapsuiBackground();
        surface.ReplaceBackground(background);

        using var stream = await surface.RenderPngAsync(1.5f, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(map.Navigator.Viewport, background.ExportViewport);
        Assert.Equal(1.5f, background.ExportPixelDensity);
        Assert.Equal(1, background.ExportDisposeCount);
        var signature = new byte[8];
        Assert.Equal(8, await stream.ReadAsync(signature).ConfigureAwait(true));
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], signature);
    }

    [Fact]
    public async Task RenderPngAsyncThrowsWhenTheBackgroundIsNotSet()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => surface.RenderPngAsync(1f, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task RenderPngAsyncAddsContextWhenTheRenderingFails()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        using var background = new FakeMapsuiBackground();
        surface.ReplaceBackground(background);

        // 表示面の大きさが未確定（0）の視点では描画できない
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => surface.RenderPngAsync(1f, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal("Failed to render the current map viewport as PNG.", exception.Message);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    private sealed class NonMapsuiBackground : IMapBackground
    {
        public MapTileSourceType SourceType => MapTileSourceType.OpenStreetMap;

        public string AttributionText => string.Empty;

        public bool IsBusy => false;

        public event EventHandler? BusyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? FrameRendered
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }

    private static MemoryLayer MarkerLayer(Map map)
        => map.Layers.OfType<MemoryLayer>().Single(layer => layer.Name == MapsuiMapSurface.MarkerLayerName);

    private static MapMarker CreateMarker(double latitude, double longitude, string filePath, string? pinImagePath)
    {
        var item = new PhotoItem(filePath, 100, DateTimeOffset.UtcNow, isFolder: false);
        var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, latitude, longitude);
        return new MapMarker(latitude, longitude, item, metadata, pinImagePath);
    }
}
