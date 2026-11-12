using System;
using System.IO;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 背景地図ごとの帰属表示（文言とリンク先）。
/// </summary>
internal static class MapAttribution
{
    internal const string OpenStreetMapText = "© OpenStreetMap contributors";
    internal const string OpenStreetMapUrl = "https://www.openstreetmap.org/copyright";
    internal const string EsriText = "Source: Esri, Vantor, Earthstar Geographics, and the GIS User Community";
    internal const string EsriUrl = "https://www.esri.com/";
    internal const string MapboxText = "© Mapbox © OpenStreetMap Improve this map";
    internal const string MapboxUrl = "https://www.mapbox.com/about/maps";

    /// <summary>
    /// Mapbox のロゴ（帰属表示用の公式 SVG）。Mapbox の背景を表示するときは文言とあわせて表示が必須。
    /// </summary>
    internal static string MapboxLogoPath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "MapAttribution",
        "mapbox-logo.svg");

    internal static (string Text, string Url) Get(MapTileSourceType sourceType)
    {
        return sourceType switch
        {
            MapTileSourceType.EsriWorldImagery => (EsriText, EsriUrl),
            MapTileSourceType.MapboxSatellite => (MapboxText, MapboxUrl),
            _ => (OpenStreetMapText, OpenStreetMapUrl)
        };
    }
}
