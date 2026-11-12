using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class MapboxConnectionTesterTests
{
    private const string Token = "pk.test-token";

    [Theory]
    [InlineData(HttpStatusCode.OK, MapboxConnectionResult.Success)]
    [InlineData(HttpStatusCode.Unauthorized, MapboxConnectionResult.AuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, MapboxConnectionResult.AuthenticationFailed)]
    [InlineData(HttpStatusCode.TooManyRequests, MapboxConnectionResult.NetworkError)]
    [InlineData(HttpStatusCode.InternalServerError, MapboxConnectionResult.NetworkError)]
    internal async Task TestAsyncMapsStatusCodeToResult(HttpStatusCode statusCode, MapboxConnectionResult expected)
    {
        using var handler = new StubHttpMessageHandler(statusCode);
        using var client = new HttpClient(handler);
        var tester = new MapboxConnectionTester(client);

        var result = await tester.TestAsync(Token, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(expected, result);
        var uri = Assert.Single(handler.RequestUris);
        Assert.Equal("https://api.mapbox.com/v4/mapbox.satellite/0/0/0.jpg90?access_token=" + Token, uri?.ToString());
    }

    [Fact]
    public async Task TestAsyncReturnsNetworkErrorWhenRequestFails()
    {
        using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        using var client = new HttpClient(handler);
        var tester = new MapboxConnectionTester(client);

        var result = await tester.TestAsync(Token, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(MapboxConnectionResult.NetworkError, result);
    }

    [Fact]
    public async Task TestAsyncPropagatesCallerCancellation()
    {
        using var handler = new StubHttpMessageHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var tester = new MapboxConnectionTester(client);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tester.TestAsync(Token, cts.Token)).ConfigureAwait(true);
    }
}
