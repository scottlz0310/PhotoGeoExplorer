using System;
using System.Linq;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapBackgroundCatalogTests
{
    [Fact]
    public void EveryMapTileSourceTypeIsDefinedOnceInSettingsOrder()
    {
        var expected = Enum.GetValues<MapTileSourceType>().OrderBy(type => (int)type).ToArray();

        Assert.Equal(expected, MapBackgroundCatalog.All.Select(descriptor => descriptor.Type));
    }

    [Fact]
    public void ListOrderMatchesTheSavedSettingValues()
    {
        for (var index = 0; index < MapBackgroundCatalog.All.Count; index++)
        {
            Assert.Equal(index, (int)MapBackgroundCatalog.All[index].Type);
        }
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, MapBackgroundKind.Raster, false, false)]
    [InlineData(MapTileSourceType.EsriWorldImagery, MapBackgroundKind.Raster, false, false)]
    [InlineData(MapTileSourceType.MapboxSatellite, MapBackgroundKind.Raster, true, true)]
    [InlineData(MapTileSourceType.OpenFreeMapVector, MapBackgroundKind.Vector, false, false)]
    internal void DescriptorHoldsKindAndMapboxRequirements(
        MapTileSourceType type,
        MapBackgroundKind kind,
        bool requiresToken,
        bool showsLogo)
    {
        var descriptor = MapBackgroundCatalog.Get(type);

        Assert.Equal(kind, descriptor.Kind);
        Assert.Equal(requiresToken, descriptor.RequiresMapboxAccessToken);
        Assert.Equal(showsLogo, descriptor.ShowsMapboxLogo);
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, "© OpenStreetMap contributors")]
    [InlineData(MapTileSourceType.EsriWorldImagery, "Source: Esri, Vantor, Earthstar Geographics, and the GIS User Community")]
    [InlineData(MapTileSourceType.MapboxSatellite, "© Mapbox|© OpenStreetMap|Improve this map")]
    [InlineData(MapTileSourceType.OpenFreeMapVector, "OpenFreeMap|© OpenMapTiles|Data from OpenStreetMap")]
    internal void AttributionLinksKeepTheRequiredWording(MapTileSourceType type, string expectedTexts)
    {
        ArgumentNullException.ThrowIfNull(expectedTexts);
        var texts = MapBackgroundCatalog.Get(type).AttributionLinks.Select(link => link.Text);

        Assert.Equal(expectedTexts.Split('|'), texts);
    }

    [Fact]
    public void OnlyTheMapboxFeedbackLinkTracksTheViewport()
    {
        var tracking = MapBackgroundCatalog.All
            .SelectMany(descriptor => descriptor.AttributionLinks.Select(link => (descriptor.Type, link)))
            .Where(entry => entry.link.TracksViewport)
            .ToArray();

        var single = Assert.Single(tracking);
        Assert.Equal(MapTileSourceType.MapboxSatellite, single.Type);
        Assert.Equal(MapAttribution.MapboxFeedbackUrl, single.link.Url);
    }

    [Fact]
    public void AttributionLinkCountsFitTheAttributionWidgets()
    {
        Assert.All(
            MapBackgroundCatalog.All,
            descriptor => Assert.InRange(descriptor.AttributionLinks.Count, 1, MapAttribution.MaxLinkCount));
    }

    [Fact]
    public void DefaultIsOpenStreetMapAndNeedsNoToken()
    {
        Assert.Equal(MapTileSourceType.OpenStreetMap, MapBackgroundCatalog.Default.Type);
        Assert.False(MapBackgroundCatalog.Default.RequiresMapboxAccessToken);
    }

    [Theory]
    [InlineData(0, MapTileSourceType.OpenStreetMap)]
    [InlineData(1, MapTileSourceType.EsriWorldImagery)]
    [InlineData(2, MapTileSourceType.MapboxSatellite)]
    [InlineData(3, MapTileSourceType.OpenFreeMapVector)]
    [InlineData(-1, MapTileSourceType.OpenStreetMap)]
    [InlineData(99, MapTileSourceType.OpenStreetMap)]
    internal void FromIndexFallsBackToDefaultOutOfRange(int index, MapTileSourceType expected)
    {
        Assert.Equal(expected, MapBackgroundCatalog.FromIndex(index).Type);
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, 0)]
    [InlineData(MapTileSourceType.EsriWorldImagery, 1)]
    [InlineData(MapTileSourceType.MapboxSatellite, 2)]
    [InlineData(MapTileSourceType.OpenFreeMapVector, 3)]
    internal void IndexOfReturnsTheSettingsPosition(MapTileSourceType type, int expected)
    {
        Assert.Equal(expected, MapBackgroundCatalog.IndexOf(type));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    internal void GetRejectsUndefinedValues(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapBackgroundCatalog.Get((MapTileSourceType)value));
    }
}
