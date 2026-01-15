using PhotoGeoExplorer.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoGeoExplorer.Tests;

public sealed class ExifServiceTests
{
    [Fact]
    public async Task UpdateMetadataAsyncUnsupportedFormatReturnsFalse()
    {
        var root = CreateTempDirectory();
        try
        {
            var txtPath = Path.Combine(root, "test.txt");
            await File.WriteAllTextAsync(txtPath, "test content").ConfigureAwait(true);

            var result = await ExifService.UpdateMetadataAsync(
                txtPath,
                DateTimeOffset.Now,
                35.6762,
                139.6503,
                updateFileModifiedDate: false,
                CancellationToken.None).ConfigureAwait(true);

            Assert.False(result);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpdateMetadataAsyncValidJpegReturnsTrue()
    {
        var root = CreateTempDirectory();
        try
        {
            var jpgPath = Path.Combine(root, "test.jpg");

            // Create a simple JPEG image
            using (var image = new Image<Rgba32>(100, 100))
            {
                await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
            }

            var takenAt = new DateTimeOffset(2024, 1, 15, 12, 30, 0, TimeSpan.Zero);
            var result = await ExifService.UpdateMetadataAsync(
                jpgPath,
                takenAt,
                35.6762,
                139.6503,
                updateFileModifiedDate: false,
                CancellationToken.None).ConfigureAwait(true);

            Assert.True(result);

            // Verify the metadata was written
            var metadata = await ExifService.GetMetadataAsync(jpgPath, CancellationToken.None).ConfigureAwait(true);
            Assert.NotNull(metadata);
            Assert.NotNull(metadata.TakenAt);
            Assert.Equal(takenAt.DateTime.Year, metadata.TakenAt.Value.DateTime.Year);
            Assert.Equal(takenAt.DateTime.Month, metadata.TakenAt.Value.DateTime.Month);
            Assert.Equal(takenAt.DateTime.Day, metadata.TakenAt.Value.DateTime.Day);
            Assert.NotNull(metadata.Latitude);
            Assert.NotNull(metadata.Longitude);
            Assert.True(Math.Abs(metadata.Latitude.Value - 35.6762) < 0.01);
            Assert.True(Math.Abs(metadata.Longitude.Value - 139.6503) < 0.01);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpdateMetadataAsyncFileModifiedDateOptionUpdatesFileDate()
    {
        var root = CreateTempDirectory();
        try
        {
            var jpgPath = Path.Combine(root, "test.jpg");

            // Create a simple JPEG image
            using (var image = new Image<Rgba32>(100, 100))
            {
                await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
            }

            var originalModifiedTime = File.GetLastWriteTime(jpgPath);
            var takenAt = new DateTimeOffset(2024, 1, 15, 12, 30, 0, TimeSpan.Zero);

            await ExifService.UpdateMetadataAsync(
                jpgPath,
                takenAt,
                null,
                null,
                updateFileModifiedDate: true,
                CancellationToken.None).ConfigureAwait(true);

            var newModifiedTime = File.GetLastWriteTime(jpgPath);
            Assert.NotEqual(originalModifiedTime, newModifiedTime);
            Assert.Equal(takenAt.DateTime, newModifiedTime);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpdateMetadataAsyncClearLocationRemovesGpsTags()
    {
        var root = CreateTempDirectory();
        try
        {
            var jpgPath = Path.Combine(root, "test.jpg");

            // Create a simple JPEG image
            using (var image = new Image<Rgba32>(100, 100))
            {
                await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
            }

            // First, add GPS data
            await ExifService.UpdateMetadataAsync(
                jpgPath,
                null,
                35.6762,
                139.6503,
                updateFileModifiedDate: false,
                CancellationToken.None).ConfigureAwait(true);

            var metadataWithGps = await ExifService.GetMetadataAsync(jpgPath, CancellationToken.None).ConfigureAwait(true);
            Assert.NotNull(metadataWithGps);
            Assert.NotNull(metadataWithGps.Latitude);
            Assert.NotNull(metadataWithGps.Longitude);

            // Now clear the GPS data
            var result = await ExifService.UpdateMetadataAsync(
                jpgPath,
                null,
                null,
                null,
                updateFileModifiedDate: false,
                CancellationToken.None).ConfigureAwait(true);

            Assert.True(result);

            // Verify GPS data is removed
            var metadataWithoutGps = await ExifService.GetMetadataAsync(jpgPath, CancellationToken.None).ConfigureAwait(true);
            Assert.NotNull(metadataWithoutGps);
            Assert.Null(metadataWithoutGps.Latitude);
            Assert.Null(metadataWithoutGps.Longitude);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpdateMetadataAsyncOnlyUpdateLocationPreservesDate()
    {
        var root = CreateTempDirectory();
        try
        {
            var jpgPath = Path.Combine(root, "test.jpg");

            // Create a simple JPEG image
            using (var image = new Image<Rgba32>(100, 100))
            {
                await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
            }

            // Set initial date
            var initialDate = new DateTimeOffset(2023, 1, 1, 10, 0, 0, TimeSpan.Zero);
            await ExifService.UpdateMetadataAsync(
                jpgPath,
                initialDate,
                null,
                null,
                updateFileModifiedDate: false,
                CancellationToken.None).ConfigureAwait(true);

            // Update only location, not date
            await ExifService.UpdateMetadataAsync(
                jpgPath,
                null,
                35.6762,
                139.6503,
                updateFileModifiedDate: false,
                CancellationToken.None).ConfigureAwait(true);

            // Verify date is preserved
            var metadata = await ExifService.GetMetadataAsync(jpgPath, CancellationToken.None).ConfigureAwait(true);
            Assert.NotNull(metadata);
            Assert.NotNull(metadata.TakenAt);
            Assert.Equal(initialDate.Year, metadata.TakenAt.Value.Year);
            Assert.Equal(initialDate.Month, metadata.TakenAt.Value.Month);
            Assert.Equal(initialDate.Day, metadata.TakenAt.Value.Day);
            Assert.NotNull(metadata.Latitude);
            Assert.NotNull(metadata.Longitude);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpdateMetadataAsyncNonExistentFileReturnsFalse()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.jpg");

        var result = await ExifService.UpdateMetadataAsync(
            nonExistentPath,
            DateTimeOffset.Now,
            35.6762,
            139.6503,
            updateFileModifiedDate: false,
            CancellationToken.None).ConfigureAwait(true);

        Assert.False(result);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
