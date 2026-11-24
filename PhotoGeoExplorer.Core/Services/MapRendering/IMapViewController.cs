namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 地図視点の操作結果。
/// </summary>
internal sealed record MapViewCommandResult(
    bool Succeeded,
    MapViewStatus Status,
    string? Error = null);

/// <summary>
/// Mapsui の型を公開せずに地図の表示状態を取得・変更する契約（#362 / #363 / #364）。
/// </summary>
internal interface IMapViewController
{
    /// <summary>現在の地図状態の不変スナップショット。任意のスレッドから取得可能。</summary>
    MapViewStatus CurrentStatus { get; }

    /// <summary>地図状態が変化したときに発火する。任意のスレッドから呼ばれる。</summary>
    event EventHandler<MapViewStatus>? StatusChanged;

    /// <summary>
    /// 指定した位置（緯度・経度）と論理ズーム（小数可）へ即座に移動する。
    /// </summary>
    Task<MapViewCommandResult> SetViewAsync(MapViewTarget target, CancellationToken cancellationToken = default);
}
