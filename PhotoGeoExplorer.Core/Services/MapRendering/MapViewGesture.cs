using PhotoGeoExplorer.Services.PanZoom;

namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 地図の視点へ、ジェスチャー 1 回分を適用する純粋な計算（#362 段階 1）。視点の状態は持たない。
/// </summary>
/// <remarks>
/// 回転は扱わない（地図の回転は当面許さない）。将来対応する場合は、<see cref="MapViewState"/> と この計算に回転を足す。
/// </remarks>
internal static class MapViewGesture
{
    /// <summary>
    /// <paramref name="pivot"/> の下にある地点が、拡大のあと <c>pivot + 移動量</c> の位置へ移る視点を返す。
    /// </summary>
    /// <param name="view">適用前の視点。大きさが確定していないときは、そのまま返す</param>
    /// <param name="pivot">拡大の基準（表示面の左上が原点、論理ピクセル）</param>
    /// <param name="scaleFactor">倍率の変化（2 なら 2 倍に拡大）</param>
    /// <param name="translationX">X の移動量（論理ピクセル）</param>
    /// <param name="translationY">Y の移動量（論理ピクセル）</param>
    /// <exception cref="ArgumentOutOfRangeException">倍率が正の有限値でないか、座標・移動量が有限でないとき</exception>
    internal static MapViewState Apply(MapViewState view, PanZoomPoint pivot, double scaleFactor, double translationX, double translationY)
    {
        if (!PanZoomController.IsValidManipulation(pivot, scaleFactor, translationX, translationY))
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor), "The pivot, scale factor, and translation must be finite, and the scale factor must be positive.");
        }

        if (!view.HasSize)
        {
            return view;
        }

        var worldX = view.CenterX + ((pivot.X - (view.WidthDip / 2)) * view.Resolution);
        var worldY = view.CenterY - ((pivot.Y - (view.HeightDip / 2)) * view.Resolution);
        var resolution = view.Resolution / scaleFactor;
        return view with
        {
            Resolution = resolution,
            CenterX = worldX - ((pivot.X + translationX - (view.WidthDip / 2)) * resolution),
            CenterY = worldY + ((pivot.Y + translationY - (view.HeightDip / 2)) * resolution)
        };
    }
}
