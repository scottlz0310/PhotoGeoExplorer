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
        var service = CreateService();

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
        var directoryPath = CreateTemporaryDirectory();
        var filePath = Path.Combine(directoryPath, "map.png");
        await File.WriteAllBytesAsync(filePath, [9, 8, 7]).ConfigureAwait(true);
        using var source = new MemoryStream([1, 2, 3, 4]);
        var service = new MapImageService();

        try
        {
            await service.SavePngAsync(source, filePath, CancellationToken.None).ConfigureAwait(true);

            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(filePath).ConfigureAwait(true));
            Assert.Single(Directory.GetFiles(directoryPath));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SavePngAsyncPreservesExistingFileWhenAlreadyCanceled()
    {
        var directoryPath = CreateTemporaryDirectory();
        var filePath = Path.Combine(directoryPath, "map.png");
        await File.WriteAllBytesAsync(filePath, [9, 8, 7]).ConfigureAwait(true);
        using var source = new MemoryStream([1, 2, 3, 4]);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync().ConfigureAwait(true);
        var service = new MapImageService();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.SavePngAsync(source, filePath, cancellationTokenSource.Token)).ConfigureAwait(true);

            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(filePath).ConfigureAwait(true));
            Assert.Single(Directory.GetFiles(directoryPath));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SavePngAsyncPreservesExistingFileWhenCopyFails()
    {
        var directoryPath = CreateTemporaryDirectory();
        var filePath = Path.Combine(directoryPath, "map.png");
        await File.WriteAllBytesAsync(filePath, [9, 8, 7]).ConfigureAwait(true);
        using var source = new FailingReadStream([1, 2, 3, 4]);
        var service = new MapImageService();

        try
        {
            var exception = await Assert.ThrowsAsync<IOException>(
                () => service.SavePngAsync(source, filePath, CancellationToken.None)).ConfigureAwait(true);

            Assert.Contains(filePath, exception.Message, StringComparison.Ordinal);
            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(filePath).ConfigureAwait(true));
            Assert.Single(Directory.GetFiles(directoryPath));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static MapImageService CreateService()
    {
        return new MapImageService(new CapturingRenderer(new MemoryStream()), _ => new MemoryStream());
    }

    private static string CreateTemporaryDirectory()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
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

    private sealed class FailingReadStream : MemoryStream
    {
        private readonly byte[] _bytes;

        public FailingReadStream(byte[] bytes)
            : base(bytes)
        {
            _bytes = bytes;
        }

        public override async Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(_bytes.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
            throw new IOException("read failed");
        }
    }
}
