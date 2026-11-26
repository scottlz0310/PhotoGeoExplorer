namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 地図の移動の慣性の滑り方を、OS の慣性へ指定する減速度として決める純粋な計算（#362 段階 1）。
/// </summary>
/// <remarks>
/// 従来の地図（Mapsui 5.2.0 の <c>FlingTracker</c> と <c>FlingAnimation</c>）の滑る距離に合わせる。
/// 速さが 1 論理ピクセル／ミリ秒以下なら滑らず、4 を上限とし、距離は (2/π) × 速さ² × 100。
/// OS の慣性は一定の減速なので、距離が一致する減速度は π/400 になる。上限を超える速さは、距離が上限の速さのときと
/// 同じになるように減速度を大きくする（OS の慣性は、減速度の指定だけで距離を抑える）。
/// </remarks>
internal static class MapInertia
{
    internal const double MinSpeed = 1.0;
    internal const double MaxSpeed = 4.0;

    private const double Deceleration = Math.PI / 400;

    // 滑らせないときに指定する、十分に大きい減速度（滑る距離は 1 論理ピクセル未満）
    private const double StopDeceleration = 100;

    /// <summary>
    /// 指やボタンが離れたときの速度（論理ピクセル／ミリ秒）から、減速度（論理ピクセル／ミリ秒²。正の有限値）を返す。
    /// </summary>
    internal static double ResolveDeceleration(double velocityX, double velocityY)
    {
        var speed = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
        if (!double.IsFinite(speed) || speed <= MinSpeed)
        {
            return StopDeceleration;
        }

        var excess = Math.Max(1.0, speed / MaxSpeed);
        return Deceleration * excess * excess;
    }
}
