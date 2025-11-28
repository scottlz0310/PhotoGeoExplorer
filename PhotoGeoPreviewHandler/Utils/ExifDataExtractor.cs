using System.IO;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PhotoGeoPreviewHandler.Utils;

/// <summary>
/// Extracts GPS coordinates from image EXIF metadata.
/// </summary>
public static class ExifDataExtractor
{
    /// <summary>
    /// Extracts GPS coordinates from an image stream.
    /// </summary>
    /// <param name="imageStream">The stream containing the image data</param>
    /// <returns>GPS coordinates if found, otherwise null</returns>
    public static GpsCoordinate? ExtractGpsCoordinates(Stream imageStream)
    {
        if (imageStream == null)
        {
            throw new ArgumentNullException(nameof(imageStream));
        }

        try
        {
            // Ensure stream is at the beginning
            if (imageStream.CanSeek)
            {
                imageStream.Position = 0;
            }

            // Read metadata directories
            var directories = ImageMetadataReader.ReadMetadata(imageStream);

            // Find GPS directory
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
            if (gpsDirectory == null)
            {
                return null;
            }

            // Extract GPS location using individual tags
            var location = ExtractGeoLocation(gpsDirectory);
            if (location == null)
            {
                return null;
            }

            return new GpsCoordinate(location.Value.Latitude, location.Value.Longitude);
        }
        catch (Exception ex)
        {
            // Log error and return null if EXIF extraction fails
            System.Diagnostics.Trace.WriteLine($"EXIF extraction error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts additional EXIF metadata from an image stream.
    /// </summary>
    /// <param name="imageStream">The stream containing the image data</param>
    /// <returns>EXIF metadata information</returns>
    public static ExifMetadata? ExtractExifMetadata(Stream imageStream)
    {
        if (imageStream == null)
        {
            throw new ArgumentNullException(nameof(imageStream));
        }

        try
        {
            // Ensure stream is at the beginning
            if (imageStream.CanSeek)
            {
                imageStream.Position = 0;
            }

            var directories = ImageMetadataReader.ReadMetadata(imageStream);

            // Extract various EXIF tags
            var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var exifIfd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Extract GPS data
            GpsCoordinate? gpsCoord = null;
            if (gpsDirectory != null)
            {
                var location = ExtractGeoLocation(gpsDirectory);
                if (location.HasValue)
                {
                    gpsCoord = new GpsCoordinate(location.Value.Latitude, location.Value.Longitude);
                }
            }

            var metadata = new ExifMetadata
            {
                // GPS data
                GpsCoordinate = gpsCoord,

                // Date/Time
                DateTimeTaken = exifSubIfdDirectory?.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal),

                // Camera info
                CameraMake = exifIfd0Directory?.GetString(ExifDirectoryBase.TagMake),
                CameraModel = exifIfd0Directory?.GetString(ExifDirectoryBase.TagModel),

                // Image dimensions
                ImageWidth = exifSubIfdDirectory?.GetInt32(ExifDirectoryBase.TagExifImageWidth),
                ImageHeight = exifSubIfdDirectory?.GetInt32(ExifDirectoryBase.TagExifImageHeight),

                // GPS altitude (if available)
                Altitude = gpsDirectory?.GetDouble(GpsDirectory.TagAltitude)
            };

            return metadata;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"EXIF metadata extraction error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if an image stream contains GPS data.
    /// </summary>
    /// <param name="imageStream">The stream containing the image data</param>
    /// <returns>True if GPS data is present, otherwise false</returns>
    public static bool HasGpsData(Stream imageStream)
    {
        return ExtractGpsCoordinates(imageStream) != null;
    }

    /// <summary>
    /// Extracts geographic location from GPS directory.
    /// </summary>
    private static (double Latitude, double Longitude)? ExtractGeoLocation(GpsDirectory gpsDirectory)
    {
        try
        {
            // Get latitude
            var latitudeArray = gpsDirectory.GetRationalArray(GpsDirectory.TagLatitude);
            var latitudeRef = gpsDirectory.GetString(GpsDirectory.TagLatitudeRef);

            // Get longitude
            var longitudeArray = gpsDirectory.GetRationalArray(GpsDirectory.TagLongitude);
            var longitudeRef = gpsDirectory.GetString(GpsDirectory.TagLongitudeRef);

            if (latitudeArray == null || longitudeArray == null)
            {
                return null;
            }

            // Convert to decimal degrees
            double latitude = ConvertToDecimalDegrees(latitudeArray);
            double longitude = ConvertToDecimalDegrees(longitudeArray);

            // Apply hemisphere
            if (latitudeRef == "S")
            {
                latitude = -latitude;
            }
            if (longitudeRef == "W")
            {
                longitude = -longitude;
            }

            return (latitude, longitude);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts GPS coordinates from degrees/minutes/seconds to decimal degrees.
    /// </summary>
    private static double ConvertToDecimalDegrees(MetadataExtractor.Rational[] dms)
    {
        if (dms.Length < 3)
        {
            return 0;
        }

        double degrees = dms[0].ToDouble();
        double minutes = dms[1].ToDouble();
        double seconds = dms[2].ToDouble();

        return degrees + (minutes / 60.0) + (seconds / 3600.0);
    }

    /// <summary>
    /// Gets a formatted string representation of GPS coordinates.
    /// </summary>
    /// <param name="coordinate">The GPS coordinate</param>
    /// <returns>Formatted string (e.g., "35.6762° N, 139.6503° E")</returns>
    public static string FormatGpsCoordinate(GpsCoordinate coordinate)
    {
        var latDirection = coordinate.Latitude >= 0 ? "N" : "S";
        var lonDirection = coordinate.Longitude >= 0 ? "E" : "W";

        return $"{Math.Abs(coordinate.Latitude):F4}° {latDirection}, {Math.Abs(coordinate.Longitude):F4}° {lonDirection}";
    }
}

/// <summary>
/// Represents GPS coordinates extracted from EXIF data.
/// </summary>
public record GpsCoordinate(double Latitude, double Longitude)
{
    /// <summary>
    /// Returns true if this represents "Null Island" (0,0) - typically indicating no actual GPS data.
    /// </summary>
    public bool IsNullIsland => Math.Abs(Latitude) < 0.0001 && Math.Abs(Longitude) < 0.0001;

    /// <summary>
    /// Returns a formatted string representation of the coordinate.
    /// </summary>
    public override string ToString()
    {
        return ExifDataExtractor.FormatGpsCoordinate(this);
    }
}

/// <summary>
/// Contains comprehensive EXIF metadata extracted from an image.
/// </summary>
public class ExifMetadata
{
    public GpsCoordinate? GpsCoordinate { get; init; }
    public DateTime? DateTimeTaken { get; init; }
    public string? CameraMake { get; init; }
    public string? CameraModel { get; init; }
    public int? ImageWidth { get; init; }
    public int? ImageHeight { get; init; }
    public double? Altitude { get; init; }

    /// <summary>
    /// Returns true if GPS data is available.
    /// </summary>
    public bool HasGpsData => GpsCoordinate != null;

    /// <summary>
    /// Returns the camera name (Make + Model).
    /// </summary>
    public string? CameraName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CameraMake) && !string.IsNullOrWhiteSpace(CameraModel))
            {
                return $"{CameraMake} {CameraModel}";
            }
            return CameraMake ?? CameraModel;
        }
    }
}
