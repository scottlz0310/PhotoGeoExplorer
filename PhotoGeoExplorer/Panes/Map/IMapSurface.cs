using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// 画素密度（物理ピクセル／論理ピクセル）。表示倍率が変わったときに設定する。
    /// 値が変わると、視点に応じて描き直す背景は新しい密度で描き直す。任意のスレッドから読める。UI スレッドから設定する。
    /// </summary>
    double PixelDensity { get; set; }

    /// <summary>
    /// 背景を差し替える。表示中の背景のレイヤーを外し、新しい背景のレイヤーを最背面へ載せて、帰属表示を更新する。
    /// 古い背景の破棄は呼び出し側が行う。作成に失敗した背景で表示中の背景を壊さないよう、新しい背景を先に作ってから呼ぶ。
    /// </summary>
    /// <param name="background">この表示面が扱える背景（実装が作ったもの）</param>
    void ReplaceBackground(IMapBackground background);

    /// <summary>
    /// 現在の視点を PNG に描く。表示中のレイヤーには触らない。ベクターの背景は、保存時の視点と密度で確定描画し直す。
    /// </summary>
    /// <param name="pixelDensity">出力の画素密度</param>
    /// <param name="cancellationToken">キャンセル</param>
    /// <returns>PNG のストリーム（先頭へ巻き戻し済み）</returns>
    Task<Stream> RenderPngAsync(float pixelDensity, CancellationToken cancellationToken);
}
