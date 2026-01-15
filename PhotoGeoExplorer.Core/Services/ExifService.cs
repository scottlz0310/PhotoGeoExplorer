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
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ImageSharpRational = SixLabors.ImageSharp.Rational;

namespace PhotoGeoExplorer.Services;

internal static class ExifService
{
    private static readonly byte[] ExifHeader = { 0x45, 0x78, 0x69, 0x66, 0x00, 0x00 };

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

        var payload = new byte[ExifHeader.Length + exifData.Length];
        Buffer.BlockCopy(ExifHeader, 0, payload, 0, ExifHeader.Length);
        Buffer.BlockCopy(exifData, 0, payload, ExifHeader.Length, exifData.Length);
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

        if (!TryReadByte(input, out var firstByte) || !TryReadByte(input, out var secondByte))
        {
            return false;
        }

        if (firstByte != 0xFF || secondByte != 0xD8)
        {
            return false;
        }

        output.WriteByte((byte)firstByte);
        output.WriteByte((byte)secondByte);

        var exifPayloadAvailable = exifPayload is not null;
        var exifWritten = false;
        var insertedAfterApp0 = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryReadByte(input, out var markerPrefix))
            {
                return false;
            }

            if (markerPrefix != 0xFF)
            {
                return false;
            }

            int marker;
            do
            {
                if (!TryReadByte(input, out marker))
                {
                    return false;
                }
            }
            while (marker == 0xFF);

            if (marker == 0xD9)
            {
                if (!exifWritten && exifPayloadAvailable)
                {
                    if (!TryWriteExifSegment(output, exifPayload!))
                    {
                        return false;
                    }

                    exifWritten = true;
                }

                output.WriteByte(0xFF);
                output.WriteByte((byte)marker);
                return true;
            }

            if (marker == 0xDA)
            {
                if (!exifWritten && exifPayloadAvailable)
                {
                    if (!TryWriteExifSegment(output, exifPayload!))
                    {
                        return false;
                    }

                    exifWritten = true;
                }

                output.WriteByte(0xFF);
                output.WriteByte((byte)marker);

                if (!TryReadUInt16(input, out var scanLength) || scanLength < 2)
                {
                    return false;
                }

                WriteUInt16(output, scanLength);

                if (!CopyBytes(input, output, scanLength - 2))
                {
                    return false;
                }

                input.CopyTo(output);
                return true;
            }

            if ((marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                output.WriteByte(0xFF);
                output.WriteByte((byte)marker);
                continue;
            }

            if (!TryReadUInt16(input, out var segmentLength) || segmentLength < 2)
            {
                return false;
            }

            var payloadLength = segmentLength - 2;
            var segmentData = new byte[payloadLength];
            if (!TryReadExact(input, segmentData, payloadLength))
            {
                return false;
            }

            var isExifSegment = marker == 0xE1 && IsExifSegment(segmentData);
            if (isExifSegment)
            {
                if (!exifWritten && exifPayloadAvailable)
                {
                    if (!TryWriteExifSegment(output, exifPayload!))
                    {
                        return false;
                    }

                    exifWritten = true;
                }

                continue;
            }

            if (!exifWritten && exifPayloadAvailable && !insertedAfterApp0 && marker != 0xE0)
            {
                if (!TryWriteExifSegment(output, exifPayload!))
                {
                    return false;
                }

                exifWritten = true;
                insertedAfterApp0 = true;
            }

            output.WriteByte(0xFF);
            output.WriteByte((byte)marker);
            WriteUInt16(output, segmentLength);
            output.Write(segmentData, 0, segmentData.Length);
        }
    }

    private static bool TryWriteExifSegment(Stream output, byte[] exifPayload)
    {
        var segmentLength = exifPayload.Length + 2;
        if (segmentLength > ushort.MaxValue)
        {
            return false;
        }

        output.WriteByte(0xFF);
        output.WriteByte(0xE1);
        WriteUInt16(output, (ushort)segmentLength);
        output.Write(exifPayload, 0, exifPayload.Length);
        return true;
    }

    private static bool IsExifSegment(byte[] segmentData)
    {
        if (segmentData.Length < ExifHeader.Length)
        {
            return false;
        }

        for (var i = 0; i < ExifHeader.Length; i++)
        {
            if (segmentData[i] != ExifHeader[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadByte(Stream stream, out int value)
    {
        value = stream.ReadByte();
        return value != -1;
    }

    private static bool TryReadUInt16(Stream stream, out ushort value)
    {
        value = 0;
        if (!TryReadByte(stream, out var highByte) || !TryReadByte(stream, out var lowByte))
        {
            return false;
        }

        value = (ushort)((highByte << 8) | lowByte);
        return true;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static bool CopyBytes(Stream input, Stream output, int byteCount)
    {
        var buffer = new byte[8192];
        var remaining = byteCount;

        while (remaining > 0)
        {
            var readCount = Math.Min(buffer.Length, remaining);
            var bytesRead = input.Read(buffer, 0, readCount);
            if (bytesRead <= 0)
            {
                return false;
            }

            output.Write(buffer, 0, bytesRead);
            remaining -= bytesRead;
        }

        return true;
    }

    private static bool TryReadExact(Stream stream, byte[] buffer, int length)
    {
        var offset = 0;
        while (offset < length)
        {
            var bytesRead = stream.Read(buffer, offset, length - offset);
            if (bytesRead == 0)
            {
                return false;
            }

            offset += bytesRead;
        }

        return true;
    }
}
