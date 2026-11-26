namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 慣性の滑り方。どちらか一方だけを指定する。
/// </summary>
/// <param name="Deceleration">減速度（論理ピクセル／ミリ秒²）</param>
/// <param name="Displacement">滑る距離（論理ピクセル）</param>
internal readonly record struct InertiaBehavior(double? Deceleration, double? Displacement);

/// <summary>
/// 地図の慣性の滑り方を決める純粋な計算（#362 段階 1）。
/// </summary>
/// <remarks>
/// 従来の地図（Mapsui 5.2.0 の <c>FlingTracker</c> と <c>FlingAnimation</c>）の滑る距離に合わせる。
/// 速さが 1 論理ピクセル／ミリ秒以下なら滑らず、4 を上限とし、距離は (2/π) × 速さ² × 100。
/// OS の慣性は一定の減速なので、距離が一致する減速度は π/400 になる。
/// </remarks>
internal static class MapInertia
{
    internal const double MinSpeed = 1.0;
    internal const double MaxSpeed = 4.0;

    private const double Deceleration = Math.PI / 400;

    // 滑らせないときに指定する、十分に大きい減速度（滑る距離は 1 論理ピクセル未満）
    private const double StopDeceleration = 100;

    /// <summary>指やボタンが離れたときの速さ（論理ピクセル／ミリ秒）から、滑り方を返す。</summary>
    internal static InertiaBehavior Resolve(double velocityX, double velocityY)
    {
        var speed = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
        if (!double.IsFinite(speed) || speed <= MinSpeed)
        {
            return new InertiaBehavior(StopDeceleration, null);
        }

        return speed <= MaxSpeed
            ? new InertiaBehavior(Deceleration, null)
            : new InertiaBehavior(null, MaxSpeed * MaxSpeed / (2 * Deceleration));
    }
}
