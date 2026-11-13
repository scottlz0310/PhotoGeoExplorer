using PhotoMapStudio.Tiles.Vector;

namespace PhotoGeoExplorer.Services.RoadNames;

/// <summary>
/// 道路名の判定に使う、ベクタータイル（<see cref="RoadNameSuggester.TileZoom"/>）を読み出す
/// </summary>
internal interface IRoadTileSource
{
    /// <returns>タイルがなければ（配信範囲外・キャッシュになく通信もできない）null</returns>
    /// <exception cref="IOException">タイルを読めなかった</exception>
    /// <exception cref="HttpRequestException">タイルを取得できなかった</exception>
    Task<VectorTile?> GetTileAsync(int x, int y, CancellationToken cancellationToken);
}
