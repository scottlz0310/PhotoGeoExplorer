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
    private const int MaxThumbnailSize = 256;
    private const int ThumbnailJpegQuality = 90;
    private const string ThumbnailCacheVersion = "v2";
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "Cache",
        "Thumbnails");

    public static string? GetOrCreateThumbnailPath(string filePath, DateTime lastWriteUtc, out int? width, out int? height)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        width = null;
        height = null;

        var cacheKey = $"{filePath}|{lastWriteUtc.Ticks}|{MaxThumbnailSize}|{ThumbnailJpegQuality}|{ThumbnailCacheVersion}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)));
        var thumbnailPath = Path.Combine(CacheDirectory, $"{hash}.jpg");

        if (File.Exists(thumbnailPath))
        {
            TryGetImageSize(filePath, out width, out height);
            return thumbnailPath;
        }

        Directory.CreateDirectory(CacheDirectory);
        var tempPath = Path.Combine(CacheDirectory, $"{hash}.{Guid.NewGuid():N}.tmp");
        try
        {
            using var image = Image.Load(filePath);
            width = image.Width;
            height = image.Height;
            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(MaxThumbnailSize, MaxThumbnailSize),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3,
                Compand = true
            }));
            image.Save(tempPath, new JpegEncoder { Quality = ThumbnailJpegQuality });
            File.Move(tempPath, thumbnailPath, overwrite: true);
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
        finally
        {
            TryDelete(tempPath);
        }

        return File.Exists(thumbnailPath) ? thumbnailPath : null;
    }

    private static void TryGetImageSize(string filePath, out int? width, out int? height)
    {
        width = null;
        height = null;

        try
        {
            var info = Image.Identify(filePath);
            if (info is null)
            {
                return;
            }

            width = info.Width;
            height = info.Height;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (UnknownImageFormatException)
        {
        }
        catch (ImageProcessingException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
