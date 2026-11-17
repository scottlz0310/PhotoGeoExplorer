using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class ViewExtentRequestTests
{
    [Theory]
    [InlineData(0, 0, 10, 800, 600, 1.0)]
    [InlineData(1e7, -1e6, 0.149, 1200, 800, 1.5)]
    [InlineData(0, 0, 4.777, 300.4, 200.6, 2.0)]
    internal void TryCreateSucceedsForRenderableViewports(double cx, double cy, double resolution, double width, double height, double density)
    {
        Assert.True(ViewExtentRequest.TryCreate(cx, cy, resolution, width, height, density, "src", out var request));

        Assert.Equal(cx, request.CenterX);
        Assert.Equal(cy, request.CenterY);
        Assert.Equal("src", request.SourceKey);
    }

    [Theory]
    [InlineData(double.NaN, 0, 10, 800, 600, 1.0)]
    [InlineData(0, double.PositiveInfinity, 10, 800, 600, 1.0)]
    [InlineData(0, 0, 0, 800, 600, 1.0)]
    [InlineData(0, 0, -1, 800, 600, 1.0)]
    [InlineData(0, 0, double.NaN, 800, 600, 1.0)]
    [InlineData(0, 0, 10, 0, 600, 1.0)]
    [InlineData(0, 0, 10, 800, 0, 1.0)]
    [InlineData(0, 0, 10, double.NaN, 600, 1.0)]
    [InlineData(0, 0, 10, 800, 600, 0)]
    [InlineData(0, 0, 10, 0.2, 600, 1.0)]
    internal void TryCreateRejectsViewportsThatCannotBeRenderedWithoutThrowing(double cx, double cy, double resolution, double width, double height, double density)
    {
        Assert.False(ViewExtentRequest.TryCreate(cx, cy, resolution, width, height, density, "src", out var request));
        Assert.Null(request);
    }

    [Fact]
    public void ExtentIsCenteredAndScaledByResolution()
    {
        Assert.True(ViewExtentRequest.TryCreate(1000, 2000, 2, 100, 50, 1, "src", out var request));

        Assert.Equal(new GeoExtent(900, 1950, 1100, 2050), request.Extent);
    }

    [Theory]
    [InlineData(800, 600, 1.0, 800, 600)]
    [InlineData(800, 600, 1.5, 1200, 900)]
    [InlineData(801, 601, 1.25, 1001, 751)]
    internal void ImageSizeIsLogicalSizeScaledToPhysicalPixels(double width, double height, double density, int expectedWidth, int expectedHeight)
    {
        Assert.True(ViewExtentRequest.TryCreate(0, 0, 1, width, height, density, "src", out var request));

        Assert.Equal(expectedWidth, request.ImageWidth);
        Assert.Equal(expectedHeight, request.ImageHeight);
    }

    [Theory]
    [InlineData("a", "a", true)]
    [InlineData("a", "b", false)]
    [InlineData("a", "A", false)]
    internal void CompatibilityDependsOnlyOnSourceKey(string first, string second, bool expected)
    {
        Assert.True(ViewExtentRequest.TryCreate(0, 0, 1, 100, 100, 1, first, out var a));
        Assert.True(ViewExtentRequest.TryCreate(500, 500, 9, 200, 300, 2, second, out var b));

        Assert.Equal(expected, a.IsCompatibleWith(b));
    }

    [Theory]
    [InlineData(0, 0, 10, 10, 5, 5, 15, 15, true)]
    [InlineData(0, 0, 10, 10, 10, 0, 20, 10, false)]
    [InlineData(0, 0, 10, 10, 0, 10, 10, 20, false)]
    [InlineData(0, 0, 10, 10, 11, 11, 20, 20, false)]
    [InlineData(0, 0, 10, 10, 2, 2, 8, 8, true)]
    internal void IntersectsIgnoresTouchingEdges(double aMinX, double aMinY, double aMaxX, double aMaxY, double bMinX, double bMinY, double bMaxX, double bMaxY, bool expected)
    {
        var a = new GeoExtent(aMinX, aMinY, aMaxX, aMaxY);
        var b = new GeoExtent(bMinX, bMinY, bMaxX, bMaxY);

        Assert.Equal(expected, a.Intersects(b));
        Assert.Equal(expected, b.Intersects(a));
    }
}
