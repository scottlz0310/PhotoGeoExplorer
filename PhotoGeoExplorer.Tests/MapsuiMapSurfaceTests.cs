using Mapsui;
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
        var surface = new MapsuiMapSurface(map);

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
        var surface = new MapsuiMapSurface(map);
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
        var surface = new MapsuiMapSurface(map);
        var raised = 0;
        surface.ViewChanged += (_, _) => raised++;

        map.Navigator.SetSize(640, 480);

        Assert.True(raised > 0);
    }
}
