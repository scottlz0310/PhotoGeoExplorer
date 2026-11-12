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

    [Theory]
    [InlineData(PhotoGeoExplorer.Models.MapTileSourceType.MapboxSatellite, true)]
    [InlineData(PhotoGeoExplorer.Models.MapTileSourceType.OpenStreetMap, false)]
    [InlineData(PhotoGeoExplorer.Models.MapTileSourceType.EsriWorldImagery, false)]
    internal async System.Threading.Tasks.Task RenderPngDrawsMapboxLogoOnlyForMapbox(PhotoGeoExplorer.Models.MapTileSourceType sourceType, bool expectLogo)
    {
        Assert.True(System.IO.File.Exists(PhotoGeoExplorer.Panes.Map.MapAttribution.MapboxLogoPath));
        var renderer = new MapsuiMapImageRenderer();
        // 右下の帰属文言（Esri は長い）が左下のロゴ領域へ届かない幅にする
        var viewport = new Viewport(0, 0, 1, 0, 800, 200);

        using var map = new Map { BackColor = Mapsui.Styles.Color.Black };
        using var plainDarkStream = renderer.RenderPng(map, viewport, pixelDensity: 1f);
        using var plainDarkImage = await SixLabors.ImageSharp.Image
            .LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgba32>(plainDarkStream)
            .ConfigureAwait(true);
        var presenter = new PhotoGeoExplorer.Panes.Map.MapAttributionPresenter();
        presenter.Attach(map, sourceType);
        Assert.True(await presenter.LoadImagesAsync(map).ConfigureAwait(true));
        using var stream = renderer.RenderPng(map, viewport, pixelDensity: 1f);
        using var image = await SixLabors.ImageSharp.Image
            .LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgba32>(stream)
            .ConfigureAwait(true);

        // ロゴは左下（88×23）に描かれる
        var logoPixels = CountDifferentPixels(plainDarkImage, image, 0, 165, 100, 200);
        Assert.Equal(expectLogo, logoPixels > 100);
    }

    [Theory]
    [InlineData(PhotoGeoExplorer.Models.MapTileSourceType.MapboxSatellite)]
    [InlineData(PhotoGeoExplorer.Models.MapTileSourceType.OpenStreetMap)]
    [InlineData(PhotoGeoExplorer.Models.MapTileSourceType.EsriWorldImagery)]
    internal async System.Threading.Tasks.Task RenderPngDrawsAttributionLinksAsOneContiguousStrip(PhotoGeoExplorer.Models.MapTileSourceType sourceType)
    {
        var renderer = new MapsuiMapImageRenderer();
        const int width = 800;
        const int height = 200;
        using var map = new Map { BackColor = Mapsui.Styles.Color.Black };
        new PhotoGeoExplorer.Panes.Map.MapAttributionPresenter().Attach(map, sourceType);

        using var stream = renderer.RenderPng(map, new Viewport(0, 0, 1, 0, width, height), pixelDensity: 1f);
        using var image = await SixLabors.ImageSharp.Image
            .LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgba32>(stream)
            .ConfigureAwait(true);
        // 帰属項目は個別のウィジェットだが、右下に隙間も重なりもなく並ぶこと。
        // 背景（黒）に対して、項目の下地（半透明の白）が載った列を右半分で調べる。
        var litColumns = new System.Collections.Generic.List<int>();
        for (var x = width / 4; x < width; x++)
        {
            var lit = false;
            for (var y = height - 20; y < height && !lit; y++)
            {
                lit = image[x, y].R > 100;
            }

            if (lit)
            {
                litColumns.Add(x);
            }
        }

        Assert.NotEmpty(litColumns);
        Assert.Equal(litColumns[^1] - litColumns[0] + 1, litColumns.Count);
        Assert.InRange(litColumns[^1], width - 6, width - 4);
        Assert.True(litColumns.Count > 100);
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
