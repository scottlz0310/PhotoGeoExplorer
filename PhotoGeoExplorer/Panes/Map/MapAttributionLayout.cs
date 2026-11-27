using System;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 帰属表示の寸法（DIP）。地図に描く側と、地図に重ねる UI が帰属表示を避ける側で、同じ値を使う。
/// </summary>
internal static class MapAttributionLayout
{
    internal const double TextSize = 11;
    internal const double OuterMargin = 4;
    internal const double HorizontalPadding = 4;
    internal const double VerticalPadding = 2;

    /// <summary>
    /// 縦に積むときの、1 項目あたりの高さ（文字、上下の余白、項目の間隔）
    /// </summary>
    internal const double StackedLineHeight = TextSize + (2 * VerticalPadding) + 2;

    // 大きさは Mapbox 公式の帰属表示コントロール（88×23）に合わせる。ロゴの加工は認められていない。
    internal const double LogoWidth = 88;
    internal const double LogoHeight = 23;
    internal const double LogoMargin = 6;

    /// <summary>
    /// 地図の下端から、帰属表示（文言とロゴ）が使う高さ
    /// </summary>
    internal static double GetReservedHeight(MapBackgroundDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var links = OuterMargin + (descriptor.AttributionRows.Count * StackedLineHeight);
        var logo = descriptor.ShowsMapboxLogo ? LogoHeight + (2 * LogoMargin) : 0;
        return Math.Max(links, logo);
    }
}
