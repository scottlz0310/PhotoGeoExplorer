using System;
using System.Collections.Generic;
using System.IO;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Styles;
using Mapsui.Widgets;
using PhotoGeoExplorer.Panes.Map;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class ObservingMapRendererTests
{
    [Fact]
    public void ConstructorRejectsNullArguments()
    {
        var inner = new FakeMapRenderer();
        Assert.Throws<ArgumentNullException>(() => new ObservingMapRenderer(null!, (_, _) => null));
        Assert.Throws<ArgumentNullException>(() => new ObservingMapRenderer(inner, null!));
    }

    [Fact]
    public void RenderInvokesCompletionCallbackStrictlyAfterInnerRenderCompletes()
    {
        var events = new List<string>();
        var inner = new FakeMapRenderer
        {
            OnRender = () => events.Add("inner-render")
        };
        var renderer = new ObservingMapRenderer(
            inner,
            (viewport, layers) =>
            {
                events.Add($"starting:{viewport.Width}x{viewport.Height}:{layers.Count}");
                return () => events.Add("completed");
            });

        using var layer = new MemoryLayer { Name = "bg" };
        using var renderService = new RenderService();
        renderer.Render(new object(), new Viewport(0, 0, 1, 0, 400, 300), [layer], [], renderService);

        Assert.Equal(["starting:400x300:1", "inner-render", "completed"], events);
    }

    [Fact]
    public void RenderDoesNotInvokeCompletionCallbackWhenInnerRenderThrows()
    {
        var completed = false;
        var inner = new FakeMapRenderer
        {
            OnRender = () => throw new InvalidOperationException("render failed")
        };
        var renderer = new ObservingMapRenderer(inner, (_, _) => () => completed = true);
        using var renderService = new RenderService();

        Assert.Throws<InvalidOperationException>(
            () => renderer.Render(new object(), new Viewport(0, 0, 1, 0, 400, 300), [], [], renderService));
        Assert.False(completed);
    }

    [Fact]
    public void RenderSkipsObserverCallbackForPartialDirtyRegionRender()
    {
        var observerCalled = false;
        var inner = new FakeMapRenderer();
        var renderer = new ObservingMapRenderer(
            inner,
            (_, _) =>
            {
                observerCalled = true;
                return () => { };
            });

        using var renderService = new RenderService();
        renderer.Render(
            new object(),
            new Viewport(0, 0, 1, 0, 400, 300),
            [],
            [],
            renderService,
            dirtyRegion: new MRect(0, 0, 10, 10));

        Assert.False(observerCalled);
        Assert.Equal(1, inner.RenderCount);
    }

    [Fact]
    public void DelegatesAllOtherRendererMembersToInnerRenderer()
    {
        var inner = new FakeMapRenderer();
        var renderer = new ObservingMapRenderer(inner, (_, _) => null);
        var viewport = new Viewport(0, 0, 1, 0, 200, 100);
        using var layer = new MemoryLayer { Name = "bg" };
        var feature = new PointFeature(0, 0);
        var style = new VectorStyle();
        using var renderService = new RenderService();

        renderer.UpdateDrawables(viewport, layer, renderService);
        _ = renderer.CreateDrawableForFeature(viewport, layer, feature, style, renderService);
        using var stream = renderer.RenderToBitmapStream(viewport, [layer], renderService);
        var hasWidgetRenderer = renderer.TryGetWidgetRenderer(typeof(IWidget), out var widgetRenderer);
        var hasStyleRenderer = renderer.TryGetStyleRenderer(typeof(VectorStyle), out var styleRenderer);
        var mapInfo = renderer.GetMapInfo(new ScreenPosition(10, 20), viewport, [layer], renderService, margin: 4);

        Assert.Equal(1, inner.UpdateDrawablesCount);
        Assert.Equal(1, inner.CreateDrawableCount);
        Assert.Equal(1, inner.RenderToBitmapStreamCount);
        Assert.False(hasWidgetRenderer);
        Assert.Null(widgetRenderer);
        Assert.False(hasStyleRenderer);
        Assert.Null(styleRenderer);
        Assert.Equal(1, inner.GetMapInfoCount);
        Assert.NotNull(mapInfo);
        Assert.NotNull(stream);
    }

    private sealed class FakeMapRenderer : IMapRenderer
    {
        public Action? OnRender { get; set; }

        public int RenderCount { get; private set; }

        public int UpdateDrawablesCount { get; private set; }

        public int CreateDrawableCount { get; private set; }

        public int RenderToBitmapStreamCount { get; private set; }

        public int GetMapInfoCount { get; private set; }

        public void UpdateDrawables(Viewport viewport, ILayer layer, RenderService renderService)
            => UpdateDrawablesCount++;

        public IDrawable? CreateDrawableForFeature(Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService)
        {
            CreateDrawableCount++;
            return null;
        }

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
            RenderCount++;
            OnRender?.Invoke();
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
        {
            RenderToBitmapStreamCount++;
            return new MemoryStream();
        }

        public bool TryGetWidgetRenderer(Type widgetType, out IWidgetRenderer? widgetRenderer)
        {
            widgetRenderer = null;
            return false;
        }

        public bool TryGetStyleRenderer(Type styleType, out IStyleRenderer? styleRenderer)
        {
            styleRenderer = null;
            return false;
        }

        public MapInfo GetMapInfo(
            ScreenPosition screenPosition,
            Viewport viewport,
            IEnumerable<ILayer> layers,
            RenderService renderService,
            int margin = 0)
        {
            GetMapInfoCount++;
            return new MapInfo(screenPosition, new MPoint(0, 0), viewport.Resolution);
        }
    }
}
