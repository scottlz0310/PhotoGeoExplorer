using System;
using System.Collections.Generic;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図の表示面。View と ViewModel はこの契約だけを見て、地図ライブラリ（Mapsui）の型を知らない（#362）。
/// 実装は <c>MapsuiAdapter</c> に置く。UI スレッドから使う（<see cref="View"/> の読み取りと <see cref="ViewChanged"/> を除く）。
/// </summary>
internal interface IMapSurface : IDisposable
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

    /// <summary>いま地図に置いているマーカー。</summary>
    IReadOnlyList<MapMarker> Markers { get; }

    /// <summary>マーカーを 1 件だけ置き、その位置を中心にして、指定のズーム段階へ移る。</summary>
    /// <param name="marker">置くマーカー</param>
    /// <param name="zoomLevel">ズーム段階（0 起点。表示面が持つ段階の範囲に丸める）</param>
    void ShowMarker(MapMarker marker, int zoomLevel);

    /// <summary>マーカーを複数置き、すべてが収まるように視点を移す。</summary>
    void ShowMarkers(IReadOnlyList<MapMarker> markers);

    /// <summary>マーカーをすべて外す（視点は動かさない）。</summary>
    void ClearMarkers();

    /// <summary>
    /// 表示面上の位置（論理ピクセル）を、地球上の位置へ変換する。
    /// </summary>
    /// <param name="screenX">表示面の左端からの X</param>
    /// <param name="screenY">表示面の上端からの Y</param>
    /// <param name="position">変換した位置</param>
    /// <returns>変換できたとき true</returns>
    bool TryGetGeoPosition(double screenX, double screenY, out GeoPoint position);

    /// <summary>
    /// パンを止める。矩形選択中に地図が動かないようにするために使う。
    /// 止める前の状態を覚えておき、<see cref="RestorePan"/> で戻す。重ねて呼んでも、覚える状態は最初の 1 回だけ。
    /// </summary>
    void LockPan();

    /// <summary><see cref="LockPan"/> の前の状態へ戻す。止めていなければ何もしない。</summary>
    void RestorePan();

    /// <summary>矩形選択の枠（対角の 2 点）を表示する。表示中なら置き換える。</summary>
    void ShowSelectionRectangle(GeoPoint corner1, GeoPoint corner2);

    /// <summary>矩形選択の枠を消す。表示していなければ何もしない。</summary>
    void ClearSelectionRectangle();
}
