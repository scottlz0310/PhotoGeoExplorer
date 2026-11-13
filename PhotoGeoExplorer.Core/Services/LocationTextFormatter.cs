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
    /// 第 1 候補の表記と、残りの候補数（<c>(+N)</c>）を返す。候補がなければ空文字
    /// </summary>
    /// <param name="candidates">近い順の候補</param>
    /// <param name="formatRouteNumber">名称のない道路の表記を、路線番号から作る（言語ごとに異なる）</param>
    public static string FormatRoadName(
        IReadOnlyList<RoadNameCandidate> candidates,
        Func<string, string> formatRouteNumber)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates.Count switch
        {
            0 => string.Empty,
            1 => FormatRoadCandidate(candidates[0], formatRouteNumber),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatRoadCandidate(candidates[0], formatRouteNumber)} (+{candidates.Count - 1})")
        };
    }

    /// <summary>
    /// 候補 1 件の表記。名称があればそれを、なければ路線番号から作る
    /// </summary>
    public static string FormatRoadCandidate(RoadNameCandidate candidate, Func<string, string> formatRouteNumber)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(formatRouteNumber);

        return candidate.Name ?? formatRouteNumber(candidate.Ref ?? string.Empty);
    }
}
