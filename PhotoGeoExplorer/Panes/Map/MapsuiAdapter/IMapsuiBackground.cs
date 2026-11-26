using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// Mapsui の地図へ載せられる背景。<see cref="MapsuiMapSurface"/> だけがこの契約を使う。
/// 背景が持つレイヤーの破棄は、背景の <see cref="IDisposable.Dispose"/> が行う。
/// </summary>
internal interface IMapsuiBackground : IMapBackground
{
    /// <summary>背景のレイヤー（下から順）。写真マーカーなど背景以外のレイヤーは含まない。</summary>
    IReadOnlyList<ILayer> Layers { get; }

    /// <summary>
    /// 地図描画の開始直前に呼ばれる。対象ビューポートのフレーム反映完了を通知すべき条件を満たす場合は、
    /// 描画完了直後に呼び出すコールバックを返す。未準備・古いビューポート・通知済みの場合は <see langword="null"/> を返す。
    /// </summary>
    Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService);

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
}
