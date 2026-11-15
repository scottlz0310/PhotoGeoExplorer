using System;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// E2E が起動したアプリの挙動を、利用者の設定ファイルを書き換えずに固定する環境変数
/// </summary>
internal static class E2EOptions
{
    /// <summary>
    /// 設定すると、地域データの取得（確認ダイアログ・通信）を無効にする。
    /// E2E の写真には位置情報があり、地域データが無い環境では確認ダイアログが開いて、他のダイアログ（競合など）と操作を妨げるため
    /// </summary>
    internal const string DisableAreaDataDownloadVariable = "PHOTO_GEO_EXPLORER_E2E_DISABLE_AREA_DATA_DOWNLOAD";

    internal static bool IsAreaDataDownloadDisabled(Func<string, string?>? getEnvironmentVariable = null)
        => !string.IsNullOrWhiteSpace((getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(DisableAreaDataDownloadVariable));
}
