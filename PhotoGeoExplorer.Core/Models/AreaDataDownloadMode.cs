namespace PhotoGeoExplorer.Models;

/// <summary>
/// 字地名の地域データが無い地域の写真を選んだときの動作
/// </summary>
public enum AreaDataDownloadMode
{
    /// <summary>確認ダイアログを出し、同意したときだけ取得する（既定）</summary>
    Ask = 0,

    /// <summary>確認せずに取得する</summary>
    Auto = 1,

    /// <summary>案内も取得もしない（設定からの手動の取得・取り込みは使える）</summary>
    Never = 2
}
