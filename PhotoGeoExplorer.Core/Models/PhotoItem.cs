using System;
using System.Globalization;
using System.IO;

namespace PhotoGeoExplorer.Models;

internal sealed class PhotoItem
{
    public PhotoItem(
        string filePath,
        long sizeBytes,
        DateTimeOffset modifiedAt,
        bool isFolder,
        string? thumbnailPath = null,
        int? pixelWidth = null,
        int? pixelHeight = null,
        DateTimeOffset? takenAt = null,
        bool? hasLocation = null)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        FileName = Path.GetFileName(filePath);
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt;
        IsFolder = isFolder;
        ThumbnailPath = thumbnailPath;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        TakenAt = takenAt;
        HasLocation = hasLocation;
    }

    public string FilePath { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public DateTimeOffset ModifiedAt { get; }
    public bool IsFolder { get; }
    public string? ThumbnailPath { get; }
    public int? PixelWidth { get; }
    public int? PixelHeight { get; }
    public DateTimeOffset? TakenAt { get; }
    public bool? HasLocation { get; }

    public string SizeText => IsFolder ? string.Empty : FormatSize(SizeBytes);
    public string ModifiedAtText => ModifiedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string ResolutionText => FormatResolution(PixelWidth, PixelHeight, IsFolder);
    public string? TakenAtText => TakenAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

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

    private static string FormatResolution(int? width, int? height, bool isFolder)
    {
        if (isFolder || width is null || height is null)
        {
            return string.Empty;
        }

        if (width <= 0 || height <= 0)
        {
            return string.Empty;
        }

        return string.Format(CultureInfo.CurrentCulture, "{0} x {1}", width, height);
    }
}
