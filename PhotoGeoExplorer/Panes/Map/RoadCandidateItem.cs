using Microsoft.UI.Xaml;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 最寄り道路の候補 1 件の表示用の文言（候補の一覧の 1 行）
/// </summary>
/// <param name="Name">名称。名称のない道路は「路線番号 203」</param>
/// <param name="RouteText">名称とは別にある路線番号の表記（「路線番号 203」）。なければ null</param>
/// <param name="DistanceText">地点からの距離（「12 m」）</param>
internal sealed record RoadCandidateItem(string Name, string? RouteText, string DistanceText)
{
    public Visibility RouteVisibility => RouteText is null ? Visibility.Collapsed : Visibility.Visible;
}
