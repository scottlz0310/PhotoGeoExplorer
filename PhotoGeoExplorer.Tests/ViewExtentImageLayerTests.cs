// 生成した SKBitmap／SKImage の所有権は SharedResource（のちに Release）へ渡る
#pragma warning disable CA2000
using System;
using Mapsui;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class ViewExtentImageLayerTests
{
    private static readonly Rgba32 Red = new(255, 0, 0, 255);

    [Theory]
    // 描いたときと同じ視点: 画像（100x80 論理ピクセル）は画面 200x160 の中央を覆う
    [InlineData(1.0f, 0, 0, 50, 40, 150, 120)]
    [InlineData(1.5f, 0, 0, 50, 40, 150, 120)]
    [InlineData(2.0f, 0, 0, 50, 40, 150, 120)]
    // 視点が東へ 20m（= 20 論理ピクセル）動くと、画像は西（左）へ 20 論理ピクセルずれる
    [InlineData(1.5f, 20, 0, 30, 40, 130, 120)]
    // 視点が北へ 10m 動くと、画像は南（下）へ 10 論理ピクセルずれる（画面の y は下向き）
    [InlineData(1.5f, 0, 10, 50, 50, 150, 130)]
    internal void ImageIsPlacedAtItsGeographicExtentForTheViewport(float density, double viewX, double viewY, double left, double top, double right, double bottom)
    {
        using var layer = new ViewExtentImageLayer("test");
        layer.Show(CreateFrame(width: 100, height: 80, density: density, Red));

        using var png = RenderPng(layer, new Viewport(viewX, viewY, 1, 0, 200, 160), density);
        using var image = Image.Load<Rgba32>(png);

        var inside = (int)Math.Ceiling(2 * density);
        var margin = (int)Math.Ceiling(3 * density);
        var l = (int)Math.Round(left * density);
        var t = (int)Math.Round(top * density);
        var r = (int)Math.Round(right * density);
        var b = (int)Math.Round(bottom * density);
        var midX = (l + r) / 2;
        var midY = (t + b) / 2;

        // 4 辺それぞれの内側は画像の色、外側は背景
        Assert.Equal(Red, image[l + inside, midY]);
        Assert.Equal(Red, image[r - inside - 1, midY]);
        Assert.Equal(Red, image[midX, t + inside]);
        Assert.Equal(Red, image[midX, b - inside - 1]);
        Assert.NotEqual(Red, image[l - margin, midY]);
        Assert.NotEqual(Red, image[r + margin, midY]);
        Assert.NotEqual(Red, image[midX, t - margin]);
        Assert.NotEqual(Red, image[midX, b + margin]);
    }

    [Fact]
    public void ZoomedOutViewShrinksTheStaleImageAroundTheCenter()
    {
        using var layer = new ViewExtentImageLayer("test");
        layer.Show(CreateFrame(width: 100, height: 80, density: 1f, Red));

        // 解像度が 2 倍（縮小表示）なので、画像は 50x40 論理ピクセルになる
        using var png = RenderPng(layer, new Viewport(0, 0, 2, 0, 200, 160), 1f);
        using var image = Image.Load<Rgba32>(png);

        Assert.Equal(Red, image[100, 80]);
        Assert.Equal(Red, image[78, 62]);
        Assert.NotEqual(Red, image[70, 80]);
        Assert.NotEqual(Red, image[100, 55]);
    }

    [Fact]
    public void ShowingANewImageReleasesThePreviousOneAndKeepsTheNewOne()
    {
        using var layer = new ViewExtentImageLayer("test");
        var first = CreateFrame(100, 80, 1f, Red);
        var second = CreateFrame(100, 80, 1f, new Rgba32(0, 0, 255, 255));

        layer.Show(first);
        layer.Show(second);

        Assert.True(first.Image.IsDisposed);
        Assert.False(second.Image.IsDisposed);
    }

    [Fact]
    public void ImageBorrowedByADrawingThreadSurvivesTheSwapUntilReturned()
    {
        using var layer = new ViewExtentImageLayer("test");
        var first = CreateFrame(100, 80, 1f, Red);
        layer.Show(first);
        Assert.True(first.Image.TryAcquire(out var lease));

        layer.Show(CreateFrame(100, 80, 1f, Red));

        Assert.False(first.Image.IsDisposed);
        lease.Dispose();
        Assert.True(first.Image.IsDisposed);
    }

    [Fact]
    public void DisposingTheLayerReleasesTheCurrentImage()
    {
        var layer = new ViewExtentImageLayer("test");
        var frame = CreateFrame(100, 80, 1f, Red);
        layer.Show(frame);

        layer.Dispose();
        layer.Dispose();

        Assert.True(frame.Image.IsDisposed);
    }

    [Fact]
    public void ShowingAfterDisposeReleasesTheImageInsteadOfLeakingIt()
    {
        var layer = new ViewExtentImageLayer("test");
        layer.Dispose();
        var frame = CreateFrame(100, 80, 1f, Red);

        layer.Show(frame);

        Assert.True(frame.Image.IsDisposed);
    }

    [Fact]
    public void DrawingAnAlreadyReleasedImageIsSkippedWithoutThrowing()
    {
        // 交換の直後に、別スレッドの描画が古いスタイルを描こうとした場合
        var frame = CreateFrame(100, 80, 1f, Red);
        var style = new ViewExtentImageStyle(frame.Image, frame.Request.Extent);
        frame.Image.Release();
        using var canvasBitmap = new SKBitmap(200, 160);
        canvasBitmap.Erase(SKColors.Black);
        using var canvas = new SKCanvas(canvasBitmap);

        var handled = new ViewExtentImageStyleRenderer().Draw(
            canvas,
            new Viewport(0, 0, 1, 0, 200, 160),
            null!,
            null!,
            style,
            null!,
            0);

        Assert.True(handled);
        Assert.Equal(SKColors.Black, canvasBitmap.GetPixel(100, 80));
    }

    [Fact]
    public void RendererIgnoresStylesOfOtherTypes()
    {
        using var canvasBitmap = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(canvasBitmap);

        var handled = new ViewExtentImageStyleRenderer().Draw(
            canvas,
            new Viewport(0, 0, 1, 0, 10, 10),
            null!,
            null!,
            new Mapsui.Styles.VectorStyle(),
            null!,
            0);

        Assert.False(handled);
    }

    private static System.IO.Stream RenderPng(ViewExtentImageLayer layer, Viewport viewport, float density)
    {
        using var map = new Map { BackColor = Mapsui.Styles.Color.Black };
        return new MapsuiMapImageRenderer().RenderPng(map, [layer.Layer], viewport, density);
    }

    // 画面 200x160 論理ピクセルの視点（中心 0,0、解像度 1）ではなく、画像の大きさ分（width x height 論理ピクセル）の
    // 範囲を中心 (0,0)・解像度 1 で描いた画像として作る。画像の画素寸法は論理寸法 × density。
    private static ViewExtentFrame<SKImage> CreateFrame(int width, int height, float density, Rgba32 color)
    {
        Assert.True(ViewExtentRequest.TryCreate(0, 0, 1, width, height, density, "test", out var request));
        using var bitmap = new SKBitmap(request.ImageWidth, request.ImageHeight);
        bitmap.Erase(new SKColor(color.R, color.G, color.B, color.A));
        return new ViewExtentFrame<SKImage>(request, 1, new SharedResource<SKImage>(SKImage.FromBitmap(bitmap)));
    }
}
