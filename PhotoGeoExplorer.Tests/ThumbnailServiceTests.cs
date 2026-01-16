using System;
using System.IO;
using PhotoGeoExplorer.Services;

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
        Assert.Throws<ArgumentException>(() => ThumbnailService.ThumbnailCacheExists(null!));
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
}
