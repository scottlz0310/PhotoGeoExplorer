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
