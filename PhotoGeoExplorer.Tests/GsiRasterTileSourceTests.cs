using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using PhotoMapStudio.Tiles;

namespace PhotoGeoExplorer.Tests;

public sealed class GsiRasterTileSourceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task GetTileAsyncDoesNotFetchBelowTheProviderMinimumZoom(int zoom)
    {
        var source = new GsiRasterTileSource(TileSources.GsiStandard, "PhotoGeoExplorer.Tests", persistentCache: null);
        using var handler = new StubHttpMessageHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var tile = new TileInfo { Index = new TileIndex(0, 0, zoom) };

        Assert.Null(await source.GetTileAsync(client, tile, CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetTileAsyncReturnsNoImageForAnAbsentTile()
    {
        var source = new GsiRasterTileSource(TileSources.GsiStandard, "PhotoGeoExplorer.Tests", persistentCache: null);
        using var handler = new StubHttpMessageHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);
        var tile = new TileInfo { Index = new TileIndex(0, 0, 12) };

        Assert.Null(await source.GetTileAsync(client, tile, CancellationToken.None).ConfigureAwait(true));
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetTileAsyncReportsAServiceFailure()
    {
        var source = new GsiRasterTileSource(TileSources.GsiStandard, "PhotoGeoExplorer.Tests", persistentCache: null);
        using var handler = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler);
        var tile = new TileInfo { Index = new TileIndex(0, 0, 12) };

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => source.GetTileAsync(client, tile, CancellationToken.None)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }
}
