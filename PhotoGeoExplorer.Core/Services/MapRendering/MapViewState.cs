namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 地図の視点（どこを・どの縮尺で・どの大きさで見ているか）だけを表す不変の値。
/// 描画元やスタイルの識別（<see cref="ViewExtentRequest.SourceKey"/>）は持たない。
/// </summary>
/// <param name="HasSize">表示面の大きさが確定しているか。確定前は他の値を信用しない</param>
/// <param name="CenterX">中心の X（Web メルカトル、メートル）</param>
/// <param name="CenterY">中心の Y（Web メルカトル、メートル）</param>
/// <param name="Resolution">表示解像度（メートル／論理ピクセル）</param>
/// <param name="WidthDip">表示面の幅（論理ピクセル）</param>
/// <param name="HeightDip">表示面の高さ（論理ピクセル）</param>
internal readonly record struct MapViewState(
    bool HasSize,
    double CenterX,
    double CenterY,
    double Resolution,
    double WidthDip,
    double HeightDip);
