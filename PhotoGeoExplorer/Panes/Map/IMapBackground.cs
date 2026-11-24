using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図の背景。ラスタ・ベクターなど描き方の違いを隠し、<see cref="MapPaneViewModel"/> は背景の種類を知らずに扱う。
/// 背景が持つレイヤーの破棄は、背景の <see cref="IDisposable.Dispose"/> が行う。
/// </summary>
internal interface IMapBackground : IDisposable
{
    /// <summary>背景のレイヤー（下から順）。写真マーカーなど背景以外のレイヤーは含まない。</summary>
    IReadOnlyList<ILayer> Layers { get; }

    /// <summary>表示中の帰属表示の文言。</summary>
    string AttributionText { get; }

    /// <summary>表示に必要な読み込み・描画の待機中かどうか。待機中は地図画像を保存できない。</summary>
    bool IsBusy { get; }

    /// <summary><see cref="IsBusy"/> が変化したとき。任意のスレッドから呼ばれる。</summary>
    event EventHandler? BusyChanged;

    /// <summary>最新の表示範囲に対するフレームの画面描画（反映）が完了したときに発火する。描画失敗・タイル欠損・古い過渡フレーム・描画反映前では発火しない。</summary>
    event EventHandler? FrameRendered;

    /// <summary>
    /// 地図描画の開始直前に呼ばれる。対象ビューポートのフレーム反映完了を通知すべき条件を満たす場合は、
    /// 描画完了直後に呼び出すコールバックを返す。未準備・古いビューポート・通知済みの場合は <see langword="null"/> を返す。
    /// </summary>
    Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers);

    /// <summary>
    /// 地図へ載せた後に呼ぶ。視点に応じて描き直す背景（ベクター）は、ここで視点の監視と最初の描画を始める。
    /// ラスタは何もしない。視点の通知は UI スレッド以外から来るため、密度は取得関数で読む。
    /// </summary>
    /// <param name="map">背景のレイヤーを載せた地図。</param>
    /// <param name="getPixelDensity">画素密度（物理ピクセル／論理ピクセル）の取得。任意のスレッドから呼ばれる。</param>
    void Attach(Mapsui.Map map, Func<double> getPixelDensity);

    /// <summary>
    /// 画素密度が変わったことを伝える（ウィンドウを別の表示倍率のモニターへ動かした場合など）。
    /// 視点に応じて描き直す背景は、新しい密度で描き直す。ラスタは何もしない。UI スレッドから呼ぶ。
    /// </summary>
    void OnPixelDensityChanged();

    /// <summary>
    /// PNG 保存用のレイヤーを用意する。表示中のレイヤーには触らない。
    /// ラスタは表示中のレイヤーをそのまま返し、ベクターは保存時のビューポートと密度で確定描画した別レイヤーを返す。
    /// </summary>
    Task<IMapExportLayers> CreateExportLayersAsync(Viewport viewport, float pixelDensity, CancellationToken cancellationToken);
}
