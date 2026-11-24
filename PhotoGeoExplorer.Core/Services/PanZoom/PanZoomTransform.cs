namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// コンテンツ座標から表示座標への相似変換（<c>表示 = コンテンツ × Scale + Translate</c>）。
/// 表示座標の原点はビューポートの左上。
/// </summary>
internal readonly record struct PanZoomTransform(double Scale, double TranslateX, double TranslateY)
{
    /// <summary>
    /// 倍率 1（ウィンドウに合わせた状態）で、移動なし
    /// </summary>
    internal static PanZoomTransform Identity => new(1.0, 0.0, 0.0);

    internal PanZoomPoint ToView(PanZoomPoint content)
        => new(content.X * Scale + TranslateX, content.Y * Scale + TranslateY);

    internal PanZoomPoint ToContent(PanZoomPoint view)
        => new((view.X - TranslateX) / Scale, (view.Y - TranslateY) / Scale);
}
