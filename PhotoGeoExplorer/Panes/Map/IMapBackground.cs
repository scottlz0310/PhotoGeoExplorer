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

    /// <summary>
    /// PNG 保存用のレイヤーを用意する。表示中のレイヤーには触らない。
    /// ラスタは表示中のレイヤーをそのまま返し、ベクターは保存時のビューポートと密度で確定描画した別レイヤーを返す。
    /// </summary>
    Task<IMapExportLayers> CreateExportLayersAsync(Viewport viewport, float pixelDensity, CancellationToken cancellationToken);
}
