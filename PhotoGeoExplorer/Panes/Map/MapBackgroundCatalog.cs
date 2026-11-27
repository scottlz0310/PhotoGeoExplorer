using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    Vector,

    /// <summary>ラスタの航空写真の上へ、道路だけを描いた透過のベクター画像を重ねる</summary>
    Composite
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
/// <param name="ImagerySource">合成の背景で、下に敷く航空写真（ラスタ）の背景</param>
internal sealed record MapBackgroundDescriptor(
    MapTileSourceType Type,
    MapBackgroundKind Kind,
    string DisplayNameKey,
    IReadOnlyList<MapAttributionLink> AttributionLinks,
    bool RequiresMapboxAccessToken = false,
    bool ShowsMapboxLogo = false,
    bool RequiresJapanMapSources = false,
    MapTileSourceType? ImagerySource = null)
{
    internal string DisplayName => LocalizationService.GetString(DisplayNameKey);

    /// <summary>
    /// 帰属表示の、行ごとの項目数（上の行から順。合計は <see cref="AttributionLinks"/> の件数）。
    /// 地理院の追加の出典は、狭い地図でも収まるように 1 件ずつ縦に並べる。合成の背景は、航空写真と道路データを別の行にする
    /// </summary>
    internal IReadOnlyList<int> AttributionRows
    {
        get
        {
            if (RequiresJapanMapSources)
            {
                return Enumerable.Repeat(1, AttributionLinks.Count).ToArray();
            }

            if (ImagerySource is { } imagery)
            {
                var imageryCount = MapBackgroundCatalog.Get(imagery).AttributionLinks.Count;
                return [imageryCount, AttributionLinks.Count - imageryCount];
            }

            return [AttributionLinks.Count];
        }
    }
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

    private static readonly MapAttributionLink[] EsriAttribution = [new(MapAttribution.EsriText, MapAttribution.EsriUrl)];

    // Mapbox は、Mapbox・OpenStreetMap・地図の修正依頼を、それぞれ別のリンクで示すことを求めている。
    private static readonly MapAttributionLink[] MapboxAttribution =
    [
        new("© Mapbox", MapAttribution.MapboxUrl),
        new("© OpenStreetMap", MapAttribution.OpenStreetMapUrl),
        new("Improve this map", MapAttribution.MapboxFeedbackUrl, TracksViewport: true)
    ];

    // OpenFreeMap の利用条件が求める「OpenFreeMap © OpenMapTiles Data from OpenStreetMap」を、項目ごとのリンクで示す。
    private static readonly MapAttributionLink[] OpenFreeMapAttribution =
    [
        new(MapAttribution.OpenFreeMapText, MapAttribution.OpenFreeMapUrl),
        new(MapAttribution.OpenMapTilesText, MapAttribution.OpenMapTilesUrl),
        new(MapAttribution.OpenStreetMapDataText, MapAttribution.OpenStreetMapUrl)
    ];

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
            EsriAttribution),
        new(
            MapTileSourceType.MapboxSatellite,
            MapBackgroundKind.Raster,
            "MapBackground.MapboxSatellite",
            MapboxAttribution,
            RequiresMapboxAccessToken: true,
            ShowsMapboxLogo: true),
        new(
            MapTileSourceType.OpenFreeMapVector,
            MapBackgroundKind.Vector,
            "MapBackground.OpenFreeMapVector",
            OpenFreeMapAttribution),
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
            RequiresJapanMapSources: true),
        new(
            MapTileSourceType.EsriWorldImageryRoads,
            MapBackgroundKind.Composite,
            "MapBackground.EsriWorldImageryRoads",
            [.. EsriAttribution, .. OpenFreeMapAttribution],
            ImagerySource: MapTileSourceType.EsriWorldImagery),
        new(
            MapTileSourceType.MapboxSatelliteRoads,
            MapBackgroundKind.Composite,
            "MapBackground.MapboxSatelliteRoads",
            [.. MapboxAttribution, .. OpenFreeMapAttribution],
            RequiresMapboxAccessToken: true,
            ShowsMapboxLogo: true,
            ImagerySource: MapTileSourceType.MapboxSatellite)
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
        => Enum.IsDefined(type) && IsListed(Get(type), japanMapSourcesEnabled)
            ? type
            : Default.Type;

    /// <summary>背景の選択肢に出す（日本国内向けの背景は、有効にしたときだけ出す）</summary>
    internal static bool IsListed(MapBackgroundDescriptor descriptor, bool japanMapSourcesEnabled)
        => japanMapSourcesEnabled || !descriptor.RequiresJapanMapSources;

    /// <summary>表示できる（アクセストークンが要る背景は、設定されているときだけ表示できる）</summary>
    internal static bool IsUsable(MapBackgroundDescriptor descriptor, bool hasMapboxAccessToken)
        => hasMapboxAccessToken || !descriptor.RequiresMapboxAccessToken;

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
