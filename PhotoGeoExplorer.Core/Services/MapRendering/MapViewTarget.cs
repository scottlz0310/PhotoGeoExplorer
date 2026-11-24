using System.Globalization;

namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 地図の表示位置（緯度・経度）と論理ズーム（連続値）の指定値。
/// </summary>
internal readonly record struct MapViewTarget(double Latitude, double Longitude, double Zoom)
{
    internal const double MinLatitude = -90.0;
    internal const double MaxLatitude = 90.0;
    internal const double MinLongitude = -180.0;
    internal const double MaxLongitude = 180.0;
    internal const double MinZoom = 0.0;
    internal const double MaxZoom = 22.0;

    /// <summary>
    /// 緯度・経度・ズームを検証して <see cref="MapViewTarget"/> を生成する。
    /// </summary>
    internal static bool TryCreate(
        double latitude,
        double longitude,
        double zoom,
        out MapViewTarget target,
        out string error)
    {
        target = default;

        if (!double.IsFinite(latitude) || latitude < MinLatitude || latitude > MaxLatitude)
        {
            error = $"Latitude must be finite and within [{MinLatitude}, {MaxLatitude}].";
            return false;
        }

        if (!double.IsFinite(longitude) || longitude < MinLongitude || longitude > MaxLongitude)
        {
            error = $"Longitude must be finite and within [{MinLongitude}, {MaxLongitude}].";
            return false;
        }

        if (!double.IsFinite(zoom) || zoom < MinZoom || zoom > MaxZoom)
        {
            error = $"Zoom must be finite and within [{MinZoom}, {MaxZoom}].";
            return false;
        }

        target = new MapViewTarget(latitude, longitude, zoom);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// <c>"緯度,経度,ズーム"</c> 形式の文字列を解析・検証して <see cref="MapViewTarget"/> を生成する。
    /// </summary>
    internal static bool TryParse(string? raw, out MapViewTarget target, out string error)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Map view specification is empty.";
            return false;
        }

        var parts = raw.Split(',');
        if (parts.Length != 3)
        {
            error = "Map view must have 3 comma-separated values: \"latitude,longitude,zoom\".";
            return false;
        }

        const NumberStyles numberStyle = NumberStyles.Float | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        if (!double.TryParse(parts[0], numberStyle, CultureInfo.InvariantCulture, out var latitude))
        {
            error = "Failed to parse latitude.";
            return false;
        }

        if (!double.TryParse(parts[1], numberStyle, CultureInfo.InvariantCulture, out var longitude))
        {
            error = "Failed to parse longitude.";
            return false;
        }

        if (!double.TryParse(parts[2], numberStyle, CultureInfo.InvariantCulture, out var zoom))
        {
            error = "Failed to parse zoom.";
            return false;
        }

        return TryCreate(latitude, longitude, zoom, out target, out error);
    }
}
