using PhotoGeoExplorer.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoGeoExplorer.Tests;

public sealed class FileSystemIntegrationTests
{
    [Fact]
    public async Task GetPhotoItemsAsyncReturnsCachedThumbnailForImage()
    {
        var root = CreateTempDirectory();
        string? thumbnailPath = null;
        try
        {
            var imagePath = Path.Combine(root, "image.png");
            using (var image = new Image<Rgba32>(1, 1))
            {
                image[0, 0] = new Rgba32(255, 255, 255, 255);
                await image.SaveAsPngAsync(imagePath).ConfigureAwait(true);
            }

            var service = new FileSystemService();
            var items = await service.GetPhotoItemsAsync(root, imagesOnly: true, searchText: null).ConfigureAwait(true);

            var item = Assert.Single(items);
            Assert.False(item.IsFolder);
            Assert.Null(item.ThumbnailPath);
            Assert.Null(item.PixelWidth);
            Assert.Null(item.PixelHeight);

            var fileInfo = new FileInfo(imagePath);
            var result = ThumbnailService.GenerateThumbnail(imagePath, fileInfo.LastWriteTimeUtc);
            thumbnailPath = result.ThumbnailPath;
            Assert.NotNull(thumbnailPath);
            Assert.True(File.Exists(thumbnailPath));
            Assert.Equal(1, result.Width);
            Assert.Equal(1, result.Height);

            var cachedItems = await service.GetPhotoItemsAsync(root, imagesOnly: true, searchText: null).ConfigureAwait(true);
            var cachedItem = Assert.Single(cachedItems);
            Assert.NotNull(cachedItem.ThumbnailPath);
            Assert.True(File.Exists(cachedItem.ThumbnailPath));
            Assert.Equal(1, cachedItem.PixelWidth);
            Assert.Equal(1, cachedItem.PixelHeight);
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
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
