using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// 表示範囲のラスタタイルをすべて取得して、保存用のレイヤーを作る。
/// 表示中のタイルレイヤーは取得の完了と失敗が分からないため、保存の条件を変える場合には使えない。
/// </summary>
internal static class RasterTileExportLayer
{
    // OpenStreetMap の利用方針が許す同時接続数
    private const int MaxConcurrentRequests = 2;

    /// <summary>
    /// 表示範囲を覆うタイル。ズーム段階は、画面表示と同じく解像度が最も近いものを使う
    /// </summary>
    internal static IReadOnlyList<TileInfo> SelectTiles(ITileSchema schema, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var halfWidth = viewport.Width * viewport.Resolution / 2;
        var halfHeight = viewport.Height * viewport.Resolution / 2;
        var extent = new Extent(
            viewport.CenterX - halfWidth,
            viewport.CenterY - halfHeight,
            viewport.CenterX + halfWidth,
            viewport.CenterY + halfHeight);
        var level = BruTile.Utilities.GetNearestLevel(schema.Resolutions, viewport.Resolution);
        return schema.GetTileInfos(extent, level).ToArray();
    }

    /// <exception cref="InvalidOperationException">取得できないタイルがある場合</exception>
    internal static async Task<MemoryLayer> CreateAsync(
        HttpTileSource source,
        Viewport viewport,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(httpClient);

        var tiles = SelectTiles(source.Schema, viewport);
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var gate = new SemaphoreSlim(MaxConcurrentRequests);

        async Task<IFeature?> FetchAsync(TileInfo tile)
        {
            await gate.WaitAsync(failed.Token).ConfigureAwait(false);
            try
            {
                var data = await source.GetTileAsync(httpClient, tile, failed.Token).ConfigureAwait(false);

                // 画像がないタイルは、配信元の提供範囲外（空白にする）
                return data is null
                    ? null
                    : new RasterFeature(new MRaster(
                        data,
                        new MRect(tile.Extent.MinX, tile.Extent.MinY, tile.Extent.MaxX, tile.Extent.MaxY)));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // 1 枚でも取れなければ画像は作らないため、残りの取得をやめる
                await failed.CancelAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                gate.Release();
            }
        }

        var fetches = tiles.Select(FetchAsync).ToArray();
        try
        {
            await Task.WhenAll(fetches).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 打ち切りによる取り消しではなく、原因になった失敗を報告する。時間切れは取り消しとして届く
            var cause = fetches
                .Where(fetch => fetch.IsFaulted)
                .Select(fetch => fetch.Exception!.InnerException)
                .FirstOrDefault() ?? ex;
            throw new InvalidOperationException("Failed to fetch the map tiles for the image.", cause);
        }

        return new MemoryLayer
        {
            Name = "RasterMapExport",
            Features = fetches.Select(fetch => fetch.Result).OfType<IFeature>().ToArray(),
            Style = new RasterStyle()
        };
    }
}
