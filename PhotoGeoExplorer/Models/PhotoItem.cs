using System;
using System.Globalization;
using System.IO;

namespace PhotoGeoExplorer.Models;

internal sealed class PhotoItem
{
    public PhotoItem(string filePath, long sizeBytes, DateTimeOffset modifiedAt, string? thumbnailPath = null)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        FileName = Path.GetFileName(filePath);
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt;
        ThumbnailPath = thumbnailPath;
    }

    public string FilePath { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public DateTimeOffset ModifiedAt { get; }
    public string? ThumbnailPath { get; }

    public string SizeText => FormatSize(SizeBytes);
    public string ModifiedAtText => ModifiedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    private static string FormatSize(long sizeBytes)
    {
        double size = sizeBytes;
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.CurrentCulture, "{0:0.##} {1}", size, units[unitIndex]);
    }
}
