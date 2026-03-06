using System;
using System.Globalization;

namespace PhotoGeoExplorer.Models;

internal sealed class PhotoMetadata
{
    private const double ZeroCoordinateThreshold = 0.000001;

    public PhotoMetadata(
        DateTimeOffset? takenAt,
        string? cameraMake,
        string? cameraModel,
        double? latitude,
        double? longitude,
        bool hasGpsData = false)
    {
        TakenAt = takenAt;
        CameraMake = cameraMake;
        CameraModel = cameraModel;
        Latitude = latitude;
        Longitude = longitude;
        HasGpsData = hasGpsData;
    }

    public DateTimeOffset? TakenAt { get; }
    public string? CameraMake { get; }
    public string? CameraModel { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }
    public bool HasGpsData { get; }

    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;
    public bool HasValidLocation => HasLocation
        && Latitude is double lat
        && Longitude is double lon
        && (Math.Abs(lat) >= ZeroCoordinateThreshold || Math.Abs(lon) >= ZeroCoordinateThreshold);
    public bool IsLikelyLocationFixFailed => HasGpsData && !HasValidLocation;

    public string? CameraSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CameraMake) && string.IsNullOrWhiteSpace(CameraModel))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(CameraMake))
            {
                return CameraModel;
            }

            if (string.IsNullOrWhiteSpace(CameraModel))
            {
                return CameraMake;
            }

            return $"{CameraMake} {CameraModel}";
        }
    }

    public string? TakenAtText => TakenAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}
