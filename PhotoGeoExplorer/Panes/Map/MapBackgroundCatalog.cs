using System;
using System.Collections.Generic;
using System.Globalization;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 背景地図の描画方式
/// </summary>
internal enum MapBackgroundKind
{
    /// <summary>ラスタタイル（取得したタイル画像をそのまま表示する）</summary>
    Raster,

    /// <summary>ベクタータイル（表示範囲ごとに描画した 1 枚の画像を表示する）</summary>
    Vector
}

/// <summary>
/// 背景地図 1 種類の定義。
/// </summary>
/// <param name="Type">識別子。設定ファイルの保存値でもあるため、値は変えない</param>
/// <param name="DisplayNameKey">設定画面や保存時の背景選択に使うリソース名</param>
/// <param name="RequiresJapanMapSources">日本国内向けの背景地図の有効化が必要</param>
/// <param name="Kind">描画方式</param>
/// <param name="AttributionLinks">帰属表示の項目（最大 <see cref="MapAttribution.MaxLinkCount"/> 件）</param>
/// <param name="RequiresMapboxAccessToken">Mapbox のアクセストークンがないと表示できない</param>
/// <param name="ShowsMapboxLogo">帰属表示に Mapbox のロゴが必須</param>
internal sealed record MapBackgroundDescriptor(
    MapTileSourceType Type,
    MapBackgroundKind Kind,
    string DisplayNameKey,
    IReadOnlyList<MapAttributionLink> AttributionLinks,
    bool RequiresMapboxAccessToken = false,
    bool ShowsMapboxLogo = false,
    bool RequiresJapanMapSources = false)
{
    internal string DisplayName => LocalizationService.GetString(DisplayNameKey);

    /// <summary>
    /// 帰属項目を縦に積む。地理院の航空写真などの追加出典は、狭い地図でも収まるように縦に並べる
    /// </summary>
    internal bool StacksAttribution => RequiresJapanMapSources;
}

/// <summary>
/// 背景地図の一覧（単一情報源）。生成・設定の選択肢・帰属表示は、すべてここを参照する。
/// </summary>
/// <remarks>
/// 並び順は設定画面の選択肢の順で、<see cref="MapTileSourceType"/> の値（設定ファイルへの保存値）と一致させる。
/// 新しい背景は末尾へ足す。
/// </remarks>
internal static class MapBackgroundCatalog
{
    internal const string GsiAttributionUrl = "https://maps.gsi.go.jp/development/ichiran.html";
    private static readonly MapAttributionLink GsiAttribution = new("Geospatial Information Authority of Japan (GSI)", GsiAttributionUrl);

    private static readonly MapBackgroundDescriptor[] Descriptors =
    [
        new(
            MapTileSourceType.OpenStreetMap,
            MapBackgroundKind.Raster,
            "MapBackground.OpenStreetMap",
            [new(MapAttribution.OpenStreetMapText, MapAttribution.OpenStreetMapUrl)]),
        new(
            MapTileSourceType.EsriWorldImagery,
            MapBackgroundKind.Raster,
            "MapBackground.EsriWorldImagery",
            [new(MapAttribution.EsriText, MapAttribution.EsriUrl)]),
        // Mapbox は、Mapbox・OpenStreetMap・地図の修正依頼を、それぞれ別のリンクで示すことを求めている。
        new(
            MapTileSourceType.MapboxSatellite,
            MapBackgroundKind.Raster,
            "MapBackground.MapboxSatellite",
            [
                new("© Mapbox", MapAttribution.MapboxUrl),
                new("© OpenStreetMap", MapAttribution.OpenStreetMapUrl),
                new("Improve this map", MapAttribution.MapboxFeedbackUrl, TracksViewport: true)
            ],
            RequiresMapboxAccessToken: true,
            ShowsMapboxLogo: true),
        // OpenFreeMap の利用条件が求める「OpenFreeMap © OpenMapTiles Data from OpenStreetMap」を、項目ごとのリンクで示す。
        new(
            MapTileSourceType.OpenFreeMapVector,
            MapBackgroundKind.Vector,
            "MapBackground.OpenFreeMapVector",
            [
                new(MapAttribution.OpenFreeMapText, MapAttribution.OpenFreeMapUrl),
                new(MapAttribution.OpenMapTilesText, MapAttribution.OpenMapTilesUrl),
                new(MapAttribution.OpenStreetMapDataText, MapAttribution.OpenStreetMapUrl)
            ]),
        new(MapTileSourceType.GsiStandard, MapBackgroundKind.Raster, "MapBackground.GsiStandard", [GsiAttribution],
            RequiresJapanMapSources: true),
        new(MapTileSourceType.GsiPale, MapBackgroundKind.Raster, "MapBackground.GsiPale",
            [GsiAttribution, new("Shoreline: NIMA Vector Map Level 0 (VMAP0)", GsiAttributionUrl + "#pale2")],
            RequiresJapanMapSources: true),
        new(MapTileSourceType.GsiPhoto, MapBackgroundKind.Raster, "MapBackground.GsiPhoto",
            [
                GsiAttribution,
                new("GRUS imagery (© Axelspace)", GsiAttributionUrl + "#seamlessphoto"),
                new("Landsat 8 (GSI, TSIC, GEO Grid/AIST)", GsiAttributionUrl + "#seamlessphoto"),
                new("Landsat 8 (USGS); GEBCO", GsiAttributionUrl + "#seamlessphoto"),
                new("World imagery: NASA LP DAAC / USGS EROS", "https://lpdaac.usgs.gov/data_access")
            ],
            RequiresJapanMapSources: true),
        new(MapTileSourceType.GsiVector, MapBackgroundKind.Vector, "MapBackground.GsiVector",
            [new("Geospatial Information Authority of Japan (GSI) vector tiles", "https://github.com/gsi-cyberjapan/optimal_bvmap")],
            RequiresJapanMapSources: true)
    ];

    /// <summary>
    /// 設定画面の選択肢の順に並べた、すべての背景
    /// </summary>
    internal static IReadOnlyList<MapBackgroundDescriptor> All => Descriptors;

    /// <summary>
    /// 既定の背景。トークンがない Mapbox の代わりにも使う。
    /// </summary>
    internal static MapBackgroundDescriptor Default => Descriptors[0];

    internal static bool ResolveJapanMapSourcesEnabled(bool? enabled, string? language)
        => enabled ?? (language is null
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja"
            : language.Equals("ja", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("ja-", StringComparison.OrdinalIgnoreCase));

    internal static MapTileSourceType ResolveSource(MapTileSourceType type, bool japanMapSourcesEnabled)
        => Enum.IsDefined(type) && (japanMapSourcesEnabled || !Get(type).RequiresJapanMapSources)
            ? type
            : Default.Type;

    /// <exception cref="ArgumentOutOfRangeException">定義にない値のとき</exception>
    internal static MapBackgroundDescriptor Get(MapTileSourceType type)
    {
        var index = (int)type;
        if (index < 0 || index >= Descriptors.Length || Descriptors[index].Type != type)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "The map background is not defined.");
        }

        return Descriptors[index];
    }

}
