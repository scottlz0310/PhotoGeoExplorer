using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal enum MapboxConnectionResult
{
    Success = 0,
    AuthenticationFailed = 1,
    NetworkError = 2
}

internal interface IMapboxConnectionTester
{
    Task<MapboxConnectionResult> TestAsync(string accessToken, CancellationToken cancellationToken);
}

/// <summary>
/// Mapbox Raster Tiles API からタイルを 1 枚（z=0）取得して、トークンが使えるかを確かめる。
/// </summary>
internal sealed class MapboxConnectionTester : IMapboxConnectionTester
{
    private const string TestTileUrl = "https://api.mapbox.com/v4/mapbox.satellite/0/0/0.jpg90?access_token=";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _httpClient;

    public MapboxConnectionTester()
        : this(SharedClient)
    {
    }

    internal MapboxConnectionTester(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<MapboxConnectionResult> TestAsync(string accessToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(TestTileUrl + Uri.EscapeDataString(accessToken)));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgentProvider.UserAgent);

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return MapboxConnectionResult.Success;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return MapboxConnectionResult.AuthenticationFailed;
            }

            AppLog.Error($"Mapbox connection test failed with HTTP {(int)response.StatusCode}.");
            return MapboxConnectionResult.NetworkError;
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("Mapbox connection test failed.", ex);
            return MapboxConnectionResult.NetworkError;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient のタイムアウトは呼び出し側のキャンセルと区別して通信エラーとして扱う。
            AppLog.Error("Mapbox connection test timed out.", ex);
            return MapboxConnectionResult.NetworkError;
        }
    }
}
