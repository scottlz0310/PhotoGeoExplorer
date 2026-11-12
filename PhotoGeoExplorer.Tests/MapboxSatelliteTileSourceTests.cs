using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;

namespace PhotoGeoExplorer.Tests;

public sealed class MapboxSatelliteTileSourceTests
{
    private const string Token = "pk.test-token";
    private const string UserAgent = "PhotoGeoExplorer/Test";
    private static readonly TileInfo Tile = new() { Index = new TileIndex(3, 5, 4) };

    [Fact]
    public void CreateTileLayerReturnsMapboxLayerWithoutPersistentCache()
    {
        var service = new MapPaneService();

        using var layer = service.CreateTileLayer(
            new MapBackgroundRequest(MapTileSourceType.MapboxSatellite, UserAgent, Token));

        var source = Assert.IsType<MapboxSatelliteTileSource>(layer.TileSource);
        Assert.Equal("Mapbox Satellite", layer.Name);
        Assert.Equal(MapAttribution.MapboxText, source.Attribution.Text);
        Assert.IsNotType<BruTile.Cache.FileCache>(source.PersistentCache);
        Assert.Equal(
            "https://api.mapbox.com/v4/mapbox.satellite/4/3/5.jpg90?access_token=" + Token,
            source.GetUrl(Tile).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateTileLayerThrowsWhenMapboxTokenIsMissing(string? token)
    {
        var service = new MapPaneService();

        Assert.Throws<ArgumentException>(() => service.CreateTileLayer(
            new MapBackgroundRequest(MapTileSourceType.MapboxSatellite, UserAgent, token)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 1)]
    [InlineData(HttpStatusCode.Forbidden, 1)]
    [InlineData(HttpStatusCode.NotFound, 0)]
    [InlineData(HttpStatusCode.InternalServerError, 0)]
    public async Task GetTileAsyncReportsAuthenticationFailureOnly(HttpStatusCode statusCode, int expectedNotifications)
    {
        var notifications = 0;
        var source = new MapboxSatelliteTileSource(Token, UserAgent, () => Interlocked.Increment(ref notifications));
        using var handler = new StubHttpMessageHandler(statusCode);
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => source.GetTileAsync(client, Tile, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(expectedNotifications, notifications);
    }

    [Fact]
    public async Task GetTileAsyncReturnsTileAndSendsUserAgentOnSuccess()
    {
        var notifications = 0;
        string? sentUserAgent = null;
        var source = new MapboxSatelliteTileSource(Token, UserAgent, () => Interlocked.Increment(ref notifications));
        using var handler = new StubHttpMessageHandler(request =>
        {
            sentUserAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7]) };
        });
        using var client = new HttpClient(handler);

        var bytes = await source.GetTileAsync(client, Tile, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new byte[] { 9, 8, 7 }, bytes);
        Assert.Equal(UserAgent, sentUserAgent);
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, MapAttribution.OpenStreetMapText, MapAttribution.OpenStreetMapUrl)]
    [InlineData(MapTileSourceType.EsriWorldImagery, MapAttribution.EsriText, MapAttribution.EsriUrl)]
    internal void AttributionPresenterShowsSingleLinkForSource(MapTileSourceType sourceType, string expectedText, string expectedUrl)
    {
        var presenter = new MapAttributionPresenter();
        using var map = new Mapsui.Map();

        // Mapbox（3 項目）から切り替えても、余った項目が残らないこと
        presenter.Attach(map, MapTileSourceType.MapboxSatellite);
        presenter.Update(sourceType);

        var link = Assert.Single(presenter.Links);
        Assert.Equal(expectedText, link.Text);
        Assert.Equal(expectedUrl, link.Url);
        Assert.Equal(expectedText, presenter.Text);
        Assert.False(presenter.IsMapboxLogoVisible);
    }

    [Fact]
    public void AttributionPresenterShowsSeparateLinksForMapbox()
    {
        var presenter = new MapAttributionPresenter();
        using var map = new Mapsui.Map();

        presenter.Attach(map, MapTileSourceType.OpenStreetMap);
        presenter.Update(MapTileSourceType.MapboxSatellite);

        // Mapbox・OpenStreetMap・地図の修正依頼を、それぞれ別のリンクで開けること
        Assert.Equal(
            [
                ("© Mapbox", "https://www.mapbox.com/about/maps"),
                ("© OpenStreetMap", "https://www.openstreetmap.org/copyright"),
                ("Improve this map", "https://apps.mapbox.com/feedback/")
            ],
            presenter.Links);
        Assert.Equal(MapAttribution.MapboxText, presenter.Text);
        Assert.True(presenter.IsMapboxLogoVisible);
    }

    [Fact]
    public void AttributionPresenterAddsViewportToMapboxFeedbackLinkOnly()
    {
        var presenter = new MapAttributionPresenter();
        using var map = new Mapsui.Map();
        presenter.Attach(map, MapTileSourceType.MapboxSatellite);
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(139.7671, 35.6812);

        // ズーム 14（512px タイル基準）相当の解像度
        presenter.UpdateViewport(new Mapsui.Viewport(x, y, 156543.03392804097 / 32768, 0, 800, 600));

        Assert.Equal("https://www.mapbox.com/about/maps", presenter.Links[0].Url);
        Assert.Equal("https://www.openstreetmap.org/copyright", presenter.Links[1].Url);
        Assert.Equal("https://apps.mapbox.com/feedback/#/139.7671/35.6812/14", presenter.Links[2].Url);

        // 他の背景へ切り替えた後は、位置を付けない
        presenter.Update(MapTileSourceType.OpenStreetMap);
        presenter.UpdateViewport(new Mapsui.Viewport(x, y, 10, 0, 800, 600));
        Assert.Equal(MapAttribution.OpenStreetMapUrl, Assert.Single(presenter.Links).Url);
    }

    [Theory]
    [InlineData(139.7671, 35.6812, 156543.03392804097 / 2, "https://apps.mapbox.com/feedback/#/139.7671/35.6812/0")]
    [InlineData(-122.41941, 37.77493, 156543.03392804097 / 8192, "https://apps.mapbox.com/feedback/#/-122.41941/37.77493/12")]
    [InlineData(0, 0, 156543.03392804097, "https://apps.mapbox.com/feedback/#/0/0/0")]
    [InlineData(10.123456789, -20.987654321, 6.5, "https://apps.mapbox.com/feedback/#/10.12346/-20.98765/13.56")]
    [InlineData(double.NaN, 35, 10, "https://apps.mapbox.com/feedback/")]
    [InlineData(139, double.PositiveInfinity, 10, "https://apps.mapbox.com/feedback/")]
    [InlineData(139, 35, 0, "https://apps.mapbox.com/feedback/")]
    [InlineData(139, 35, -1, "https://apps.mapbox.com/feedback/")]
    [InlineData(139, 35, double.NaN, "https://apps.mapbox.com/feedback/")]
    [InlineData(181, 35, 10, "https://apps.mapbox.com/feedback/")]
    [InlineData(139, 91, 10, "https://apps.mapbox.com/feedback/")]
    public void BuildMapboxFeedbackUrlIncludesPositionWhenValid(double longitude, double latitude, double resolution, string expected)
    {
        Assert.Equal(expected, MapAttribution.BuildMapboxFeedbackUrl(longitude, latitude, resolution));
    }
}
