using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// 要素のポインター入力を <see cref="PanZoomController"/> へ渡す。
/// ドラッグ・ピンチ・慣性は OS のジェスチャー認識（<c>ManipulationMode</c>）に任せ、ホイールは回転量に比例してズームする。
/// 描画への反映は呼び出し側が <see cref="PanZoomController.Changed"/> で行う。画像プレビューのほか、地図にも同じ形で使える。
/// </summary>
internal sealed class PanZoomInputAdapter
{
    private readonly UIElement _target;
    private readonly PanZoomController _controller;
    private readonly double _wheelFactorPerNotch;
    private readonly ManipulationModes _previousManipulationMode;
    private bool _isAttached = true;

    internal PanZoomInputAdapter(UIElement target, PanZoomController controller, double wheelFactorPerNotch)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        if (!double.IsFinite(wheelFactorPerNotch) || wheelFactorPerNotch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wheelFactorPerNotch), wheelFactorPerNotch, "Factor per notch must be finite and positive.");
        }

        _wheelFactorPerNotch = wheelFactorPerNotch;
        _previousManipulationMode = target.ManipulationMode;

        _target.ManipulationMode = ManipulationModes.Scale
            | ManipulationModes.ScaleInertia
            | ManipulationModes.TranslateX
            | ManipulationModes.TranslateY
            | ManipulationModes.TranslateInertia;
        _target.ManipulationDelta += OnManipulationDelta;
        _target.PointerWheelChanged += OnPointerWheelChanged;
    }

    /// <summary>
    /// 入力の受け取りをやめて、接続前の <c>ManipulationMode</c> に戻す。2 回目以降は何もしない
    /// </summary>
    internal void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _isAttached = false;
        _target.ManipulationDelta -= OnManipulationDelta;
        _target.PointerWheelChanged -= OnPointerWheelChanged;
        _target.ManipulationMode = _previousManipulationMode;
    }

    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        var delta = e.Delta;
        var pivot = new PanZoomPoint(e.Position.X, e.Position.Y);

        // OS・デバイス由来の値は信頼できない。異常なフレームは、例外でアプリを落とさず無視する
        if (PanZoomController.IsValidManipulation(pivot, delta.Scale, delta.Translation.X, delta.Translation.Y))
        {
            _controller.Manipulate(pivot, delta.Scale, delta.Translation.X, delta.Translation.Y);
        }

        e.Handled = true;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_target);
        if (point.Properties.IsHorizontalMouseWheel || point.Properties.MouseWheelDelta == 0)
        {
            return;
        }

        var anchor = new PanZoomPoint(point.Position.X, point.Position.Y);
        if (PanZoomController.IsValidPoint(anchor))
        {
            _controller.ZoomByWheel(point.Properties.MouseWheelDelta, _wheelFactorPerNotch, anchor);
        }

        e.Handled = true;
    }
}
