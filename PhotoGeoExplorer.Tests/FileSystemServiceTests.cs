using PhotoGeoExplorer.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoGeoExplorer.Tests;

public sealed class FileSystemServiceTests
{
    [Fact]
    public async Task GetPhotoItemsAsyncReturnsDirectoriesBeforeFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "BFolder"));
            Directory.CreateDirectory(Path.Combine(root, "AFolder"));
            await File.WriteAllTextAsync(Path.Combine(root, "b.txt"), "b").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "a").ConfigureAwait(true);

            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: false, searchText: null).ConfigureAwait(true);

            Assert.Equal(4, items.Count);
            Assert.True(items[0].IsFolder);
            Assert.Equal("AFolder", items[0].FileName);
            Assert.True(items[1].IsFolder);
            Assert.Equal("BFolder", items[1].FileName);
            Assert.False(items[2].IsFolder);
            Assert.Equal("a.txt", items[2].FileName);
            Assert.False(items[3].IsFolder);
            Assert.Equal("b.txt", items[3].FileName);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetPhotoItemsAsyncImagesOnlyFiltersNonImages()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Images"));
            await File.WriteAllTextAsync(Path.Combine(root, "note.txt"), "note").ConfigureAwait(true);

            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: true, searchText: null).ConfigureAwait(true);

            var item = Assert.Single(items);
            Assert.True(item.IsFolder);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GetChildDirectoriesReturnsSortedNames()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Zoo"));
            Directory.CreateDirectory(Path.Combine(root, "Alpha"));

            var children = FileSystemService.GetChildDirectories(root);

            Assert.Equal(2, children.Count);
            Assert.Equal("Alpha", children[0].Name);
            Assert.Equal("Zoo", children[1].Name);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetPhotoItemsAsyncHandlesEmptyFolder()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: false, searchText: null).ConfigureAwait(true);

            Assert.Empty(items);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetPhotoItemsAsyncHandlesEmptyFolderWithImagesOnly()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: true, searchText: null).ConfigureAwait(true);

            Assert.Empty(items);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetPhotoItemsAsyncLogsEmptyFolderCorrectly()
    {
        var root = CreateTempDirectory();
        try
        {
            var service = new FileSystemService();

            // This should not throw and should log properly
            var exception = await Record.ExceptionAsync(async () =>
            {
                var items = await service.GetPhotoItemsAsync(root, imagesOnly: false, searchText: null).ConfigureAwait(true);
                Assert.Empty(items);
            }).ConfigureAwait(true);

            Assert.Null(exception);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetPhotoItemsAsyncDoesNotReadExifColumnsDuringInitialEnumeration()
    {
        var root = CreateTempDirectory();
        try
        {
            var jpgPath = Path.Combine(root, "test.jpg");
            using (var image = new Image<Rgba32>(100, 100))
            {
                await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
            }

            var takenAt = new DateTimeOffset(2024, 1, 15, 12, 30, 0, TimeSpan.Zero);
            await ExifService.UpdateMetadataAsync(
                jpgPath,
                takenAt,
                35.6762,
                139.6503,
                updateFileModifiedDate: false,
                CancellationToken.None).ConfigureAwait(true);

            var expectedLastWriteTime = new DateTime(2025, 5, 6, 7, 8, 0);
            File.SetLastWriteTime(jpgPath, expectedLastWriteTime);

            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: true, searchText: null).ConfigureAwait(true);

            var item = Assert.Single(items);
            Assert.NotNull(item.TakenAt);
            Assert.Equal(expectedLastWriteTime.Year, item.TakenAt!.Value.Year);
            Assert.Equal(expectedLastWriteTime.Month, item.TakenAt.Value.Month);
            Assert.Equal(expectedLastWriteTime.Day, item.TakenAt.Value.Day);
            Assert.False(item.HasLocation.GetValueOrDefault(true));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetPhotoItemsAsyncFallsBackToFileTimestampWhenExifDateIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var jpgPath = Path.Combine(root, "no-exif.jpg");
            using (var image = new Image<Rgba32>(100, 100))
            {
                await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
            }

            var expectedLastWriteTime = new DateTime(2024, 3, 4, 5, 6, 0);
            File.SetLastWriteTime(jpgPath, expectedLastWriteTime);

            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: true, searchText: null).ConfigureAwait(true);

            var item = Assert.Single(items);
            Assert.NotNull(item.TakenAt);
            Assert.Equal(expectedLastWriteTime.Year, item.TakenAt!.Value.Year);
            Assert.Equal(expectedLastWriteTime.Month, item.TakenAt.Value.Month);
            Assert.Equal(expectedLastWriteTime.Day, item.TakenAt.Value.Day);
            Assert.False(item.HasLocation.GetValueOrDefault(true));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
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
