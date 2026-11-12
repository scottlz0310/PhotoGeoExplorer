namespace PhotoGeoExplorer.Models;

/// <summary>
/// 国勢調査 小地域から判定した字地名（確定値）
/// </summary>
internal sealed record AreaName(
    string KeyCode,
    string Prefecture,
    string City,
    string Town,
    bool SnappedToNearest,
    int CensusYear);
