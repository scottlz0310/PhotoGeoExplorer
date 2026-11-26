using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// 要素のポインター入力を <see cref="IPanZoomTarget"/> へ渡す。
/// ドラッグ・ピンチは OS のジェスチャー認識（<c>ManipulationMode</c>）に任せる。回転は受けない。画像プレビューと地図で共用する。
/// </summary>
internal sealed class PanZoomInputAdapter
{
    private readonly UIElement _target;
    private readonly IPanZoomTarget _receiver;
    private readonly ManipulationModes _previousManipulationMode;
    private bool _isAttached = true;

    /// <param name="target">入力を受ける要素</param>
    /// <param name="receiver">入力の受け手</param>
    /// <param name="useSystemInertia">
    /// 離した後の慣性を OS に任せる（移動量として受け手へ届く）か。受け手が自分で慣性を持つ場合は <see langword="false"/>
    /// </param>
    internal PanZoomInputAdapter(UIElement target, IPanZoomTarget receiver, bool useSystemInertia = true)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _previousManipulationMode = target.ManipulationMode;

        var mode = ManipulationModes.Scale | ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        if (useSystemInertia)
        {
            mode |= ManipulationModes.ScaleInertia | ManipulationModes.TranslateInertia;
        }

        _target.ManipulationMode = mode;
        _target.ManipulationDelta += OnManipulationDelta;
        _target.ManipulationCompleted += OnManipulationCompleted;
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
        _target.ManipulationCompleted -= OnManipulationCompleted;
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
            _receiver.Manipulate(pivot, delta.Scale, delta.Translation.X, delta.Translation.Y);
        }

        e.Handled = true;
    }

    private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e) => _receiver.CompleteManipulation();

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
            _receiver.ZoomByWheel(point.Properties.MouseWheelDelta, anchor);
        }

        e.Handled = true;
    }
}
