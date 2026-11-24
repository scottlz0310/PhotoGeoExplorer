using System;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class WebMercatorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(139.767, 35.681)]
    [InlineData(-122.4194, 37.7749)]
    [InlineData(151.2093, -33.8688)]
    [InlineData(179.9, 84.9)]
    public void ToLongitudeAndToLatitudeInvertTheForwardProjection(double longitude, double latitude)
    {
        // 順方向の式は、変換の実装（atan・sinh）とは別の式（ln・tan）で書く
        var x = WebMercator.EarthRadius * longitude * Math.PI / 180;
        var y = WebMercator.EarthRadius * Math.Log(Math.Tan((Math.PI / 4) + (latitude * Math.PI / 360)));

        Assert.Equal(longitude, WebMercator.ToLongitude(x), 9);
        Assert.Equal(latitude, WebMercator.ToLatitude(y), 9);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 4)]
    [InlineData(19, 20)]
    public void ZoomLevelResolutionsStartAtTheWorldAndHalveWithEachLevel(int maxLevel, int expectedCount)
    {
        var resolutions = WebMercator.ZoomLevelResolutions(maxLevel);

        Assert.Equal(expectedCount, resolutions.Length);
        Assert.Equal(WebMercator.ZoomZeroResolution, resolutions[0]);
        for (var level = 1; level < resolutions.Length; level++)
        {
            Assert.Equal(resolutions[level - 1] / 2, resolutions[level], 9);
            // 連続ズームの式と逆の関係（密度 1 のとき、レベルがそのままズーム）
            Assert.Equal(level, WebMercator.ZoomFromResolution(resolutions[level], 1.0), 9);
        }
    }

    [Fact]
    public void ZoomLevelResolutionsRejectANegativeLevel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WebMercator.ZoomLevelResolutions(-1));
    }

    [Theory]
    // 100%: ズーム 0 の解像度なら 0、解像度が半分になるごとに 1 増える
    [InlineData(WebMercator.ZoomZeroResolution, 1.0, 0.0)]
    [InlineData(WebMercator.ZoomZeroResolution / 2, 1.0, 1.0)]
    [InlineData(WebMercator.ZoomZeroResolution / 32768, 1.0, 15.0)]
    // 密度が高いと、同じ論理解像度でも物理ピクセルあたりの地上距離が短い（150% は log2(1.5) だけ大きい）
    [InlineData(WebMercator.ZoomZeroResolution / 32768, 1.5, 15.584962500721156)]
    [InlineData(WebMercator.ZoomZeroResolution / 32768, 2.0, 16.0)]
    // 物理ピクセルで見た解像度が同じなら、密度が違っても同じズーム
    [InlineData(WebMercator.ZoomZeroResolution * 2 / 32768, 2.0, 15.0)]
    public void ZoomFromResolutionCountsTilesInPhysicalPixels(double resolution, double pixelDensity, double expected)
    {
        Assert.Equal(expected, WebMercator.ZoomFromResolution(resolution, pixelDensity), 9);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(139.767, 35.681)]
    [InlineData(-122.4194, 37.7749)]
    [InlineData(142.362436, 43.768436)]
    [InlineData(-180.0, -85.0)]
    [InlineData(180.0, 85.0)]
    public void FromLongitudeAndFromLatitudeRoundTripWithInverse(double longitude, double latitude)
    {
        var x = WebMercator.FromLongitude(longitude);
        var y = WebMercator.FromLatitude(latitude);

        Assert.Equal(longitude, WebMercator.ToLongitude(x), 9);
        Assert.Equal(latitude, WebMercator.ToLatitude(y), 9);
    }

    [Theory]
    [InlineData(90.0, WebMercator.MaxLatitude)]
    [InlineData(-90.0, -WebMercator.MaxLatitude)]
    public void FromLatitudeClampsPolarValuesToWebMercatorBounds(double inputLatitude, double expectedClampedLatitude)
    {
        var y = WebMercator.FromLatitude(inputLatitude);

        Assert.True(double.IsFinite(y));
        Assert.Equal(expectedClampedLatitude, WebMercator.ToLatitude(y), 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(8.0)]
    [InlineData(14.0)]
    [InlineData(16.5)]
    [InlineData(18.6)]
    [InlineData(22.0)]
    public void LogicalZoomAndResolutionRoundTrip(double logicalZoom)
    {
        var resolution = WebMercator.ResolutionFromLogicalZoom(logicalZoom);

        Assert.True(resolution > 0);
        Assert.Equal(logicalZoom, WebMercator.LogicalZoomFromResolution(resolution), 9);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(14.0, 14)]
    [InlineData(15.99, 15)]
    [InlineData(16.0, 16)]
    [InlineData(17.5, 17)]
    [InlineData(18.9, 18)]
    public void StyleZoomFromLogicalZoomFloorsToIntegerZoom(double logicalZoom, int expectedStyleZoom)
    {
        Assert.Equal(expectedStyleZoom, WebMercator.StyleZoomFromLogicalZoom(logicalZoom));
    }
}
