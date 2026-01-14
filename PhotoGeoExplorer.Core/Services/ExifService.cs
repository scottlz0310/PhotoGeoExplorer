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

internal static class ExifService
{
    public static Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return Task.Run(() => ReadMetadata(filePath, cancellationToken), cancellationToken);
    }

    private static PhotoMetadata? ReadMetadata(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (ImageProcessingException ex)
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
        var dateTime = subIfd?.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal)
            ?? subIfd?.GetDateTime(ExifDirectoryBase.TagDateTimeDigitized)
            ?? directories.OfType<FileMetadataDirectory>().FirstOrDefault()?.GetDateTime(FileMetadataDirectory.TagFileModifiedDate);

        DateTimeOffset? takenAt = null;
        if (dateTime.HasValue)
        {
            var localTime = DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Local);
            takenAt = new DateTimeOffset(localTime);
        }

        return new PhotoMetadata(takenAt, cameraMake, cameraModel, latitude, longitude);
    }
}
