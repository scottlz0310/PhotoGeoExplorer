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
        var items = new List<PhotoItem>();

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
            if (IsImage(info.FullName))
            {
                thumbnailPath = ThumbnailService.GetOrCreateThumbnailPath(info.FullName, info.LastWriteTimeUtc);
            }

            items.Add(new PhotoItem(info.FullName, info.Length, info.LastWriteTime, thumbnailPath));
        }

        return items
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsImage(string path)
    {
        var extension = Path.GetExtension(path);
        return _imageExtensions.Contains(extension);
    }
}
