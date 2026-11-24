namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// パン・ズームの変換状態を保持し、入力（移動量・倍率変化・ホイール）を制約つきで適用する。
/// UI の型に依存せず、入力の受け取りと描画への反映は呼び出し側（アダプタ）が担う。
/// </summary>
/// <remarks>
/// 倍率 1 は、コンテンツ全体がビューポートに収まる「ウィンドウに合わせた」状態を指す。
/// コンテンツ座標は、倍率 1 のときの表示座標と一致させる（ビューポートの左上が原点）。
/// そのため、ビューポートの大きさが変わっても倍率は相対値のまま保たれ、表示倍率（DPI）の変化を補正する必要がない。
/// </remarks>
internal sealed class PanZoomController
{
    // Windows のマウスホイール 1 ノッチ分の WHEEL_DELTA
    private const double WheelNotch = 120.0;

    private readonly double _minScale;
    private readonly double _maxScale;
    private PanZoomSize _viewport;
    private PanZoomRect _contentBounds;
    private PanZoomTransform _transform = PanZoomTransform.Identity;

    // レイアウトが空になった間も、見ていたコンテンツ上の位置を覚えておく（コンテンツの縦横比に対する 0〜1 の値）
    private PanZoomPoint? _pendingCenter;

    /// <param name="minScale">最小倍率。ウィンドウに合わせた状態（1）以下</param>
    /// <param name="maxScale">最大倍率。ウィンドウに合わせた状態（1）以上</param>
    internal PanZoomController(double minScale, double maxScale)
    {
        if (!double.IsFinite(minScale) || minScale <= 0 || minScale > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minScale), minScale, "Minimum scale must be finite and within (0, 1].");
        }

        if (!double.IsFinite(maxScale) || maxScale < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxScale), maxScale, "Maximum scale must be finite and at least 1.");
        }

        _minScale = minScale;
        _maxScale = maxScale;
    }

    /// <summary>
    /// 変換が変わったときに発火する。変化がない操作では発火しない
    /// </summary>
    internal event EventHandler<PanZoomTransform>? Changed;

    internal PanZoomTransform Transform => _transform;

    internal PanZoomSize Viewport => _viewport;

    /// <summary>
    /// 倍率 1 のときにコンテンツが占める矩形（ビューポート内に縦横比を保って収め、中央に置いたもの）
    /// </summary>
    internal PanZoomRect ContentBounds => _contentBounds;

    /// <summary>
    /// ビューポートとコンテンツの大きさを設定する。ビューポートの中心にあるコンテンツ上の位置は保たれる。
    /// コンテンツが別のものに替わる場合は、呼び出し側が <see cref="Fit"/> を呼ぶ。
    /// </summary>
    /// <remarks>
    /// どちらかが空（レイアウト前・非表示など）の間は変換を変更しない。表示が戻ったときに拡大状態を失わないため。
    /// </remarks>
    internal void SetLayout(PanZoomSize viewport, PanZoomSize contentSize)
    {
        var newBounds = viewport.IsEmpty || contentSize.IsEmpty
            ? default
            : CalculateFitBounds(viewport, contentSize);

        // 空の間は、空になる直前に退避した中心を引き継ぐ。別の大きさで戻っても、見ていた位置へ戻せる
        var preserved = CanOperate ? NormalizeCenter() : _pendingCenter;

        _viewport = viewport;
        _contentBounds = newBounds;

        if (newBounds.IsEmpty)
        {
            _pendingCenter = preserved;
            return;
        }

        _pendingCenter = null;
        if (preserved is not { } center)
        {
            Apply(Clamp(_transform));
            return;
        }

        var contentCenter = new PanZoomPoint(
            newBounds.X + center.X * newBounds.Width,
            newBounds.Y + center.Y * newBounds.Height);
        Apply(Clamp(new PanZoomTransform(
            _transform.Scale,
            viewport.Width / 2 - contentCenter.X * _transform.Scale,
            viewport.Height / 2 - contentCenter.Y * _transform.Scale)));
    }

    /// <summary>
    /// ウィンドウに合わせた状態（倍率 1・中央）に戻す
    /// </summary>
    internal void Fit()
    {
        _pendingCenter = null;
        Apply(PanZoomTransform.Identity);
    }

    /// <summary>
    /// 表示座標での移動量だけパンする
    /// </summary>
    internal void PanBy(double deltaX, double deltaY)
    {
        RequireFinite(deltaX, nameof(deltaX));
        RequireFinite(deltaY, nameof(deltaY));

        if (!CanOperate)
        {
            return;
        }

        Apply(Clamp(_transform with
        {
            TranslateX = _transform.TranslateX + deltaX,
            TranslateY = _transform.TranslateY + deltaY,
        }));
    }

    /// <summary>
    /// 指定した表示座標を動かさずに、倍率を factor 倍する（上下限で丸める）
    /// </summary>
    internal void ZoomAt(double factor, PanZoomPoint anchor)
    {
        RequireFinitePositive(factor, nameof(factor));
        RequireFinite(anchor, nameof(anchor));

        ApplyManipulation(anchor, factor, 0, 0);
    }

    /// <summary>
    /// ビューポートの中心を動かさずに、倍率を factor 倍する
    /// </summary>
    internal void ZoomAtCenter(double factor)
    {
        RequireFinitePositive(factor, nameof(factor));

        // 空や非有限のビューポートは中心が定まらないため、操作できる状態になってから座標を求める
        if (!CanOperate)
        {
            return;
        }

        ZoomAt(factor, new PanZoomPoint(_viewport.Width / 2, _viewport.Height / 2));
    }

    /// <summary>
    /// ホイールの回転量を倍率に換算して、カーソル位置を基準にズームする。
    /// 標準の 1 ノッチ（120）で <paramref name="factorPerNotch"/> 倍になり、タッチパッドの細かい値では比例して滑らかに変わる。
    /// </summary>
    internal void ZoomByWheel(double wheelDelta, double factorPerNotch, PanZoomPoint anchor)
    {
        RequireFinite(wheelDelta, nameof(wheelDelta));
        RequireFinitePositive(factorPerNotch, nameof(factorPerNotch));
        RequireFinite(anchor, nameof(anchor));

        // 有限の回転量でも累乗が溢れる（Infinity）か 0 に潰れることがある。どちらも上下限で丸まる向きなので、有限の端へ寄せる
        var factor = Math.Pow(factorPerNotch, wheelDelta / WheelNotch);
        factor = double.IsPositiveInfinity(factor) ? double.MaxValue : Math.Max(factor, double.Epsilon);

        ApplyManipulation(anchor, factor, 0, 0);
    }

    /// <summary>
    /// ジェスチャーの 1 回分の変化（ピンチなら倍率と中心、ドラッグなら移動量）を、1 回の変更として適用する。
    /// pivot を基準に倍率を掛けたあと、移動量を加える。
    /// </summary>
    internal void Manipulate(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY)
    {
        RequireFinitePositive(scaleFactor, nameof(scaleFactor));
        RequireFinite(pivot, nameof(pivot));
        RequireFinite(translationX, nameof(translationX));
        RequireFinite(translationY, nameof(translationY));

        ApplyManipulation(pivot, scaleFactor, translationX, translationY);
    }

    private void ApplyManipulation(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY)
    {
        if (!CanOperate)
        {
            return;
        }

        var newScale = Math.Clamp(_transform.Scale * scaleFactor, _minScale, _maxScale);
        var effectiveFactor = newScale / _transform.Scale;

        Apply(Clamp(new PanZoomTransform(
            newScale,
            pivot.X - (pivot.X - _transform.TranslateX) * effectiveFactor + translationX,
            pivot.Y - (pivot.Y - _transform.TranslateY) * effectiveFactor + translationY)));
    }

    // NaN や Infinity が変換に入ると、以後の計算がすべて NaN になり Fit() まで復帰できない。入口で拒否する
    private static void RequireFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite.");
        }
    }

    private static void RequireFinite(PanZoomPoint value, string paramName)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Point coordinates must be finite.");
        }
    }

    private static void RequireFinitePositive(double value, string paramName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite and positive.");
        }
    }

    private bool CanOperate => !_viewport.IsEmpty && !_contentBounds.IsEmpty;

    private PanZoomPoint NormalizeCenter()
    {
        var contentCenter = _transform.ToContent(new PanZoomPoint(_viewport.Width / 2, _viewport.Height / 2));
        return new PanZoomPoint(
            (contentCenter.X - _contentBounds.X) / _contentBounds.Width,
            (contentCenter.Y - _contentBounds.Y) / _contentBounds.Height);
    }

    private static PanZoomRect CalculateFitBounds(PanZoomSize viewport, PanZoomSize content)
    {
        var scale = Math.Min(viewport.Width / content.Width, viewport.Height / content.Height);
        var width = content.Width * scale;
        var height = content.Height * scale;
        return new PanZoomRect((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height);
    }

    private PanZoomTransform Clamp(PanZoomTransform transform) => transform with
    {
        TranslateX = ClampAxis(transform.TranslateX, transform.Scale, _contentBounds.X, _contentBounds.Width, _viewport.Width),
        TranslateY = ClampAxis(transform.TranslateY, transform.Scale, _contentBounds.Y, _contentBounds.Height, _viewport.Height),
    };

    // コンテンツがビューポートより小さい軸は中央に置き、大きい軸は端が内側へ入り込まない範囲に収める
    private static double ClampAxis(double translate, double scale, double boundsStart, double boundsLength, double viewportLength)
    {
        var scaledStart = boundsStart * scale;
        var scaledLength = boundsLength * scale;
        if (scaledLength <= viewportLength)
        {
            return (viewportLength - scaledLength) / 2 - scaledStart;
        }

        var min = viewportLength - scaledStart - scaledLength;
        var max = -scaledStart;
        return Math.Clamp(translate, min, max);
    }

    private void Apply(PanZoomTransform transform)
    {
        if (transform == _transform)
        {
            return;
        }

        _transform = transform;
        Changed?.Invoke(this, transform);
    }
}
