using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ImageSharpRational = SixLabors.ImageSharp.Rational;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// EXIF メタデータの書き込み責務を担う（読み取りは <see cref="ExifReader"/> を参照）
/// </summary>
internal static class ExifWriter
{
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

            var originalLastWriteTime = File.GetLastWriteTime(filePath);

            // Create a backup file path
            var backupPath = filePath + ".bak";
            File.Copy(filePath, backupPath, overwrite: true);

            try
            {
                var imageInfo = Image.Identify(filePath);
                if (imageInfo is null)
                {
                    AppLog.Error($"Failed to identify image metadata: {filePath}");
                    return false;
                }

                var exifProfile = imageInfo.Metadata.ExifProfile?.DeepClone() ?? new ExifProfile();

                // Update DateTime if provided
                if (takenAt.HasValue)
                {
                    var dateTimeString = takenAt.Value.DateTime.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                    exifProfile.SetValue(ExifTag.DateTimeOriginal, dateTimeString);
                    exifProfile.SetValue(ExifTag.DateTimeDigitized, dateTimeString);
                    exifProfile.SetValue(ExifTag.DateTime, dateTimeString);
                }

                // Update GPS location if provided, or remove if both are null
                if (latitude.HasValue && longitude.HasValue)
                {
                    // Set GPS version
                    exifProfile.SetValue(ExifTag.GPSVersionID, new byte[] { 2, 3, 0, 0 });

                    // Set latitude
                    var latRef = latitude.Value >= 0 ? "N" : "S";
                    var absLat = Math.Abs(latitude.Value);
                    var latDegrees = (int)absLat;
                    var latRemainder = (absLat - latDegrees) * 60;
                    var latMinutes = (int)latRemainder;
                    var latSeconds = (latRemainder - latMinutes) * 60;

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
                    var lonDegrees = (int)absLon;
                    var lonRemainder = (absLon - lonDegrees) * 60;
                    var lonMinutes = (int)lonRemainder;
                    var lonSeconds = (lonRemainder - lonMinutes) * 60;

                    exifProfile.SetValue(ExifTag.GPSLongitudeRef, lonRef);
                    exifProfile.SetValue(ExifTag.GPSLongitude, new ImageSharpRational[]
                    {
                        new ImageSharpRational((uint)lonDegrees, 1),
                        new ImageSharpRational((uint)lonMinutes, 1),
                        new ImageSharpRational((uint)(lonSeconds * 1000000), 1000000)
                    });
                }
                else if (!latitude.HasValue && !longitude.HasValue)
                {
                    // Remove GPS tags when clearing location
                    exifProfile.RemoveValue(ExifTag.GPSVersionID);
                    exifProfile.RemoveValue(ExifTag.GPSLatitudeRef);
                    exifProfile.RemoveValue(ExifTag.GPSLatitude);
                    exifProfile.RemoveValue(ExifTag.GPSLongitudeRef);
                    exifProfile.RemoveValue(ExifTag.GPSLongitude);
                }

                var exifPayload = BuildExifPayload(exifProfile);
                if (!WriteJpegWithUpdatedExif(backupPath, filePath, exifPayload, cancellationToken))
                {
                    AppLog.Error($"Failed to write EXIF metadata: {filePath}");
                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, filePath, overwrite: true);
                        File.SetLastWriteTime(filePath, originalLastWriteTime);
                        File.Delete(backupPath);
                    }
                    return false;
                }

                // Delete backup file on success
                File.Delete(backupPath);

                // Update file modified date if requested
                if (updateFileModifiedDate && takenAt.HasValue)
                {
                    File.SetLastWriteTime(filePath, takenAt.Value.DateTime);
                }
                else
                {
                    File.SetLastWriteTime(filePath, originalLastWriteTime);
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

    private static byte[]? BuildExifPayload(ExifProfile exifProfile)
    {
        if (exifProfile.Values.Count == 0)
        {
            return null;
        }

        var exifData = exifProfile.ToByteArray();
        if (exifData is null || exifData.Length == 0)
        {
            return null;
        }

        var payload = new byte[JpegExifSegmentWriter.ExifHeader.Length + exifData.Length];
        Buffer.BlockCopy(JpegExifSegmentWriter.ExifHeader, 0, payload, 0, JpegExifSegmentWriter.ExifHeader.Length);
        Buffer.BlockCopy(exifData, 0, payload, JpegExifSegmentWriter.ExifHeader.Length, exifData.Length);
        return payload;
    }

    private static bool WriteJpegWithUpdatedExif(
        string sourcePath,
        string destinationPath,
        byte[]? exifPayload,
        CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(sourcePath);
        using var output = File.Create(destinationPath);
        return JpegExifSegmentWriter.Write(input, output, exifPayload, cancellationToken);
    }
}
