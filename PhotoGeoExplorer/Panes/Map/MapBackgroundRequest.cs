using System;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 背景地図レイヤーの生成要求。背景ごとの追加パラメータをここにまとめる。
/// </summary>
/// <param name="SourceType">背景の種類</param>
/// <param name="UserAgent">HTTP リクエストの User-Agent</param>
/// <param name="MapboxAccessToken">Mapbox の公開アクセストークン（Mapbox 背景のときだけ必須）</param>
/// <param name="AuthenticationFailed">タイル取得が認証エラー（401・403）になったときの通知。任意のスレッドから呼ばれる</param>
/// <param name="TryPostToUi">処理を UI スレッドへ渡す。渡せなかったら false（ベクター背景だけが使い、ベクター背景では必須）</param>
internal sealed record MapBackgroundRequest(
    MapTileSourceType SourceType,
    string UserAgent,
    string? MapboxAccessToken = null,
    Action? AuthenticationFailed = null,
    Func<Action, bool>? TryPostToUi = null);
