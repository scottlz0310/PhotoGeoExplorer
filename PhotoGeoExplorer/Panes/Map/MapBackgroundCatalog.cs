using System;
using System.Collections.Generic;
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
/// <param name="Kind">描画方式</param>
/// <param name="AttributionLinks">帰属表示の項目（最大 <see cref="MapAttribution.MaxLinkCount"/> 件）</param>
/// <param name="RequiresMapboxAccessToken">Mapbox のアクセストークンがないと表示できない</param>
/// <param name="ShowsMapboxLogo">帰属表示に Mapbox のロゴが必須</param>
internal sealed record MapBackgroundDescriptor(
    MapTileSourceType Type,
    MapBackgroundKind Kind,
    IReadOnlyList<MapAttributionLink> AttributionLinks,
    bool RequiresMapboxAccessToken = false,
    bool ShowsMapboxLogo = false);

/// <summary>
/// 背景地図の一覧（単一情報源）。生成・設定の選択肢・帰属表示は、すべてここを参照する。
/// </summary>
/// <remarks>
/// 並び順は設定画面の選択肢の順で、<see cref="MapTileSourceType"/> の値（設定ファイルへの保存値）と一致させる。
/// 新しい背景は末尾へ足す。
/// </remarks>
internal static class MapBackgroundCatalog
{
    private static readonly MapBackgroundDescriptor[] Descriptors =
    [
        new(
            MapTileSourceType.OpenStreetMap,
            MapBackgroundKind.Raster,
            [new(MapAttribution.OpenStreetMapText, MapAttribution.OpenStreetMapUrl)]),
        new(
            MapTileSourceType.EsriWorldImagery,
            MapBackgroundKind.Raster,
            [new(MapAttribution.EsriText, MapAttribution.EsriUrl)]),
        // Mapbox は、Mapbox・OpenStreetMap・地図の修正依頼を、それぞれ別のリンクで示すことを求めている。
        new(
            MapTileSourceType.MapboxSatellite,
            MapBackgroundKind.Raster,
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
            [
                new(MapAttribution.OpenFreeMapText, MapAttribution.OpenFreeMapUrl),
                new(MapAttribution.OpenMapTilesText, MapAttribution.OpenMapTilesUrl),
                new(MapAttribution.OpenStreetMapDataText, MapAttribution.OpenStreetMapUrl)
            ])
    ];

    /// <summary>
    /// 設定画面の選択肢の順に並べた、すべての背景
    /// </summary>
    internal static IReadOnlyList<MapBackgroundDescriptor> All => Descriptors;

    /// <summary>
    /// 既定の背景。トークンがない Mapbox の代わりにも使う。
    /// </summary>
    internal static MapBackgroundDescriptor Default => Descriptors[0];

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

    /// <summary>
    /// 設定画面の選択肢の位置から背景を引く。範囲外は既定の背景にする（ラジオボタンの選択解除で -1 が来る）。
    /// </summary>
    internal static MapBackgroundDescriptor FromIndex(int index)
    {
        return index >= 0 && index < Descriptors.Length ? Descriptors[index] : Default;
    }

    internal static int IndexOf(MapTileSourceType type)
    {
        return Array.IndexOf(Descriptors, Get(type));
    }
}
