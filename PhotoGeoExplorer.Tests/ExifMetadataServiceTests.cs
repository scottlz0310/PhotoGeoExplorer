using PhotoGeoExplorer.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoGeoExplorer.Tests;

public sealed class ExifMetadataServiceTests
{
    [Fact]
    public async Task UpdateMetadataAsyncDelegatesToExifWriter()
    {
        var root = CreateTempDirectory();
        try
        {
            var jpgPath = Path.Combine(root, "test.jpg");
            using (var image = new Image<Rgba32>(100, 100))
            {
                await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
            }

            var service = new ExifMetadataService();
            var takenAt = new DateTimeOffset(2024, 1, 15, 12, 30, 0, TimeSpan.Zero);

            var result = await service.UpdateMetadataAsync(
                jpgPath,
                takenAt,
                35.6762,
                139.6503,
                updateFileModifiedDate: false,
                CancellationToken.None).ConfigureAwait(true);

            Assert.True(result);

            var metadata = await service.GetMetadataAsync(jpgPath, CancellationToken.None).ConfigureAwait(true);
            Assert.NotNull(metadata);
            Assert.NotNull(metadata.TakenAt);
            Assert.Equal(takenAt.DateTime.Year, metadata.TakenAt.Value.DateTime.Year);
            Assert.NotNull(metadata.Latitude);
            Assert.NotNull(metadata.Longitude);
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
