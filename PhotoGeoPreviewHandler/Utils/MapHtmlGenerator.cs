using System.IO;
using System.Reflection;
using System.Text;

namespace PhotoGeoPreviewHandler.Utils;

/// <summary>
/// Generates HTML for displaying Leaflet maps with GPS coordinates.
/// </summary>
public static class MapHtmlGenerator
{
    private static string? _cachedTemplate;
    private static readonly object _lockObject = new();

    /// <summary>
    /// Generates HTML for a Leaflet map with the given GPS coordinates.
    /// </summary>
    /// <param name="coordinate">GPS coordinate to display (null for Null Island)</param>
    /// <returns>Complete HTML string ready for WebView2</returns>
    public static string GenerateMapHtml(GpsCoordinate? coordinate)
    {
        var template = GetHtmlTemplate();

        // Determine values based on whether GPS data exists
        var hasGps = coordinate != null && !coordinate.IsNullIsland;
        var latitude = coordinate?.Latitude ?? 0.0;
        var longitude = coordinate?.Longitude ?? 0.0;
        var locationName = hasGps ? "Photo Location" : "Null Island";
        var formattedCoords = coordinate != null ? coordinate.ToString() : "0.0000° N, 0.0000° E";
        var zoomLevel = hasGps ? 13 : 2;

        // Replace template placeholders
        var html = template
            .Replace("{{LATITUDE}}", latitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{{LONGITUDE}}", longitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{{HAS_GPS}}", hasGps ? "true" : "false")
            .Replace("{{LOCATION_NAME}}", EscapeHtml(locationName))
            .Replace("{{FORMATTED_COORDS}}", EscapeHtml(formattedCoords))
            .Replace("{{ZOOM_LEVEL}}", zoomLevel.ToString());

        return html;
    }

    /// <summary>
    /// Generates HTML for a Leaflet map with additional metadata.
    /// </summary>
    /// <param name="metadata">EXIF metadata containing GPS and other information</param>
    /// <returns>Complete HTML string with enhanced popup content</returns>
    public static string GenerateMapHtml(ExifMetadata? metadata)
    {
        if (metadata?.GpsCoordinate == null)
        {
            return GenerateMapHtml((GpsCoordinate?)null);
        }

        var template = GetHtmlTemplate();
        var coordinate = metadata.GpsCoordinate;
        var hasGps = !coordinate.IsNullIsland;

        // Build location name with camera info if available
        var locationName = "Photo Location";
        if (!string.IsNullOrWhiteSpace(metadata.CameraName))
        {
            locationName = metadata.CameraName;
        }

        // Build formatted info
        var formattedCoords = coordinate.ToString();
        if (metadata.DateTimeTaken.HasValue)
        {
            formattedCoords += $"<br/>{metadata.DateTimeTaken.Value:yyyy-MM-dd HH:mm:ss}";
        }
        if (metadata.Altitude.HasValue)
        {
            formattedCoords += $"<br/>Altitude: {metadata.Altitude.Value:F1}m";
        }

        var html = template
            .Replace("{{LATITUDE}}", coordinate.Latitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{{LONGITUDE}}", coordinate.Longitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{{HAS_GPS}}", hasGps ? "true" : "false")
            .Replace("{{LOCATION_NAME}}", EscapeHtml(locationName))
            .Replace("{{FORMATTED_COORDS}}", formattedCoords)
            .Replace("{{ZOOM_LEVEL}}", hasGps ? "13" : "2");

        return html;
    }

    /// <summary>
    /// Gets the HTML template from embedded resources or file system.
    /// </summary>
    private static string GetHtmlTemplate()
    {
        // Return cached template if available
        if (_cachedTemplate != null)
        {
            return _cachedTemplate;
        }

        lock (_lockObject)
        {
            // Double-check after acquiring lock
            if (_cachedTemplate != null)
            {
                return _cachedTemplate;
            }

            try
            {
                // Try to load from file system first (for development)
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
                var templatePath = Path.Combine(assemblyDirectory!, "Resources", "map-template.html");

                if (File.Exists(templatePath))
                {
                    _cachedTemplate = File.ReadAllText(templatePath, Encoding.UTF8);
                    return _cachedTemplate;
                }

                // Try embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "PhotoGeoPreviewHandler.Resources.map-template.html";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    _cachedTemplate = reader.ReadToEnd();
                    return _cachedTemplate;
                }

                // Fallback to inline template if resource not found
                _cachedTemplate = GetFallbackTemplate();
                return _cachedTemplate;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Template loading error: {ex.Message}");
                return GetFallbackTemplate();
            }
        }
    }

    /// <summary>
    /// Returns a minimal inline HTML template as fallback.
    /// </summary>
    private static string GetFallbackTemplate()
    {
        return @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css'/>
    <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
    <style>
        body { margin: 0; padding: 0; }
        #map { width: 100%; height: 100vh; }
    </style>
</head>
<body>
    <div id='map'></div>
    <script>
        var map = L.map('map').setView([{{LATITUDE}}, {{LONGITUDE}}], {{ZOOM_LEVEL}});
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '&copy; OpenStreetMap contributors'
        }).addTo(map);
        L.marker([{{LATITUDE}}, {{LONGITUDE}}]).addTo(map)
            .bindPopup('{{LOCATION_NAME}}<br/>{{FORMATTED_COORDS}}')
            .openPopup();
    </script>
</body>
</html>";
    }

    /// <summary>
    /// Escapes HTML special characters to prevent XSS.
    /// </summary>
    private static string EscapeHtml(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    /// <summary>
    /// Clears the cached template (useful for development/testing).
    /// </summary>
    public static void ClearCache()
    {
        lock (_lockObject)
        {
            _cachedTemplate = null;
        }
    }
}
