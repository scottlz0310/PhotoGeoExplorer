using System;
using System.Globalization;

namespace PhotoGeoExplorer.Models;

internal sealed class PhotoMetadata
{
    public PhotoMetadata(DateTimeOffset? takenAt, string? cameraMake, string? cameraModel, double? latitude, double? longitude)
    {
        TakenAt = takenAt;
        CameraMake = cameraMake;
        CameraModel = cameraModel;
        Latitude = latitude;
        Longitude = longitude;
    }

    public DateTimeOffset? TakenAt { get; }
    public string? CameraMake { get; }
    public string? CameraModel { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }

    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

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
