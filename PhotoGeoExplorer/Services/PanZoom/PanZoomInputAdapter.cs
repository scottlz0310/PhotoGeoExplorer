using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// 要素のポインター入力を <see cref="IPanZoomTarget"/> へ渡す。
/// ドラッグ・ピンチ・慣性は OS のジェスチャー認識（<c>ManipulationMode</c>）に任せる。回転は受けない。
/// 適用の順序は <see cref="PanZoomInputSequencer"/> が整える。画像プレビューと地図で共用する。
/// </summary>
internal sealed class PanZoomInputAdapter
{
    // 拡大の慣性を実質的に止める、十分に大きい減速度
    private const double StopExpansionDeceleration = 100;

    private readonly UIElement _target;
    private readonly PanZoomInputSequencer _sequencer;
    private readonly Func<double, double, InertiaBehavior>? _inertia;
    private readonly ManipulationModes _previousManipulationMode;
    private bool _isAttached = true;

    /// <param name="target">入力を受ける要素</param>
    /// <param name="receiver">入力の受け手</param>
    /// <param name="inertia">
    /// 移動の慣性の滑り方を、離した時点の速度（論理ピクセル／ミリ秒）から決める。指定した場合は、拡大の慣性を止め、
    /// 慣性中のホイールは慣性を打ち切ってから適用する。<see langword="null"/> なら OS の既定の慣性を使う。
    /// </param>
    internal PanZoomInputAdapter(UIElement target, IPanZoomTarget receiver, Func<double, double, InertiaBehavior>? inertia = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _sequencer = new PanZoomInputSequencer(receiver, wheelInterruptsInertia: inertia is not null);
        _inertia = inertia;
        _previousManipulationMode = target.ManipulationMode;

        _target.ManipulationMode = ManipulationModes.Scale
            | ManipulationModes.ScaleInertia
            | ManipulationModes.TranslateX
            | ManipulationModes.TranslateY
            | ManipulationModes.TranslateInertia;
        _target.ManipulationInertiaStarting += OnManipulationInertiaStarting;
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
        _target.ManipulationInertiaStarting -= OnManipulationInertiaStarting;
        _target.ManipulationDelta -= OnManipulationDelta;
        _target.ManipulationCompleted -= OnManipulationCompleted;
        _target.PointerWheelChanged -= OnPointerWheelChanged;
        _target.ManipulationMode = _previousManipulationMode;
    }

    // 要素が先に設定した慣性（MapControl の減速度）がある場合は、後から購読するこのハンドラで置き換える
    private void OnManipulationInertiaStarting(object sender, ManipulationInertiaStartingRoutedEventArgs e)
    {
        _sequencer.StartInertia();
        if (_inertia is null)
        {
            return;
        }

        var behavior = _inertia(e.Velocities.Linear.X, e.Velocities.Linear.Y);
        if (behavior.Deceleration is { } deceleration)
        {
            e.TranslationBehavior.DesiredDeceleration = deceleration;
        }
        else if (behavior.Displacement is { } displacement)
        {
            // 減速度と距離は、どちらか一方だけを指定する（NaN は指定なし）
            e.TranslationBehavior.DesiredDeceleration = double.NaN;
            e.TranslationBehavior.DesiredDisplacement = displacement;
        }

        e.ExpansionBehavior.DesiredDeceleration = StopExpansionDeceleration;
    }

    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        var delta = e.Delta;
        var pivot = new PanZoomPoint(e.Position.X, e.Position.Y);
        if (!_sequencer.Manipulate(pivot, delta.Scale, delta.Translation.X, delta.Translation.Y, e.IsInertial))
        {
            e.Complete();
        }

        e.Handled = true;
    }

    private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e) => _sequencer.Complete();

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_target);
        if (point.Properties.IsHorizontalMouseWheel || point.Properties.MouseWheelDelta == 0)
        {
            return;
        }

        _sequencer.ZoomByWheel(point.Properties.MouseWheelDelta, new PanZoomPoint(point.Position.X, point.Position.Y));
        e.Handled = true;
    }
}
