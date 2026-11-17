namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>画面上の矩形（論理ピクセル。原点は左上、y は下向き）。</summary>
internal readonly record struct ScreenRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
}

/// <summary>
/// 表示範囲画像を、現在の視点の画面上のどこへ・どの品質で描くかの計算。
/// 画像は描いた時点の視点で作られるため、操作中は視点とずれる。範囲から画面矩形を求めて拡大・移動させることで、
/// 直前の画像が地図の動きに追従する。回転は扱わない（地図の回転は無効）。
/// </summary>
internal static class ViewExtentPlacement
{
    /// <summary>
    /// これ未満の拡大率（縮小表示）では、ミップマップを使う。使わないと縮小のエイリアシングで線や文字が崩れ、
    /// 位置がずれて見える。
    /// </summary>
    internal const double MipmapScaleThreshold = 0.9;

    /// <summary>範囲を、視点（中心・解像度・画面サイズ）での画面矩形へ変換する。</summary>
    public static ScreenRect ToScreenRect(
        GeoExtent extent,
        double viewCenterX,
        double viewCenterY,
        double viewResolution,
        double viewWidthDip,
        double viewHeightDip)
    {
        var left = ((extent.MinX - viewCenterX) / viewResolution) + (viewWidthDip / 2);
        var right = ((extent.MaxX - viewCenterX) / viewResolution) + (viewWidthDip / 2);
        var top = ((viewCenterY - extent.MaxY) / viewResolution) + (viewHeightDip / 2);
        var bottom = ((viewCenterY - extent.MinY) / viewResolution) + (viewHeightDip / 2);
        return new ScreenRect(left, top, right, bottom);
    }

    /// <summary>
    /// 画像の拡大率（描画先の物理ピクセル幅 / 画像の画素幅）。論理ピクセルで比べない。DPI が 100% を超えると、
    /// 同じ画素寸法でも論理ピクセルでは 1/密度（150% で 0.67）になり、等倍なのに縮小用の補間へ入って遅くなる。
    /// </summary>
    public static double ComputeScale(ScreenRect destination, double deviceScale, int imageWidthPixels)
    {
        if (imageWidthPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidthPixels), imageWidthPixels, "画像の画素幅は正の値でなければならない。");
        }

        return destination.Width * deviceScale / imageWidthPixels;
    }

    public static bool ShouldUseMipmaps(double scale) => scale < MipmapScaleThreshold;
}
