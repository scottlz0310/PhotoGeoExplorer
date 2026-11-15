using System.Globalization;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

namespace PhotoMapStudio.Spike;

/// <summary>保存済みの MVT（root/z/x/y.pbf）だけから読む取得元。P1 のオフライン操作で使う（通信しない）。</summary>
public sealed class SavedMvtTileSource : IVectorTileSource
{
    private readonly string _root;

    public SavedMvtTileSource(string root, int maxZoom = 14)
    {
        _root = root;
        MaxZoom = maxZoom;
    }

    public string SourceId => "saved-mvt:" + _root;

    public int MaxZoom { get; }

    public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            _root,
            z.ToString(CultureInfo.InvariantCulture),
            x.ToString(CultureInfo.InvariantCulture),
            y.ToString(CultureInfo.InvariantCulture) + ".pbf");
        return Task.FromResult<byte[]?>(File.Exists(path) ? File.ReadAllBytes(path) : null);
    }
}
