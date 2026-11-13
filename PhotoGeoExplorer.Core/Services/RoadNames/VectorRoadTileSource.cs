using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

namespace PhotoGeoExplorer.Services.RoadNames;

/// <summary>
/// <see cref="IVectorTileSource"/>（取得・キャッシュ）から取り出したバイト列を、タイルへ読み出す
/// </summary>
internal sealed class VectorRoadTileSource : IRoadTileSource
{
    private readonly IVectorTileSource _source;

    public VectorRoadTileSource(IVectorTileSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task<VectorTile?> GetTileAsync(int x, int y, CancellationToken cancellationToken)
    {
        byte[]? bytes;
        try
        {
            bytes = await _source.GetTileAsync(RoadNameSuggester.TileZoom, x, y, cancellationToken).ConfigureAwait(false);
        }
        catch (TileFetchException ex)
        {
            // IRoadNameSuggester の契約（HttpRequestException）に合わせ、呼び出し側が配信元の型を知らなくて済むようにする
            throw new HttpRequestException(
                $"Failed to fetch the road tile z{RoadNameSuggester.TileZoom}/{x}/{y}.",
                ex,
                ex.StatusCode);
        }

        if (bytes is null)
        {
            return null;
        }

        if (!VectorTileDecoder.TryDecode(bytes, out var tile))
        {
            throw new IOException($"The road tile z{RoadNameSuggester.TileZoom}/{x}/{y} is not a vector tile.");
        }

        return tile;
    }
}
