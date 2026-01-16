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
        return Task.Run(() => EnumerateFiles(folderPath, imagesOnly, normalizedSearch));
    }

    private List<PhotoItem> EnumerateFiles(string folderPath, bool imagesOnly, string? searchText)
    {
        var directories = new List<PhotoItem>();
        var files = new List<PhotoItem>();

        try
        {
            AppLog.Info($"EnumerateFiles: folderPath='{folderPath}', imagesOnly={imagesOnly}, searchText='{searchText ?? "(null)"}'");

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
                
                // キャッシュ済みサムネイルのみ取得（新規生成はしない）
                string? thumbnailPath = null;
                int? pixelWidth = null;
                int? pixelHeight = null;
                if (IsImage(info.FullName))
                {
                    thumbnailPath = ThumbnailService.GetCachedThumbnailPath(
                        info.FullName,
                        info.LastWriteTimeUtc);
                    
                    // キャッシュがある場合は解像度も取得
                    if (thumbnailPath is not null)
                    {
                        var size = ThumbnailService.GetImageSize(info.FullName);
                        pixelWidth = size.Width;
                        pixelHeight = size.Height;
                    }
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

            directories.Sort((left, right) => string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase));
            files.Sort((left, right) => string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase));

            var items = new List<PhotoItem>(directories.Count + files.Count);
            items.AddRange(directories);
            items.AddRange(files);

            AppLog.Info($"EnumerateFiles: Completed. Total: {items.Count} items ({directories.Count} dirs + {files.Count} files)");
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
