using System;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class ViewExtentPlacementTests
{
    [Fact]
    public void ImageDrawnForTheSameViewportFillsTheViewportExactly()
    {
        Assert.True(ViewExtentRequest.TryCreate(1000, 2000, 2, 800, 600, 1.5, "src", out var request));

        var rect = ViewExtentPlacement.ToScreenRect(request.Extent, 1000, 2000, 2, 800, 600);

        Assert.Equal(0, rect.Left, 6);
        Assert.Equal(0, rect.Top, 6);
        Assert.Equal(800, rect.Right, 6);
        Assert.Equal(600, rect.Bottom, 6);
    }

    [Theory]
    // 右へ 100px パン（視点の中心が +100px 分東へ）→ 画像は左へ 100px ずれる
    [InlineData(1200, 2000, 2, -100, 0, 700, 600)]
    // 北へパン（中心 y が増える）→ 画像は下へずれる（画面 y は下向き）
    [InlineData(1000, 2200, 2, 0, 100, 800, 700)]
    // 解像度が半分（2 倍に拡大）→ 中心基準で 2 倍に広がる
    [InlineData(1000, 2000, 1, -400, -300, 1200, 900)]
    // 解像度が倍（縮小表示）→ 中心基準で半分に縮む
    [InlineData(1000, 2000, 4, 200, 150, 600, 450)]
    internal void StaleImageFollowsTheViewport(double viewX, double viewY, double viewResolution, double left, double top, double right, double bottom)
    {
        Assert.True(ViewExtentRequest.TryCreate(1000, 2000, 2, 800, 600, 1, "src", out var request));

        var rect = ViewExtentPlacement.ToScreenRect(request.Extent, viewX, viewY, viewResolution, 800, 600);

        Assert.Equal(left, rect.Left, 6);
        Assert.Equal(top, rect.Top, 6);
        Assert.Equal(right, rect.Right, 6);
        Assert.Equal(bottom, rect.Bottom, 6);
    }

    [Theory]
    // 論理ピクセルで比べると 150% で 0.67 になり、等倍なのに縮小用の補間へ入る。デバイスピクセルでは 1
    [InlineData(800, 1.5, 1200, 1.0)]
    [InlineData(800, 2.0, 1600, 1.0)]
    [InlineData(800, 1.0, 800, 1.0)]
    [InlineData(400, 1.5, 1200, 0.5)]
    [InlineData(1600, 1.5, 1200, 2.0)]
    internal void ScaleIsMeasuredInDevicePixels(double destinationWidthDip, double deviceScale, int imageWidthPixels, double expected)
    {
        var scale = ViewExtentPlacement.ComputeScale(new ScreenRect(0, 0, destinationWidthDip, 10), deviceScale, imageWidthPixels);

        Assert.Equal(expected, scale, 6);
    }

    [Theory]
    [InlineData(0.1, true)]
    [InlineData(0.89, true)]
    [InlineData(0.9, false)]
    [InlineData(1.0, false)]
    [InlineData(3.0, false)]
    internal void MipmapsAreUsedOnlyWhenShrinking(double scale, bool expected)
    {
        Assert.Equal(expected, ViewExtentPlacement.ShouldUseMipmaps(scale));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    internal void ComputeScaleRejectsEmptyImages(int width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ViewExtentPlacement.ComputeScale(new ScreenRect(0, 0, 10, 10), 1, width));
    }
}
