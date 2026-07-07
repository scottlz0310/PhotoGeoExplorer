using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 設定値の正規化ロジックを集約する static ヘルパー。
/// SettingsCoordinator と設定系 ViewModel から共用します。
/// </summary>
internal static class SettingsNormalization
{
    internal static string? NormalizeLanguageSetting(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return null;
        }

        var trimmed = languageTag.Trim();
        if (string.Equals(trimmed, "system", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(trimmed, "ja", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "ja-jp", StringComparison.OrdinalIgnoreCase))
        {
            return "ja-JP";
        }

        if (string.Equals(trimmed, "en", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "en-us", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        return trimmed;
    }

    internal static int NormalizeMapZoomLevel(int level)
    {
        if (MapZoomLevelCatalog.Options.Contains(level))
        {
            return level;
        }

        return MapZoomLevelCatalog.Default;
    }

    /// <summary>
    /// カタログ外のズームレベルを最寄りの有効値へスナップします。
    /// スライダー等の連続入力 UI 向け（設定読み込み時は <see cref="NormalizeMapZoomLevel"/> を使用）。
    /// </summary>
    internal static int SnapMapZoomLevelToNearest(int level)
    {
        if (MapZoomLevelCatalog.Options.Contains(level))
        {
            return level;
        }

        if (MapZoomLevelCatalog.Options.Length == 0)
        {
            return MapZoomLevelCatalog.Default;
        }

        // int 減算のオーバーフロー（Math.Abs(int.MinValue) の例外含む）を避けるため long で距離を計算する
        return MapZoomLevelCatalog.Options.MinBy(candidate => Math.Abs((long)candidate - level));
    }

    internal static string? NormalizeExternalContentBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var parsed))
        {
            return null;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parsed.AbsoluteUri.TrimEnd('/');
    }

    internal static PaneLayoutPreset NormalizePaneLayoutPreset(PaneLayoutPreset preset)
    {
        return Enum.IsDefined(preset)
            ? preset
            : AppSettings.DefaultPaneLayoutPreset;
    }

    internal static (PaneViewType Region1View, PaneViewType Region2View, PaneViewType Region3View) NormalizePaneRegionViews(
        PaneViewType region1View,
        PaneViewType region2View,
        PaneViewType region3View)
    {
        var values = new[]
        {
            NormalizePaneView(region1View),
            NormalizePaneView(region2View),
            NormalizePaneView(region3View)
        };

        var allViews = Enum.GetValues<PaneViewType>();
        var used = new HashSet<PaneViewType>();
        for (var i = 0; i < values.Length; i++)
        {
            if (used.Add(values[i]))
            {
                continue;
            }

            var replacement = allViews.FirstOrDefault(candidate => !used.Contains(candidate));
            values[i] = replacement;
            used.Add(values[i]);
        }

        return (values[0], values[1], values[2]);
    }

    internal static string? FindValidAncestorPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var current = Path.GetFullPath(path);

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current))
                {
                    return current;
                }

                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }
        catch (Exception ex) when (ex is ArgumentException
            or PathTooLongException
            or System.Security.SecurityException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            AppLog.Error($"Failed to find valid ancestor path for '{path}'", ex);
        }

        return null;
    }

    private static PaneViewType NormalizePaneView(PaneViewType view)
    {
        return Enum.IsDefined(view)
            ? view
            : PaneViewType.File;
    }
}
