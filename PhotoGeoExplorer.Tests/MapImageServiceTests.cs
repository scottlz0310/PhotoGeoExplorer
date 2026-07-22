using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapImageServiceTests
{
    [Fact]
    public void CreateDefaultFileNameUsesInvariantTimestamp()
    {
        var service = CreateService(new MemoryStream());

        var fileName = service.CreateDefaultFileName(new DateTimeOffset(2026, 7, 22, 13, 45, 6, TimeSpan.FromHours(9)));

        Assert.Equal("PhotoGeoExplorer_Map_20260722_134506.png", fileName);
    }

    [Fact]
    public void RenderPngPassesCurrentMapViewportAndPixelDensityToRenderer()
    {
        using var map = new Map();
        var viewport = new Viewport(100, 200, 2, 30, 640, 480);
        using var expectedStream = new MemoryStream([1, 2, 3]);
        var renderer = new CapturingRenderer(expectedStream);
        var service = new MapImageService(renderer, _ => new MemoryStream());

        var actualStream = service.RenderPng(map, viewport, 1.5f);

        Assert.Same(expectedStream, actualStream);
        Assert.Same(map, renderer.Map);
        Assert.Equal(viewport, renderer.Viewport);
        Assert.Equal(1.5f, renderer.PixelDensity);
    }

    [Fact]
    public void RenderPngAddsContextToRenderingFailure()
    {
        using var map = new Map();
        var expectedException = new InvalidOperationException("renderer failed");
        var service = new MapImageService(new ThrowingRenderer(expectedException), _ => new MemoryStream());

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.RenderPng(map, new Viewport(0, 0, 1, 0, 100, 50), 1));

        Assert.Equal("Failed to render the current map viewport as PNG.", exception.Message);
        Assert.Same(expectedException, exception.InnerException);
    }

    [Fact]
    public async Task SavePngAsyncWritesEntireStream()
    {
        using var source = new MemoryStream([1, 2, 3, 4]);
        var destination = new MemoryStream();
        var service = CreateService(destination);

        await service.SavePngAsync(source, "map.png", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal([1, 2, 3, 4], destination.ToArray());
    }

    [Fact]
    public async Task SavePngAsyncAddsPathContextToWriteFailure()
    {
        using var source = new MemoryStream([1, 2, 3, 4]);
        var expectedException = new UnauthorizedAccessException("access denied");
        var service = new MapImageService(
            new CapturingRenderer(new MemoryStream()),
            _ => throw expectedException);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => service.SavePngAsync(source, "unwritable-map.png", CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains("unwritable-map.png", exception.Message, StringComparison.Ordinal);
        Assert.Same(expectedException, exception.InnerException);
    }

    private static MapImageService CreateService(MemoryStream destination)
    {
        return new MapImageService(
            new CapturingRenderer(new MemoryStream()),
            _ => destination);
    }

    private sealed class CapturingRenderer(Stream stream) : IMapImageRenderer
    {
        public Map? Map { get; private set; }

        public Viewport Viewport { get; private set; }

        public float PixelDensity { get; private set; }

        public Stream RenderPng(Map map, Viewport viewport, float pixelDensity)
        {
            Map = map;
            Viewport = viewport;
            PixelDensity = pixelDensity;
            return stream;
        }
    }

    private sealed class ThrowingRenderer(Exception exception) : IMapImageRenderer
    {
        public Stream RenderPng(Map map, Viewport viewport, float pixelDensity)
        {
            throw exception;
        }
    }

}
