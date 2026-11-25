using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図上に置く写真 1 枚分のマーカー。表示面（<see cref="IMapSurface"/>）はこれを保持し、ヒット判定の結果として返す。
/// </summary>
/// <param name="Latitude">緯度（度）</param>
/// <param name="Longitude">経度（度）</param>
/// <param name="Item">写真</param>
/// <param name="Metadata">写真のメタデータ</param>
/// <param name="PinImagePath">ピン画像のパス。画像がないときは <see langword="null"/>（表示面の既定の印で描く）</param>
internal sealed record MapMarker(
    double Latitude,
    double Longitude,
    PhotoItem Item,
    PhotoMetadata Metadata,
    string? PinImagePath);
