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

    internal PanZoomInputAdapter(UIElement target, PanZoomController controller, double wheelFactorPerNotch)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _wheelFactorPerNotch = wheelFactorPerNotch;

        _target.ManipulationMode = ManipulationModes.Scale
            | ManipulationModes.ScaleInertia
            | ManipulationModes.TranslateX
            | ManipulationModes.TranslateY
            | ManipulationModes.TranslateInertia;
        _target.ManipulationDelta += OnManipulationDelta;
        _target.PointerWheelChanged += OnPointerWheelChanged;
    }

    /// <summary>
    /// 入力の受け取りをやめて、要素の設定を元に戻す
    /// </summary>
    internal void Detach()
    {
        _target.ManipulationDelta -= OnManipulationDelta;
        _target.PointerWheelChanged -= OnPointerWheelChanged;
        _target.ManipulationMode = ManipulationModes.System;
    }

    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        var delta = e.Delta;
        _controller.Manipulate(new PanZoomPoint(e.Position.X, e.Position.Y), delta.Scale, delta.Translation.X, delta.Translation.Y);
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_target);
        if (point.Properties.IsHorizontalMouseWheel || point.Properties.MouseWheelDelta == 0)
        {
            return;
        }

        _controller.ZoomByWheel(point.Properties.MouseWheelDelta, _wheelFactorPerNotch, new PanZoomPoint(point.Position.X, point.Position.Y));
        e.Handled = true;
    }
}
