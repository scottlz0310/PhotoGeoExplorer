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
    [InlineData(MapTileSourceType.MapboxSatellite, MapAttribution.MapboxText, MapAttribution.MapboxUrl)]
    internal void AttributionPresenterShowsTextForSource(MapTileSourceType sourceType, string expectedText, string expectedUrl)
    {
        var presenter = new MapAttributionPresenter();
        using var map = new Mapsui.Map();
        var widgetsBefore = map.Widgets.Count;

        presenter.Attach(map, MapTileSourceType.OpenStreetMap);
        presenter.Update(sourceType);

        Assert.Equal(widgetsBefore + 2, map.Widgets.Count);
        Assert.Equal(expectedText, presenter.Text);
        Assert.Equal(expectedUrl, presenter.Url);
        Assert.Equal(sourceType == MapTileSourceType.MapboxSatellite, presenter.IsMapboxLogoVisible);
    }
}
