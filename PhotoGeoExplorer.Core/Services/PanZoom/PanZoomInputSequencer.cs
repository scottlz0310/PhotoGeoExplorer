namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// ジェスチャー・慣性・ホイールの順序を整えて <see cref="IPanZoomTarget"/> へ渡す。UI の型に依存しない。
/// </summary>
/// <remarks>
/// ホイールが慣性を打ち切る設定では、慣性中のホイールをすぐには適用せず、次の慣性のフレームで慣性を打ち切ってから適用する。
/// 入力を受ける要素の側（Mapsui の <c>MapControl</c>）が慣性のフレームごとに実行中のアニメーションを消すため、
/// 打ち切る前に適用すると、ホイールのズームのアニメーションが消される。
/// </remarks>
internal sealed class PanZoomInputSequencer
{
    private readonly IPanZoomTarget _target;
    private readonly bool _wheelInterruptsInertia;
    private readonly List<(int Delta, PanZoomPoint Anchor)> _pendingWheels = [];
    private bool _inertiaActive;

    internal PanZoomInputSequencer(IPanZoomTarget target, bool wheelInterruptsInertia)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _wheelInterruptsInertia = wheelInterruptsInertia;
    }

    /// <summary>指やボタンが離れて、慣性が始まる。</summary>
    internal void StartInertia() => _inertiaActive = true;

    /// <summary>
    /// ジェスチャー 1 回分を適用する。OS やデバイス由来の異常な値は無視する。
    /// </summary>
    /// <returns>慣性を打ち切るべきなら <see langword="false"/></returns>
    internal bool Manipulate(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY, bool isInertial)
    {
        if (!isInertial)
        {
            Complete();
        }
        else if (_pendingWheels.Count > 0)
        {
            Complete();
            return false;
        }

        return !PanZoomController.IsValidManipulation(pivot, scaleFactor, translationX, translationY)
            || _target.Manipulate(pivot, scaleFactor, translationX, translationY, isInertial);
    }

    /// <summary>ホイールを適用する。異常な座標と、回転量 0 は無視する。</summary>
    internal void ZoomByWheel(int wheelDelta, PanZoomPoint anchor)
    {
        if (wheelDelta == 0 || !PanZoomController.IsValidPoint(anchor))
        {
            return;
        }

        if (_wheelInterruptsInertia && _inertiaActive)
        {
            _pendingWheels.Add((wheelDelta, anchor));
            return;
        }

        _target.ZoomByWheel(wheelDelta, anchor);
    }

    /// <summary>ジェスチャーが終わった（慣性の終了を含む）。保留したホイールを適用する。</summary>
    internal void Complete()
    {
        _inertiaActive = false;
        foreach (var (delta, anchor) in _pendingWheels)
        {
            _target.ZoomByWheel(delta, anchor);
        }

        _pendingWheels.Clear();
    }
}
