using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// Map Pane 専用のサービスインターフェース
/// I/O処理とビジネスロジックを分離
/// </summary>
internal interface IMapPaneService
{
    /// <summary>
    /// 地図オブジェクトを初期化する
    /// </summary>
    /// <param name="tileSource">タイルソースの種類</param>
    /// <param name="userAgent">HTTPリクエストのUser-Agent</param>
    /// <returns>初期化された地図オブジェクト、タイルレイヤー、マーカーレイヤー</returns>
    (Mapsui.Map Map, TileLayer TileLayer, MemoryLayer MarkerLayer) InitializeMap(
        MapTileSourceType tileSource,
        string userAgent);

    /// <summary>
    /// タイルレイヤーを作成する
    /// </summary>
    /// <param name="sourceType">タイルソースの種類</param>
    /// <param name="userAgent">HTTPリクエストのUser-Agent</param>
    /// <returns>新しいタイルレイヤー</returns>
    TileLayer CreateTileLayer(MapTileSourceType sourceType, string userAgent);

    /// <summary>
    /// 選択された写真のメタデータを読み込む
    /// </summary>
    /// <param name="items">写真アイテムのリスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>メタデータを含むアイテムのリスト</returns>
    Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
        IReadOnlyList<PhotoListItem> items,
        CancellationToken cancellationToken);

    /// <summary>
    /// タイルキャッシュのルートディレクトリを取得する
    /// </summary>
    string GetTileCacheRootDirectory();
}
