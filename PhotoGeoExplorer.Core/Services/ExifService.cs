using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.FileSystem;
using PhotoGeoExplorer.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ImageSharpRational = SixLabors.ImageSharp.Rational;

namespace PhotoGeoExplorer.Services;

internal static class ExifService
{
    public static Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return Task.Run(() => ReadMetadata(filePath, cancellationToken), cancellationToken);
    }

    public static Task<bool> UpdateMetadataAsync(
        string filePath,
        DateTimeOffset? takenAt,
        double? latitude,
        double? longitude,
        bool updateFileModifiedDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return Task.Run(() => WriteMetadata(filePath, takenAt, latitude, longitude, updateFileModifiedDate, cancellationToken), cancellationToken);
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

    private static bool WriteMetadata(
        string filePath,
        DateTimeOffset? takenAt,
        double? latitude,
        double? longitude,
        bool updateFileModifiedDate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Check if file is a supported image format
            var extension = Path.GetExtension(filePath);
            if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Error($"Unsupported file format for EXIF writing: {filePath}");
                return false;
            }

            // Create a backup file path
            var backupPath = filePath + ".bak";
            File.Copy(filePath, backupPath, overwrite: true);

            try
            {
                using var image = Image.Load(filePath);
                var exifProfile = image.Metadata.ExifProfile ?? new ExifProfile();

                // Update DateTime if provided
                if (takenAt.HasValue)
                {
                    var dateTimeString = takenAt.Value.DateTime.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                    exifProfile.SetValue(ExifTag.DateTimeOriginal, dateTimeString);
                    exifProfile.SetValue(ExifTag.DateTimeDigitized, dateTimeString);
                    exifProfile.SetValue(ExifTag.DateTime, dateTimeString);
                }

                // Update GPS location if provided
                if (latitude.HasValue && longitude.HasValue)
                {
                    // Set GPS version
                    exifProfile.SetValue(ExifTag.GPSVersionID, new byte[] { 2, 3, 0, 0 });

                    // Set latitude
                    var latRef = latitude.Value >= 0 ? "N" : "S";
                    var absLat = Math.Abs(latitude.Value);
                    var latDegrees = Math.Abs((int)absLat);
                    var latRemainder = (absLat - latDegrees) * 60;
                    var latMinutes = Math.Abs((int)latRemainder);
                    var latSeconds = Math.Abs((latRemainder - latMinutes) * 60);

                    exifProfile.SetValue(ExifTag.GPSLatitudeRef, latRef);
                    exifProfile.SetValue(ExifTag.GPSLatitude, new ImageSharpRational[]
                    {
                        new ImageSharpRational((uint)latDegrees, 1),
                        new ImageSharpRational((uint)latMinutes, 1),
                        new ImageSharpRational((uint)(latSeconds * 1000000), 1000000)
                    });

                    // Set longitude
                    var lonRef = longitude.Value >= 0 ? "E" : "W";
                    var absLon = Math.Abs(longitude.Value);
                    var lonDegrees = Math.Abs((int)absLon);
                    var lonRemainder = (absLon - lonDegrees) * 60;
                    var lonMinutes = Math.Abs((int)lonRemainder);
                    var lonSeconds = Math.Abs((lonRemainder - lonMinutes) * 60);

                    exifProfile.SetValue(ExifTag.GPSLongitudeRef, lonRef);
                    exifProfile.SetValue(ExifTag.GPSLongitude, new ImageSharpRational[]
                    {
                        new ImageSharpRational((uint)lonDegrees, 1),
                        new ImageSharpRational((uint)lonMinutes, 1),
                        new ImageSharpRational((uint)(lonSeconds * 1000000), 1000000)
                    });
                }

                // Save the image with updated EXIF data
                image.Metadata.ExifProfile = exifProfile;
                image.Save(filePath, new JpegEncoder());

                // Delete backup file on success
                File.Delete(backupPath);

                // Update file modified date if requested
                if (updateFileModifiedDate && takenAt.HasValue)
                {
                    File.SetLastWriteTime(filePath, takenAt.Value.DateTime);
                }

                AppLog.Info($"EXIF metadata updated: {filePath}");
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                or IOException
                or NotSupportedException
                or SixLabors.ImageSharp.ImageFormatException
                or SixLabors.ImageSharp.UnknownImageFormatException
                or SixLabors.ImageSharp.InvalidImageContentException)
            {
                // Restore from backup on failure
                AppLog.Error($"Failed to write EXIF metadata, restoring backup: {filePath}", ex);
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, filePath, overwrite: true);
                    File.Delete(backupPath);
                }
                return false;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or IOException
            or NotSupportedException
            or ArgumentException)
        {
            AppLog.Error($"Failed to update EXIF metadata: {filePath}", ex);
            return false;
        }
    }
}
