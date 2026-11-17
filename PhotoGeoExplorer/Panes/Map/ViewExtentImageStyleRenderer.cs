using System;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using PhotoGeoExplorer.Services.MapRendering;
using SkiaSharp;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 表示範囲画像（<see cref="SKImage"/>）とその地理範囲を持つ Mapsui のスタイル。
/// 画像の寿命は <see cref="ViewExtentImageLayer"/> が管理し、描く側は <see cref="SharedResource{T}.TryAcquire"/> で借りる。
/// </summary>
internal sealed class ViewExtentImageStyle : BaseStyle
{
    internal ViewExtentImageStyle(SharedResource<SKImage> image, GeoExtent extent)
    {
        ArgumentNullException.ThrowIfNull(image);

        Image = image;
        Extent = extent;
    }

    internal SharedResource<SKImage> Image { get; }

    internal GeoExtent Extent { get; }
}

/// <summary>
/// <see cref="ViewExtentImageStyle"/> を、現在の視点での画面矩形へ描く。
/// Mapsui の描画は UI スレッド以外で走りうる（PNG 保存など）。ピクセル密度は XAML ではなくキャンバスの変換行列から得る。
/// </summary>
internal sealed class ViewExtentImageStyleRenderer : ISkiaStyleRenderer
{
    private static readonly object RegistrationGate = new();
    private static bool s_registered;

    /// <summary>Mapsui へスタイルレンダラーを登録する（プロセスで 1 回）。</summary>
    internal static void EnsureRegistered()
    {
        lock (RegistrationGate)
        {
            if (s_registered)
            {
                return;
            }

            MapRenderer.RegisterStyleRenderer(typeof(ViewExtentImageStyle), new ViewExtentImageStyleRenderer());
            s_registered = true;
        }
    }

    public bool Draw(
        SKCanvas canvas,
        Viewport viewport,
        ILayer layer,
        IFeature feature,
        IStyle style,
        RenderService renderService,
        long iteration)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(style);

        if (style is not ViewExtentImageStyle imageStyle)
        {
            return false;
        }

        // 画像が交換・解放された直後に描画が走った場合は、描かずに済ませる（解放済みの画像を描かない）
        if (!imageStyle.Image.TryAcquire(out var lease))
        {
            return true;
        }

        using (lease)
        {
            var image = lease.Value;
            var destination = ViewExtentPlacement.ToScreenRect(
                imageStyle.Extent,
                viewport.CenterX,
                viewport.CenterY,
                viewport.Resolution,
                viewport.Width,
                viewport.Height);
            var scale = ViewExtentPlacement.ComputeScale(destination, canvas.TotalMatrix.ScaleX, image.Width);
            var sampling = ViewExtentPlacement.ShouldUseMipmaps(scale)
                ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

            canvas.DrawImage(
                image,
                new SKRect((float)destination.Left, (float)destination.Top, (float)destination.Right, (float)destination.Bottom),
                sampling);
        }

        return true;
    }
}
