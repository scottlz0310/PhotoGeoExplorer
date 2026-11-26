using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using PhotoMapStudio.Tiles;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

internal sealed class GsiRasterTileSource(TileSource source, string userAgent, IPersistentCache<byte[]>? persistentCache)
    : HttpTileSource(
        new GlobalSphericalMercator(0, source.MaxZoom),
        source.UrlTemplate,
        name: source.Name,
        persistentCache: persistentCache,
        configureHttpRequestMessage: request => request.Headers.TryAddWithoutValidation("User-Agent", userAgent))
{
    public override async Task<byte[]?> GetTileAsync(HttpClient httpClient, TileInfo tileInfo, CancellationToken? cancellationToken = null)
    {
        // ナビゲーターの解像度一覧を z0 起点に保ち、配信範囲外のズームでは取得しない。
        if (!source.SupportsZoom(tileInfo.Index.Level))
        {
            cancellationToken?.ThrowIfCancellationRequested();
            return null;
        }

        try
        {
            return await base.GetTileAsync(httpClient, tileInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // 地理院タイルでは提供範囲外のタイルが 404 になる。
            return null;
        }
    }
}
