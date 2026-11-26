namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// <see cref="PanZoomController"/> を入力の受け手にする。ホイールは回転量に比例してズームする（画像プレビュー）。
/// </summary>
internal sealed class PanZoomControllerTarget : IPanZoomTarget
{
    private readonly PanZoomController _controller;
    private readonly double _wheelFactorPerNotch;

    /// <param name="controller">入力を適用する変換状態</param>
    /// <param name="wheelFactorPerNotch">ホイール 1 ノッチあたりの倍率</param>
    internal PanZoomControllerTarget(PanZoomController controller, double wheelFactorPerNotch)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        if (!double.IsFinite(wheelFactorPerNotch) || wheelFactorPerNotch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wheelFactorPerNotch), wheelFactorPerNotch, "Factor per notch must be finite and positive.");
        }

        _wheelFactorPerNotch = wheelFactorPerNotch;
    }

    public bool Manipulate(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY, bool isInertial)
    {
        _controller.Manipulate(pivot, scaleFactor, translationX, translationY);
        return true;
    }

    public void ZoomByWheel(int wheelDelta, PanZoomPoint anchor)
        => _controller.ZoomByWheel(wheelDelta, _wheelFactorPerNotch, anchor);
}
