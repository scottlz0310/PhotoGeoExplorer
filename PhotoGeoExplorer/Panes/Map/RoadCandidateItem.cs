using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 最寄り道路の候補 1 件の表示用の文言（候補の一覧の 1 行）
/// </summary>
/// <param name="Name">名称。名称のない道路は「路線番号 203」</param>
/// <param name="RouteText">名称とは別にある路線番号の表記（「路線番号 203」）。なければ null</param>
/// <param name="DistanceText">地点からの距離（「12 m」）。確定済みで、今の候補にない道路は空</param>
/// <param name="Road">この行を確定するときに保存する道路名</param>
/// <param name="IsConfirmed">この道路が、選択中の写真に確定されている</param>
/// <param name="CanConfirm">選択中の写真に確定を保存できる（保存先と写真の情報がある）</param>
internal sealed record RoadCandidateItem(
    string Name,
    string? RouteText,
    string DistanceText,
    ConfirmedRoadName Road,
    bool IsConfirmed,
    bool CanConfirm)
{
    public Visibility RouteVisibility => RouteText is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ConfirmedMarkVisibility => IsConfirmed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ConfirmVisibility => CanConfirm && !IsConfirmed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnconfirmVisibility => CanConfirm && IsConfirmed ? Visibility.Visible : Visibility.Collapsed;
}
