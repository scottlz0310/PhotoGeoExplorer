namespace PhotoGeoExplorer.Models;

/// <summary>
/// 国勢調査 小地域から判定した字地名（確定値）
/// </summary>
/// <param name="KeyCode">e-Stat の KEY_CODE。9 桁または 11 桁</param>
/// <param name="SnappedToNearest">地点を内包する陸の小地域がなく、30m 以内の最近傍へ寄せた結果か</param>
/// <param name="IsWater">水面調査区か。水面には住所としての字地名がないため、表示には使わない</param>
internal sealed record AreaName(
    string KeyCode,
    string Prefecture,
    string City,
    string Town,
    bool SnappedToNearest,
    int CensusYear,
    bool IsWater = false);
