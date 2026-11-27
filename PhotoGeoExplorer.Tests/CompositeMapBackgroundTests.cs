// 子の背景は、テスト対象の合成の背景が所有して破棄する
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class CompositeMapBackgroundTests
{
    [Fact]
    public void LayersStackTheRoadsAboveTheImageryAndAttributionNamesBoth()
    {
        using var background = Create(out var imagery, out var overlay);

        Assert.Equal([imagery.Layer, overlay.Layer], background.Layers);
        Assert.Equal(MapTileSourceType.EsriWorldImageryRoads, background.SourceType);
        Assert.Equal(
            "Source: Esri, Vantor, Earthstar Geographics, and the GIS User Community OpenFreeMap © OpenMapTiles Data from OpenStreetMap",
            background.AttributionText);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void IsBusyWhileEitherBackgroundIsBusy(bool imageryBusy, bool overlayBusy, bool expected)
    {
        using var background = Create(out var imagery, out var overlay);
        var changes = 0;
        background.BusyChanged += (_, _) => changes++;

        imagery.SetBusy(imageryBusy);
        overlay.SetBusy(overlayBusy);

        Assert.Equal(expected, background.IsBusy);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FrameRenderedWaitsForBothBackgrounds(bool imageryFirst)
    {
        using var background = Create(out var imagery, out var overlay);
        using var map = new Mapsui.Map();
        background.Attach(map, () => 1.0);
        var rendered = 0;
        background.FrameRendered += (_, _) => rendered++;
        var (first, second) = imageryFirst ? (imagery, overlay) : (overlay, imagery);

        first.RaiseFrameRendered();
        Assert.Equal(0, rendered);
        second.RaiseFrameRendered();

        Assert.Equal(1, rendered);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FrameRenderedForgetsTheEarlierViewportAndDensity(bool viewportChanges)
    {
        using var background = Create(out var imagery, out var overlay);
        using var map = new Mapsui.Map();
        map.Navigator.SetSize(400, 300);
        background.Attach(map, () => 1.0);
        var rendered = 0;
        background.FrameRendered += (_, _) => rendered++;
        imagery.RaiseFrameRendered();

        if (viewportChanges)
        {
            map.Navigator.CenterOnAndZoomTo(new MPoint(1000, 1000), 4.0);
        }
        else
        {
            background.OnPixelDensityChanged();
        }

        overlay.RaiseFrameRendered();
        Assert.Equal(0, rendered);
        imagery.RaiseFrameRendered();
        Assert.Equal(1, rendered);
    }

    [Fact]
    public void AttachAndDensityChangesReachBothBackgroundsAndRenderCallbacksAreCombined()
    {
        using var background = Create(out var imagery, out var overlay);
        using var map = new Mapsui.Map();
        background.Attach(map, () => 1.5);
        background.OnPixelDensityChanged();

        Assert.Same(map, imagery.AttachedMap);
        Assert.Same(map, overlay.AttachedMap);
        Assert.Equal(1, imagery.DensityChanges);
        Assert.Equal(1, overlay.DensityChanges);
        Assert.Throws<InvalidOperationException>(() => background.Attach(map, () => 1.0));
        Assert.Null(background.BeginMapRender(map.Navigator.Viewport, [], map.RenderService));

        var completed = new List<string>();
        imagery.RenderCallback = () => completed.Add("imagery");
        overlay.RenderCallback = () => completed.Add("overlay");
        background.BeginMapRender(map.Navigator.Viewport, [], map.RenderService)!.Invoke();

        Assert.Equal(["imagery", "overlay"], completed);
    }

    [Fact]
    public void DisposeReleasesBothBackgroundsOnceAndStopsForwarding()
    {
        var background = Create(out var imagery, out var overlay);
        var changes = 0;
        background.BusyChanged += (_, _) => changes++;
        background.FrameRendered += (_, _) => changes++;

        background.Dispose();
        background.Dispose();
        imagery.SetBusy(true);
        imagery.RaiseFrameRendered();
        overlay.RaiseFrameRendered();

        Assert.Equal(1, imagery.DisposeCount);
        Assert.Equal(1, overlay.DisposeCount);
        Assert.Equal(0, changes);
    }

    private static CompositeMapBackground Create(out FakeBackground imagery, out FakeBackground overlay)
    {
        imagery = new FakeBackground("imagery");
        overlay = new FakeBackground("overlay");
        return new CompositeMapBackground(imagery, overlay, MapTileSourceType.EsriWorldImageryRoads);
    }

    private sealed class FakeBackground(string name) : IMapsuiBackground
    {
        public MemoryLayer Layer { get; } = new(name);

        public IReadOnlyList<ILayer> Layers => [Layer];

        public MapTileSourceType SourceType => MapTileSourceType.OpenStreetMap;

        public string AttributionText => name;

        public bool IsBusy { get; private set; }

        public Mapsui.Map? AttachedMap { get; private set; }

        public int DensityChanges { get; private set; }

        public int DisposeCount { get; private set; }

        public Action? RenderCallback { get; set; }

        public event EventHandler? BusyChanged;

        public event EventHandler? FrameRendered;

        public void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseFrameRendered() => FrameRendered?.Invoke(this, EventArgs.Empty);

        public void Attach(Mapsui.Map map, Func<double> getPixelDensity) => AttachedMap = map;

        public void OnPixelDensityChanged() => DensityChanges++;

        public Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService)
            => RenderCallback;

        public void Dispose()
        {
            DisposeCount++;
            Layer.Dispose();
        }
    }
}
