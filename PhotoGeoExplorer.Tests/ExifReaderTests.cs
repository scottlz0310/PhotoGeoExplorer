using PhotoGeoExplorer.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoGeoExplorer.Tests;

public sealed class ExifReaderTests
{
    [Fact]
    public async Task GetMetadataAsyncJpegWithExifSubIfdButNoDateTimeTagsDoesNotThrow()
    {
        var root = CreateTempDirectory();
        try
        {
            var jpgPath = Path.Combine(root, "no_datetime.jpg");

            using (var image = new Image<Rgba32>(100, 100))
            {
                var exifProfile = new ExifProfile();
                // ExifVersion を設定して ExifSubIfdDirectory を生成させる（Date/Time Original は意図的に設定しない）
                exifProfile.SetValue(ExifTag.ExifVersion, new byte[] { 0x30, 0x32, 0x32, 0x30 });
                image.Metadata.ExifProfile = exifProfile;
                await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
            }

            // MetadataException をスローせずに正常完了することを確認する
            // Date/Time Original 不在時はファイル更新日時にフォールバックするため TakenAt は非 null になる
            var metadata = await ExifReader.GetMetadataAsync(jpgPath, CancellationToken.None).ConfigureAwait(true);

            Assert.NotNull(metadata);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetMetadataAsyncNonImageFileReturnsNull()
    {
        var root = CreateTempDirectory();
        try
        {
            var txtPath = Path.Combine(root, "test.txt");
            await File.WriteAllTextAsync(txtPath, "test content").ConfigureAwait(true);

            var metadata = await ExifReader.GetMetadataAsync(txtPath, CancellationToken.None).ConfigureAwait(true);

            Assert.Null(metadata);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetMetadataAsyncNonExistentFileReturnsNull()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.jpg");

        var metadata = await ExifReader.GetMetadataAsync(nonExistentPath, CancellationToken.None).ConfigureAwait(true);

        Assert.Null(metadata);
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
