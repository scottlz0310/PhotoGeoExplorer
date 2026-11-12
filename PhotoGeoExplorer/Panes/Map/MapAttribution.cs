using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 帰属表示の 1 項目（文言とリンク先）。
/// </summary>
/// <param name="Text">表示する文言</param>
/// <param name="Url">クリックで開くページ</param>
/// <param name="TracksViewport">リンク先に、表示中の地図の位置と倍率を含めるかどうか</param>
internal sealed record MapAttributionLink(string Text, string Url, bool TracksViewport = false);

/// <summary>
/// 背景地図ごとの帰属表示（文言とリンク先）。
/// </summary>
internal static class MapAttribution
{
    internal const string OpenStreetMapText = "© OpenStreetMap contributors";
    internal const string OpenStreetMapUrl = "https://www.openstreetmap.org/copyright";
    internal const string EsriText = "Source: Esri, Vantor, Earthstar Geographics, and the GIS User Community";
    internal const string EsriUrl = "https://www.esri.com/";
    internal const string MapboxText = "© Mapbox © OpenStreetMap Improve this map";
    internal const string MapboxUrl = "https://www.mapbox.com/about/maps";
    internal const string MapboxFeedbackUrl = "https://apps.mapbox.com/feedback/";

    // Web メルカトルで、256px タイルのズーム 0 における 1px あたりのメートル数。
    private const double ZoomZeroResolution = 156543.03392804097;

    private static readonly MapAttributionLink[] OpenStreetMapLinks =
    [
        new(OpenStreetMapText, OpenStreetMapUrl)
    ];

    private static readonly MapAttributionLink[] EsriLinks =
    [
        new(EsriText, EsriUrl)
    ];

    // Mapbox は、Mapbox・OpenStreetMap・地図の修正依頼を、それぞれ別のリンクで示すことを求めている。
    private static readonly MapAttributionLink[] MapboxLinks =
    [
        new("© Mapbox", MapboxUrl),
        new("© OpenStreetMap", OpenStreetMapUrl),
        new("Improve this map", MapboxFeedbackUrl, TracksViewport: true)
    ];

    /// <summary>
    /// 1 つの背景が持つ帰属項目の最大数
    /// </summary>
    internal const int MaxLinkCount = 3;

    /// <summary>
    /// Mapbox のロゴ（帰属表示用の公式 SVG）。Mapbox の背景を表示するときは文言とあわせて表示が必須。
    /// </summary>
    internal static string MapboxLogoPath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "MapAttribution",
        "mapbox-logo.svg");

    internal static IReadOnlyList<MapAttributionLink> GetLinks(MapTileSourceType sourceType)
    {
        return sourceType switch
        {
            MapTileSourceType.EsriWorldImagery => EsriLinks,
            MapTileSourceType.MapboxSatellite => MapboxLinks,
            _ => OpenStreetMapLinks
        };
    }

    /// <summary>
    /// 地図の修正依頼ページの URL を、表示中の位置と倍率つきで組み立てる。
    /// 位置や倍率が定まっていない場合は、位置なしの URL を返す。
    /// </summary>
    /// <param name="longitude">中心の経度</param>
    /// <param name="latitude">中心の緯度</param>
    /// <param name="resolution">地図の解像度（1px あたりのメートル数。Web メルカトル）</param>
    internal static string BuildMapboxFeedbackUrl(double longitude, double latitude, double resolution)
    {
        if (!double.IsFinite(longitude)
            || !double.IsFinite(latitude)
            || !double.IsFinite(resolution)
            || resolution <= 0
            || Math.Abs(longitude) > 180
            || Math.Abs(latitude) > 90)
        {
            return MapboxFeedbackUrl;
        }

        // Mapbox のズームは 512px タイル基準のため、256px タイル基準の値より 1 小さい。
        var zoom = Math.Clamp(Math.Log2(ZoomZeroResolution / resolution) - 1, 0, 22);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{MapboxFeedbackUrl}#/{longitude:0.#####}/{latitude:0.#####}/{zoom:0.##}");
    }
}
