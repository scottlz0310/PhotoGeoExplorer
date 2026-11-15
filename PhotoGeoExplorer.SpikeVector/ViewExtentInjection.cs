using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace PhotoMapStudio.Spike;

/// <summary>
/// 表示範囲画像（SKImage）を、地理範囲（EPSG:3857）へ描く Mapsui 用のスタイル。注入方式 A（専用レイヤー＋カスタムスタイルレンダラー）。
/// </summary>
public sealed class ViewExtentImageStyle : BaseStyle
{
    public ViewExtentImageStyle(SKImage image, MRect extent)
    {
        Image = image;
        Extent = extent;
    }

    public SKImage Image { get; }

    public MRect Extent { get; }
}

public sealed class ViewExtentImageStyleRenderer : ISkiaStyleRenderer
{
    public static void Register()
    {
        MapRenderer.RegisterStyleRenderer(typeof(ViewExtentImageStyle), new ViewExtentImageStyleRenderer());
    }

    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (style is not ViewExtentImageStyle s)
        {
            return false;
        }

        // Mapsui のキャンバスは pixelDensity で拡大済みで、viewport は DIP 単位。向きは 0 前提（P1 は回転を扱わない）
        var left = (float)(((s.Extent.MinX - viewport.CenterX) / viewport.Resolution) + (viewport.Width / 2));
        var right = (float)(((s.Extent.MaxX - viewport.CenterX) / viewport.Resolution) + (viewport.Width / 2));
        var top = (float)(((viewport.CenterY - s.Extent.MaxY) / viewport.Resolution) + (viewport.Height / 2));
        var bottom = (float)(((viewport.CenterY - s.Extent.MinY) / viewport.Resolution) + (viewport.Height / 2));
        // 縮小して見るとき（操作中の古い画像）は、ミップマップを使い、エイリアシングによる位置のずれを避ける
        var scale = (right - left) / s.Image.Width;
        var nearest = Environment.GetEnvironmentVariable("PGE_P1_NEAREST") == "1" && Math.Abs(scale - 1f) < 0.001f;
        var sampling = nearest ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None) : scale < 0.9f ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear) : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        canvas.DrawImage(s.Image, new SKRect(left, top, right, bottom), sampling);
        return true;
    }
}

public static class ViewExtentInjection
{
    /// <summary>世界座標（EPSG:3857 の m）の、赤道での 256px タイルの解像度（m/px）。</summary>
    public const double ResolutionAtZoom0 = 156543.03392804097;

    public static double ZoomFromResolution(double resolution, double pixelDensity)
        => Math.Log2(ResolutionAtZoom0 * pixelDensity / resolution);

    public static double ResolutionFromZoom(double zoom, double pixelDensity)
        => ResolutionAtZoom0 * pixelDensity / Math.Pow(2, zoom);

    /// <summary>方式 A: 地理範囲のポリゴン 1 つに、画像を描くスタイルを付ける。</summary>
    public static MemoryLayer CreateLayerA(SKImage image, MRect extent)
    {
        var ring = new LinearRing(new[]
        {
            new Coordinate(extent.MinX, extent.MinY),
            new Coordinate(extent.MaxX, extent.MinY),
            new Coordinate(extent.MaxX, extent.MaxY),
            new Coordinate(extent.MinX, extent.MaxY),
            new Coordinate(extent.MinX, extent.MinY),
        });
        var feature = new GeometryFeature(new Polygon(ring));
        feature.Styles.Clear();
        feature.Styles.Add(new ViewExtentImageStyle(image, extent));
        return new MemoryLayer { Name = "ViewExtentA", Features = new IFeature[] { feature }, Style = null };
    }

    /// <summary>方式 B: Mapsui 標準のラスタ（PNG にエンコードして RasterFeature）。</summary>
    public static MemoryLayer CreateLayerB(SKImage image, MRect extent)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var raster = new MRaster(data.ToArray(), extent);
        var feature = new RasterFeature(raster);
        return new MemoryLayer { Name = "ViewExtentB", Features = new IFeature[] { feature }, Style = new RasterStyle() };
    }
}
