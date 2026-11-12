using System.Globalization;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 撮影位置情報の表示用の表記を作る
/// </summary>
internal static class LocationTextFormatter
{
    public static string FormatCoordinates(double latitude, double longitude)
        => string.Create(CultureInfo.InvariantCulture, $"{latitude:F6}, {longitude:F6}");

    public static string FormatAreaName(AreaName areaName, bool includePrefecture)
    {
        ArgumentNullException.ThrowIfNull(areaName);

        var parts = includePrefecture
            ? new[] { areaName.Prefecture, areaName.City, areaName.Town }
            : new[] { areaName.City, areaName.Town };
        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// 第 1 候補の名称と、残りの候補数（<c>(+N)</c>）を返す。候補がなければ空文字
    /// </summary>
    public static string FormatRoadName(IReadOnlyList<RoadNameCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates.Count switch
        {
            0 => string.Empty,
            1 => candidates[0].Name,
            _ => string.Create(CultureInfo.InvariantCulture, $"{candidates[0].Name} (+{candidates.Count - 1})")
        };
    }
}
