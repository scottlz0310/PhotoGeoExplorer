using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

internal sealed class FileSystemService
{
    private readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff",
        ".heic",
        ".webp"
    };

    public Task<List<PhotoItem>> GetPhotoItemsAsync(string folderPath, bool imagesOnly, string? searchText)
    {
        ArgumentNullException.ThrowIfNull(folderPath);

        var normalizedSearch = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
        AppLog.Info($"GetPhotoItemsAsync: folderPath='{folderPath}', imagesOnly={imagesOnly}, searchText='{normalizedSearch ?? "(null)"}'");
        return Task.Run(() => EnumerateFiles(folderPath, imagesOnly, normalizedSearch));
    }

    private List<PhotoItem> EnumerateFiles(string folderPath, bool imagesOnly, string? searchText)
    {
        var directories = new List<PhotoItem>();
        var files = new List<PhotoItem>();

        try
        {
            AppLog.Info($"EnumerateFiles: Starting enumeration for '{folderPath}'");

            foreach (var path in Directory.EnumerateDirectories(folderPath))
            {
                var directoryName = Path.GetFileName(path);
                if (searchText is not null
                    && !directoryName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = new DirectoryInfo(path);
                directories.Add(new PhotoItem(info.FullName, 0, info.LastWriteTime, isFolder: true));
            }

            AppLog.Info($"EnumerateFiles: Found {directories.Count} directories");

            foreach (var path in Directory.EnumerateFiles(folderPath))
            {
                if (imagesOnly && !IsImage(path))
                {
                    continue;
                }

                if (searchText is not null)
                {
                    var fileName = Path.GetFileName(path);
                    if (!fileName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                var info = new FileInfo(path);
                string? thumbnailPath = null;
                int? pixelWidth = null;
                int? pixelHeight = null;
                if (IsImage(info.FullName))
                {
                    thumbnailPath = ThumbnailService.GetOrCreateThumbnailPath(
                        info.FullName,
                        info.LastWriteTimeUtc,
                        out var width,
                        out var height);
                    pixelWidth = width;
                    pixelHeight = height;
                }

                files.Add(new PhotoItem(
                    info.FullName,
                    info.Length,
                    info.LastWriteTime,
                    isFolder: false,
                    thumbnailPath,
                    pixelWidth,
                    pixelHeight));
            }

            AppLog.Info($"EnumerateFiles: Found {files.Count} files");

            directories.Sort((left, right) => string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase));
            files.Sort((left, right) => string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase));

            var items = new List<PhotoItem>(directories.Count + files.Count);
            items.AddRange(directories);
            items.AddRange(files);

            AppLog.Info($"EnumerateFiles: Returning {items.Count} total items ({directories.Count} dirs + {files.Count} files)");
            return items;
        }
        catch (Exception ex)
        {
            AppLog.Error($"EnumerateFiles: Exception during enumeration of '{folderPath}'", ex);
            throw;
        }
    }

    private bool IsImage(string path)
    {
        var extension = Path.GetExtension(path);
        return _imageExtensions.Contains(extension);
    }

    public static List<BreadcrumbChild> GetChildDirectories(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);

        var children = new List<BreadcrumbChild>();
        try
        {
            foreach (var path in Directory.EnumerateDirectories(folderPath))
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                children.Add(new BreadcrumbChild(name, path));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"Failed to read child folders: {folderPath}", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            AppLog.Error($"Failed to read child folders: {folderPath}", ex);
        }
        catch (PathTooLongException ex)
        {
            AppLog.Error($"Failed to read child folders: {folderPath}", ex);
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to read child folders: {folderPath}", ex);
        }

        children.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return children;
    }
}
