using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.FileSystem;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// EXIF メタデータの読み取り責務を担う（MetadataExtractor 依存はこのクラスに局所化する）
/// </summary>
internal static class ExifReader
{
    public static Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return Task.Run(() => ReadMetadata(filePath, cancellationToken), cancellationToken);
    }

    public static PhotoMetadata? GetMetadata(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return ReadMetadata(filePath, CancellationToken.None);
    }

    private static PhotoMetadata? ReadMetadata(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (MetadataExtractor.ImageProcessingException ex)
        {
            AppLog.Error($"Failed to read metadata: {filePath}", ex);
            return null;
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to read metadata: {filePath}", ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"Failed to read metadata: {filePath}", ex);
            return null;
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error($"Failed to read metadata: {filePath}", ex);
            return null;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            // Catch any other unexpected exceptions from MetadataExtractor library
            // (e.g., IndexOutOfRangeException when processing certain MP3 files)
            // to prevent app crashes. These are logged and treated as metadata read failures.
            AppLog.Error($"Unexpected exception reading metadata: {filePath}", ex);
            return null;
        }
#pragma warning restore CA1031 // Do not catch general exception types

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
            GeoLocation? location = null;
            if (gpsDirectory is not null && gpsDirectory.TryGetGeoLocation(out var geoLocation))
            {
                location = geoLocation;
            }

            double? latitude = location?.Latitude;
            double? longitude = location?.Longitude;

            var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var cameraMake = exifIfd0?.GetString(ExifDirectoryBase.TagMake);
            var cameraModel = exifIfd0?.GetString(ExifDirectoryBase.TagModel);

            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            DateTime? dateTime = null;
            if (subIfd is not null)
            {
                if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                    dateTime = dt;
                else if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out dt))
                    dateTime = dt;
            }

            if (dateTime is null)
            {
                var fileMeta = directories.OfType<FileMetadataDirectory>().FirstOrDefault();
                if (fileMeta is not null && fileMeta.TryGetDateTime(FileMetadataDirectory.TagFileModifiedDate, out var dt))
                    dateTime = dt;
            }

            DateTimeOffset? takenAt = null;
            if (dateTime.HasValue)
            {
                var localTime = DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Local);
                takenAt = new DateTimeOffset(localTime);
            }

            return new PhotoMetadata(
                takenAt,
                cameraMake,
                cameraModel,
                latitude,
                longitude,
                hasGpsData: gpsDirectory is not null);
        }
        catch (MetadataExtractor.MetadataException ex)
        {
            AppLog.Error($"Partial metadata read failure: {filePath}", ex);
            return null;
        }
    }
}
