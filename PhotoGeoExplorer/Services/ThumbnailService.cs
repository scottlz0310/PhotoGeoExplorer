using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PhotoGeoExplorer.Services;

internal static class ThumbnailService
{
    private const int MaxThumbnailSize = 96;
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "Cache",
        "Thumbnails");

    public static string? GetOrCreateThumbnailPath(string filePath, DateTime lastWriteUtc)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var cacheKey = $"{filePath}|{lastWriteUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)));
        var thumbnailPath = Path.Combine(CacheDirectory, $"{hash}.jpg");

        if (File.Exists(thumbnailPath))
        {
            return thumbnailPath;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            using var image = Image.Load(filePath);
            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(MaxThumbnailSize, MaxThumbnailSize),
                Mode = ResizeMode.Max
            }));
            image.Save(thumbnailPath, new JpegEncoder { Quality = 80 });
            return thumbnailPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"Failed to cache thumbnail: {filePath}", ex);
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to cache thumbnail: {filePath}", ex);
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error($"Failed to cache thumbnail: {filePath}", ex);
        }
        catch (UnknownImageFormatException ex)
        {
            AppLog.Error($"Failed to cache thumbnail: {filePath}", ex);
        }
        catch (ImageProcessingException ex)
        {
            AppLog.Error($"Failed to cache thumbnail: {filePath}", ex);
        }

        return File.Exists(thumbnailPath) ? thumbnailPath : null;
    }
}
