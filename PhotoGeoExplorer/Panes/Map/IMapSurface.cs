using System;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図の表示面。View と ViewModel はこの契約だけを見て、地図ライブラリ（Mapsui）の型を知らない（#362）。
/// 実装は <c>MapsuiAdapter</c> に置く。
/// </summary>
internal interface IMapSurface
{
    /// <summary>現在の視点。任意のスレッドから読める。</summary>
    MapViewState View { get; }

    /// <summary>視点が変わったとき。アニメーション中は UI 以外のスレッドから呼ばれる。</summary>
    event EventHandler? ViewChanged;

    /// <summary>
    /// 視点の中心と解像度を即座に置き換える（アニメーションなし）。UI スレッドから呼ぶ。
    /// </summary>
    /// <param name="centerX">中心の X（Web メルカトル、メートル）</param>
    /// <param name="centerY">中心の Y（Web メルカトル、メートル）</param>
    /// <param name="resolution">表示解像度（メートル／論理ピクセル）</param>
    void SetView(double centerX, double centerY, double resolution);
}
