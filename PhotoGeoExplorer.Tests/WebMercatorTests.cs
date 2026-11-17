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
}
