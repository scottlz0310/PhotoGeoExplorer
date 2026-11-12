using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using BruTile.Predefined;
using BruTile.Web;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// Mapbox Satellite（Raster Tiles API）のタイルソース。
/// 認証エラーを利用者へ伝えるため、401・403 を検出して通知する。
/// </summary>
internal sealed class MapboxSatelliteTileSource : HttpTileSource
{
    internal const string SourceName = "Mapbox Satellite";

    // {k} は BruTile が apiKey で置換する。トークンをテンプレート文字列へ直接埋め込まない。
    internal const string UrlTemplate = "https://api.mapbox.com/v4/mapbox.satellite/{z}/{x}/{y}.jpg90?access_token={k}";

    private readonly Action? _authenticationFailed;

    internal MapboxSatelliteTileSource(string accessToken, string userAgent, Action? authenticationFailed)
        : base(
            new GlobalSphericalMercator(),
            UrlTemplate,
            apiKey: accessToken,
            name: SourceName,
            // Mapbox の利用条件が端末への保存を明確に許していないため、永続キャッシュを付けない。
            persistentCache: null,
            attribution: new Attribution(MapAttribution.MapboxText, MapAttribution.MapboxUrl),
            configureHttpRequestMessage: r => r.Headers.TryAddWithoutValidation("User-Agent", userAgent))
    {
        _authenticationFailed = authenticationFailed;
    }

    public override async Task<byte[]?> GetTileAsync(HttpClient httpClient, TileInfo tileInfo, CancellationToken? cancellationToken = null)
    {
        try
        {
            return await base.GetTileAsync(httpClient, tileInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _authenticationFailed?.Invoke();
            throw;
        }
    }
}
