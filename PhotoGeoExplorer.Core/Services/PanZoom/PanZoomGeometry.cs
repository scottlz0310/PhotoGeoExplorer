namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// UI フレームワークの型に依存しない 2 次元の点
/// </summary>
internal readonly record struct PanZoomPoint(double X, double Y);

/// <summary>
/// UI フレームワークの型に依存しない 2 次元の大きさ
/// </summary>
internal readonly record struct PanZoomSize(double Width, double Height)
{
    // NaN は比較が常に偽になるため「正の値でない」で空にできるが、Infinity は正の値なので有限性を別に見る。
    // 未制約のレイアウトが Infinity を渡してきても、変換が NaN にならないようにする
    internal bool IsEmpty => !(double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0);
}

/// <summary>
/// UI フレームワークの型に依存しない 2 次元の矩形
/// </summary>
internal readonly record struct PanZoomRect(double X, double Y, double Width, double Height)
{
    // 大きさの比が極端だと、縦横比を保った矩形の計算が Infinity にあふれる。位置を含めて有限でなければ空として扱う
    internal bool IsEmpty => !(double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0);
}
