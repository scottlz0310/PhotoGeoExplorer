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
    // NaN は比較が常に偽になるため、「正の値でない」を空の判定に使うと NaN も空として扱える
    internal bool IsEmpty => !(Width > 0 && Height > 0);
}

/// <summary>
/// UI フレームワークの型に依存しない 2 次元の矩形
/// </summary>
internal readonly record struct PanZoomRect(double X, double Y, double Width, double Height)
{
    internal bool IsEmpty => !(Width > 0 && Height > 0);
}
