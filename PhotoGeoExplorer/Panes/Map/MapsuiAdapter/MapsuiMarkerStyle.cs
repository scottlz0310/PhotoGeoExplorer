using System;
using Mapsui.Styles;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// 写真マーカーの Mapsui スタイル。ピン画像があれば画像、なければ既定の印を使う。
/// </summary>
internal static class MapsuiMarkerStyle
{
    internal static IStyle Create(string? pinImagePath)
    {
        if (string.IsNullOrWhiteSpace(pinImagePath))
        {
            return CreateFallback();
        }

        return new ImageStyle
        {
            Image = new Mapsui.Styles.Image { Source = new Uri(pinImagePath).AbsoluteUri },
            SymbolScale = 1,
            RelativeOffset = new RelativeOffset(0, 0.5)
        };
    }

    private static SymbolStyle CreateFallback()
    {
        return new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.8,
            Fill = new Brush(Color.FromArgb(255, 32, 128, 255)),
            Outline = new Pen(Color.White, 2)
        };
    }
}
