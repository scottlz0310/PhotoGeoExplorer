using System;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図の背景。ラスタ・ベクターなど描き方の違いを隠し、<see cref="MapPaneViewModel"/> は背景の種類を知らずに扱う。
/// 地図ライブラリ（Mapsui）の型は出さない。表示面（<see cref="IMapSurface"/>）が背景を地図へ載せる。
/// </summary>
internal interface IMapBackground : IDisposable
{
    /// <summary>この背景が表示する地図の種類。帰属表示の選択に使う。</summary>
    MapTileSourceType SourceType { get; }

    /// <summary>表示中の帰属表示の文言。</summary>
    string AttributionText { get; }

    /// <summary>表示に必要な読み込み・描画の待機中かどうか。待機中は地図画像を保存できない。</summary>
    bool IsBusy { get; }

    /// <summary><see cref="IsBusy"/> が変化したとき。任意のスレッドから呼ばれる。</summary>
    event EventHandler? BusyChanged;

    /// <summary>最新の表示範囲に対するフレームの画面描画（反映）が完了したときに発火する。描画失敗・タイル欠損・古い過渡フレーム・描画反映前では発火しない。</summary>
    event EventHandler? FrameRendered;
}
