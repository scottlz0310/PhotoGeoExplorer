using System;
using System.Buffers.Binary;
using Mapsui;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapsuiMapImageRendererTests
{
    [Fact]
    public void RenderPngCreatesPngAtPhysicalPixelDimensions()
    {
        using var map = new Map();
        var renderer = new MapsuiMapImageRenderer();

        using var stream = renderer.RenderPng(
            map,
            new Viewport(0, 0, 1, 0, 120, 80),
            pixelDensity: 1.5f);

        Span<byte> header = stackalloc byte[24];
        Assert.Equal(header.Length, stream.Read(header));
        Assert.True(header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.Equal(180, BinaryPrimitives.ReadInt32BigEndian(header[16..20]));
        Assert.Equal(120, BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    [Fact]
    public void RenderPngIncludesAttributionWidget()
    {
        var renderer = new MapsuiMapImageRenderer();
        var viewport = new Viewport(0, 0, 1, 0, 400, 200);

        using var plainMap = new Map();
        using var plainStream = renderer.RenderPng(plainMap, viewport, pixelDensity: 1f);
        using var plainImage = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(plainStream);

        using var attributedMap = new Map();
        new PhotoGeoExplorer.Panes.Map.MapAttributionPresenter().Attach(
            attributedMap,
            PhotoGeoExplorer.Models.MapTileSourceType.MapboxSatellite);
        using var attributedStream = renderer.RenderPng(attributedMap, viewport, pixelDensity: 1f);
        using var attributedImage = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(attributedStream);

        // 帰属表示は右下に描かれる。右下の領域だけが変わり、左上は変わらないこと
        Assert.True(CountDifferentPixels(plainImage, attributedImage, 200, 150, 400, 200) > 50);
        Assert.Equal(0, CountDifferentPixels(plainImage, attributedImage, 0, 0, 200, 100));
    }

    private static int CountDifferentPixels(
        SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> left,
        SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> right,
        int fromX,
        int fromY,
        int toX,
        int toY)
    {
        var count = 0;
        for (var y = fromY; y < toY; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                if (left[x, y] != right[x, y])
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RenderPngRejectsInvalidPixelDensity(float pixelDensity)
    {
        using var map = new Map();
        var renderer = new MapsuiMapImageRenderer();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => renderer.RenderPng(map, new Viewport(0, 0, 1, 0, 120, 80), pixelDensity));
    }
}
