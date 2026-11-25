using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        return new MapImageService(_ => new MemoryStream());
    }

    private static string CreateTemporaryDirectory()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
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
