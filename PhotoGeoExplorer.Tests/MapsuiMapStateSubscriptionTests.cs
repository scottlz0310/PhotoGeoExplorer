using Mapsui;
using Mapsui.Layers;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapsuiMapStateSubscriptionTests
{
    [Fact]
    public void ViewportChangeIsRelayed()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        var count = 0;
        using var subscription = new MapsuiMapStateSubscription(surface, () => count++);

        map.Navigator.SetSize(640, 480);

        Assert.True(count > 0);
    }

    [Fact]
    public void LayerCollectionChangeIsRelayed()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        var count = 0;
        using var subscription = new MapsuiMapStateSubscription(surface, () => count++);
        using var layer = new MemoryLayer { Name = "extra" };

        map.Layers.Add(layer);

        Assert.True(count > 0);
    }

    [Fact]
    public void NothingIsRelayedAfterDispose()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);
        var count = 0;
        var subscription = new MapsuiMapStateSubscription(surface, () => count++);
        subscription.Dispose();
        using var layer = new MemoryLayer { Name = "extra" };

        map.Navigator.SetSize(640, 480);
        map.Layers.Add(layer);

        Assert.Equal(0, count);
    }
}
