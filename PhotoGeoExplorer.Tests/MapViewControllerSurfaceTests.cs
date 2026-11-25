using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// <see cref="MapViewController"/> を、地図ライブラリなしの偽の表示面で検証する。
/// </summary>
public sealed class MapViewControllerSurfaceTests
{
    [Fact]
    public async Task SetViewAsyncPassesWebMercatorCenterAndResolutionToTheSurface()
    {
        var surface = new FakeMapSurface { View = SizedView(800, 600) };
        var controller = CreateController(surface);

        var result = await controller.SetViewAsync(new MapViewTarget(43.768436, 142.362436, 16.0));

        Assert.True(result.Succeeded);
        var call = Assert.Single(surface.SetViewCalls);
        Assert.Equal(WebMercator.FromLongitude(142.362436), call.CenterX, 1e-6);
        Assert.Equal(WebMercator.FromLatitude(43.768436), call.CenterY, 1e-6);
        Assert.Equal(WebMercator.ResolutionFromLogicalZoom(16.0), call.Resolution, 1e-9);
    }

    [Fact]
    public async Task SetViewAsyncBeforeTheSurfaceHasSizeIsAppliedWhenTheViewChanges()
    {
        var surface = new FakeMapSurface();
        var controller = CreateController(surface);

        var result = await controller.SetViewAsync(new MapViewTarget(35.681236, 139.767125, 14.0));

        Assert.True(result.Succeeded);
        Assert.False(result.Status.HasValidViewport);
        Assert.Single(surface.SetViewCalls);

        surface.View = SizedView(640, 480);
        surface.RaiseViewChanged();

        Assert.Equal(2, surface.SetViewCalls.Count);
        Assert.True(controller.CurrentStatus.HasValidViewport);
    }

    [Fact]
    public void ViewChangeUpdatesTheStatusFromTheSurfaceView()
    {
        var surface = new FakeMapSurface { View = SizedView(500, 400) };
        var controller = CreateController(surface);
        MapViewStatus? observed = null;
        controller.StatusChanged += (_, status) => observed = status;

        surface.View = new MapViewState(
            HasSize: true,
            CenterX: WebMercator.FromLongitude(142.36),
            CenterY: WebMercator.FromLatitude(43.76),
            Resolution: WebMercator.ResolutionFromLogicalZoom(17.2),
            WidthDip: 500,
            HeightDip: 400);
        surface.RaiseViewChanged();

        Assert.NotNull(observed);
        Assert.Equal(17.2, observed.Zoom!.Value, 1e-6);
        Assert.Equal(43.76, observed.Center!.Latitude, 1e-6);
        Assert.Equal(142.36, observed.Center.Longitude, 1e-6);
        Assert.Equal(500, observed.ViewportSize.PhysicalWidth);
        Assert.Equal(400, observed.ViewportSize.PhysicalHeight);
    }

    [Fact]
    public void DetachStopsListeningToTheSurface()
    {
        var surface = new FakeMapSurface { View = SizedView(500, 400) };
        var controller = CreateController(surface);
        var notifications = 0;
        controller.Detach();
        controller.StatusChanged += (_, _) => notifications++;

        surface.View = SizedView(800, 600);
        surface.RaiseViewChanged();

        Assert.Equal(0, notifications);
        Assert.False(controller.CurrentStatus.HasValidViewport);
        Assert.Equal(0, surface.SubscriberCount);
    }

    [Fact]
    public void AttachingAnotherSurfaceReleasesThePreviousOne()
    {
        var first = new FakeMapSurface { View = SizedView(500, 400) };
        var second = new FakeMapSurface { View = SizedView(300, 200) };
        var controller = CreateController(first);

        controller.Attach(second, MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, string.Empty, 1.0, isBusy: false);

        Assert.Equal(0, first.SubscriberCount);
        Assert.Equal(1, second.SubscriberCount);
        Assert.Equal(300, controller.CurrentStatus.ViewportSize.PhysicalWidth);
    }

    private static MapViewController CreateController(FakeMapSurface surface)
    {
        var controller = new MapViewController();
        controller.Attach(surface, MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector, "OpenFreeMap", 1.0, isBusy: false);
        return controller;
    }

    private static MapViewState SizedView(double width, double height)
        => new(HasSize: true, CenterX: 0, CenterY: 0, Resolution: 10, WidthDip: width, HeightDip: height);

    private sealed class FakeMapSurface : IMapSurface
    {
        private EventHandler? _viewChanged;

        internal System.Collections.Generic.List<(double CenterX, double CenterY, double Resolution)> SetViewCalls { get; } = [];

        internal int SubscriberCount => _viewChanged?.GetInvocationList().Length ?? 0;

        public MapViewState View { get; set; }

        public event EventHandler? ViewChanged
        {
            add => _viewChanged += value;
            remove => _viewChanged -= value;
        }

        public void SetView(double centerX, double centerY, double resolution)
        {
            SetViewCalls.Add((centerX, centerY, resolution));
        }

        internal void RaiseViewChanged() => _viewChanged?.Invoke(this, EventArgs.Empty);
    }
}
