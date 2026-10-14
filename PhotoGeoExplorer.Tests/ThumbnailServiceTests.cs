using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoGeoExplorer.Tests;

public sealed class ThumbnailServiceTests
{
    [Fact]
    public void GetThumbnailCacheKeyReturnsSameKeyForSameInput()
    {
        var filePath = "/test/image.jpg";
        var lastWrite = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var key1 = ThumbnailService.GetThumbnailCacheKey(filePath, lastWrite);
        var key2 = ThumbnailService.GetThumbnailCacheKey(filePath, lastWrite);

        Assert.Equal(key1, key2);
        Assert.NotEmpty(key1);
    }

    [Fact]
    public void GetThumbnailCacheKeyReturnsDifferentKeyForDifferentPath()
    {
        var lastWrite = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var key1 = ThumbnailService.GetThumbnailCacheKey("/test/image1.jpg", lastWrite);
        var key2 = ThumbnailService.GetThumbnailCacheKey("/test/image2.jpg", lastWrite);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetThumbnailCacheKeyReturnsDifferentKeyForDifferentTimestamp()
    {
        var filePath = "/test/image.jpg";

        var key1 = ThumbnailService.GetThumbnailCacheKey(filePath, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var key2 = ThumbnailService.GetThumbnailCacheKey(filePath, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetThumbnailCacheKeyThrowsForNullPath()
    {
        var lastWrite = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentNullException>(() => ThumbnailService.GetThumbnailCacheKey(null!, lastWrite));
    }

    [Fact]
    public void ThumbnailCacheExistsReturnsFalseForNonExistentCache()
    {
        var key = "NONEXISTENT_CACHE_KEY_THAT_SHOULD_NOT_EXIST";

        var exists = ThumbnailService.ThumbnailCacheExists(key);

        Assert.False(exists);
    }

    [Fact]
    public void ThumbnailCacheExistsThrowsForNullOrEmptyKey()
    {
        Assert.Throws<ArgumentNullException>(() => ThumbnailService.ThumbnailCacheExists(null!));
        Assert.Throws<ArgumentException>(() => ThumbnailService.ThumbnailCacheExists(string.Empty));
        Assert.Throws<ArgumentException>(() => ThumbnailService.ThumbnailCacheExists("   "));
    }

    [Fact]
    public void GetCachedThumbnailPathReturnsNullForNonExistentCache()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.jpg");
        var lastWrite = DateTime.UtcNow;

        var cachedPath = ThumbnailService.GetCachedThumbnailPath(filePath, lastWrite);

        Assert.Null(cachedPath);
    }

    [Fact]
    public void GetCachedThumbnailPathThrowsForNullPath()
    {
        var lastWrite = DateTime.UtcNow;

        Assert.Throws<ArgumentNullException>(() => ThumbnailService.GetCachedThumbnailPath(null!, lastWrite));
    }

    [Fact]
    public void GetImageSizeReturnsNullForNonExistentFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.jpg");

        var (width, height) = ThumbnailService.GetImageSize(filePath);

        Assert.Null(width);
        Assert.Null(height);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(16)]
    public async Task GetOrCreateThumbnailPathConcurrentCallsReturnValidPathWithoutErrorLog(int parallelism)
    {
        var root = Path.Combine(Path.GetTempPath(), $"PhotoGeoExplorerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string? thumbnailPath = null;
        try
        {
            var imagePath = Path.Combine(root, "image.jpg");
            using (var image = new Image<Rgba32>(1600, 1200))
            {
                await image.SaveAsJpegAsync(imagePath).ConfigureAwait(true);
            }

            var lastWriteUtc = new FileInfo(imagePath).LastWriteTimeUtc;
            var logOffset = GetLogLength();
            using var barrier = new Barrier(parallelism);

            var tasks = Enumerable.Range(0, parallelism).Select(index => Task.Run(() =>
            {
                barrier.SignalAndWait();
                return ThumbnailService.GetOrCreateThumbnailPath(imagePath, lastWriteUtc, out _, out _);
            })).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(true);

            thumbnailPath = Assert.Single(results.Distinct());
            Assert.NotNull(thumbnailPath);
            Assert.True(File.Exists(thumbnailPath));
            Assert.DoesNotContain($"ERROR Failed to cache thumbnail: {imagePath}", ReadLogSince(logOffset), StringComparison.Ordinal);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(thumbnailPath))
            {
                TryDeleteFile(thumbnailPath);
            }

            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GetOrCreateThumbnailPathLogsErrorAndReturnsNullForCorruptImage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PhotoGeoExplorerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var imagePath = Path.Combine(root, "broken.jpg");
            File.WriteAllBytes(imagePath, [0x00, 0x01, 0x02, 0x03]);
            var logOffset = GetLogLength();

            var result = ThumbnailService.GetOrCreateThumbnailPath(
                imagePath,
                new FileInfo(imagePath).LastWriteTimeUtc,
                out _,
                out _);

            Assert.Null(result);
            Assert.Contains($"ERROR Failed to cache thumbnail: {imagePath}", ReadLogSince(logOffset), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static long GetLogLength()
    {
        var info = new FileInfo(AppLog.LogFilePath);
        return info.Exists ? info.Length : 0;
    }

    private static string ReadLogSince(long offset)
    {
        if (!File.Exists(AppLog.LogFilePath))
        {
            return string.Empty;
        }

        using var stream = new FileStream(AppLog.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (offset > stream.Length)
        {
            return string.Empty;
        }

        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
