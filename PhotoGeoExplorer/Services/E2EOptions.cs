using System;
using PhotoGeoExplorer.Services.MapRendering;

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

    /// <summary>
    /// 起動時の地図の表示位置と論理ズーム（小数可）を <c>"緯度,経度,ズーム"</c> 形式で指定する（#363 案 A）。
    /// 利用者の設定ファイルは書き換えない。
    /// </summary>
    internal const string MapViewVariable = "PHOTO_GEO_EXPLORER_E2E_MAP_VIEW";

    /// <summary>
    /// <c>1</c> または <c>true</c> を指定したときだけ、実行中のローカル専用名前付きパイプ操作チャンネルを開く（#363 案 B）。
    /// 未設定・それ以外の値ではコードパスごと動かない。
    /// </summary>
    internal const string ControlChannelVariable = "PHOTO_GEO_EXPLORER_E2E_CONTROL";

    internal static bool IsAreaDataDownloadDisabled(Func<string, string?>? getEnvironmentVariable = null)
        => !string.IsNullOrWhiteSpace((getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(DisableAreaDataDownloadVariable));

    internal static bool TryGetStartupMapView(
        out MapViewTarget target,
        out string? error,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        target = default;
        error = null;

        var raw = (getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(MapViewVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (MapViewTarget.TryParse(raw, out target, out var parseError))
        {
            return true;
        }

        error = parseError;
        return false;
    }

    internal static bool IsControlChannelEnabled(Func<string, string?>? getEnvironmentVariable = null)
    {
        var raw = (getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(ControlChannelVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        return string.Equals(trimmed, "1", StringComparison.Ordinal)
            || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase);
    }
}
