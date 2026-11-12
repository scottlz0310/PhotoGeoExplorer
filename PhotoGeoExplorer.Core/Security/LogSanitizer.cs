using System;
using System.Text.RegularExpressions;

namespace PhotoGeoExplorer.Security;

/// <summary>
/// ログ・クラッシュレポートへ書き出す文字列から機密値を伏せる。
/// </summary>
internal static partial class LogSanitizer
{
    internal const string Mask = "***";

    internal static string MaskSecrets(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return AccessTokenQueryRegex().Replace(text, "$1" + Mask);
    }

    [GeneratedRegex(@"(access_token=)[^&\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AccessTokenQueryRegex();
}
