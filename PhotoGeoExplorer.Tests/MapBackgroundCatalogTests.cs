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
    [InlineData(MapTileSourceType.GsiStandard, false, MapTileSourceType.OpenStreetMap)]
    [InlineData(MapTileSourceType.GsiPale, false, MapTileSourceType.OpenStreetMap)]
    [InlineData(MapTileSourceType.GsiPhoto, false, MapTileSourceType.OpenStreetMap)]
    [InlineData(MapTileSourceType.GsiVector, false, MapTileSourceType.OpenStreetMap)]
    [InlineData(MapTileSourceType.GsiVector, true, MapTileSourceType.GsiVector)]
    [InlineData(MapTileSourceType.OpenFreeMapVector, false, MapTileSourceType.OpenFreeMapVector)]
    internal void ResolveSourceHonorsTheJapanMapSetting(
        MapTileSourceType source,
        bool enabled,
        MapTileSourceType expected)
    {
        Assert.Equal(expected, MapBackgroundCatalog.ResolveSource(source, enabled));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    internal void GetRejectsUndefinedValues(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapBackgroundCatalog.Get((MapTileSourceType)value));
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, false, false, true, true)]
    [InlineData(MapTileSourceType.MapboxSatellite, false, false, true, false)]
    [InlineData(MapTileSourceType.MapboxSatellite, false, true, true, true)]
    [InlineData(MapTileSourceType.GsiStandard, false, false, false, true)]
    [InlineData(MapTileSourceType.GsiVector, true, false, true, true)]
    internal void ListedAndUsableBackgroundsFollowTheSettings(
        MapTileSourceType type, bool japanMapSourcesEnabled, bool hasToken, bool listed, bool usable)
    {
        var descriptor = MapBackgroundCatalog.Get(type);

        Assert.Equal(listed, MapBackgroundCatalog.IsListed(descriptor, japanMapSourcesEnabled));
        Assert.Equal(usable, MapBackgroundCatalog.IsUsable(descriptor, hasToken));
    }
}
