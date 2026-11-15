using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定の「地域データ」で、地域データが無い地域の写真を選んだときの動作を選ぶ項目
/// </summary>
internal sealed record AreaDataModeOption(AreaDataDownloadMode Mode, string Label)
{
    public override string ToString() => Label;
}
