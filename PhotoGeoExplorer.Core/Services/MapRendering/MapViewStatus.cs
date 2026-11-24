using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>地図の表示中心（緯度・経度）。</summary>
internal sealed record MapViewCenter(double Latitude, double Longitude);

/// <summary>地図の表示サイズ（論理ピクセル・物理ピクセル）。</summary>
internal sealed record MapViewSize(
    double LogicalWidth,
    double LogicalHeight,
    int PhysicalWidth,
    int PhysicalHeight);

/// <summary>地図の背景タイルソース情報。</summary>
internal sealed record MapViewTileSource(
    string Configured,
    string Displayed,
    string Attribution);

/// <summary>
/// 地図の現在状態の不変スナップショット。
/// 検証用の入口（#363）と地図ヘッダーのズーム表示（#364）で共有する単一情報源。
/// </summary>
internal sealed record MapViewStatus(
    bool HasValidViewport,
    MapViewCenter? Center,
    double? Zoom,
    int? StyleZoom,
    double PixelDensity,
    MapViewSize ViewportSize,
    MapViewTileSource TileSource,
    bool IsBusy,
    long RenderedSeq)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static MapViewStatus Empty { get; } = new(
        HasValidViewport: false,
        Center: null,
        Zoom: null,
        StyleZoom: null,
        PixelDensity: 1.0,
        ViewportSize: new MapViewSize(0, 0, 0, 0),
        TileSource: new MapViewTileSource(
            MapTileSourceType.OpenStreetMap.ToString(),
            MapTileSourceType.OpenStreetMap.ToString(),
            string.Empty),
        IsBusy: false,
        RenderedSeq: 0);

    /// <summary>
    /// ビューポートの生値と背景状態から <see cref="MapViewStatus"/> を構築する。
    /// 物理ピクセル寸法の丸めは <see cref="ViewExtentRequest"/> と同じ規則を使う。
    /// </summary>
    internal static MapViewStatus Create(
        bool hasSize,
        double centerX,
        double centerY,
        double resolution,
        double widthDip,
        double heightDip,
        double pixelDensity,
        MapTileSourceType configuredTileSource,
        MapTileSourceType displayedTileSource,
        string attributionText,
        bool isBusy,
        long renderedSeq)
    {
        var safeDensity = double.IsFinite(pixelDensity) && pixelDensity > 0 ? pixelDensity : 1.0;
        var tileSource = new MapViewTileSource(
            configuredTileSource.ToString(),
            displayedTileSource.ToString(),
            attributionText ?? string.Empty);

        var isValid = hasSize
            && double.IsFinite(centerX)
            && double.IsFinite(centerY)
            && double.IsFinite(resolution) && resolution > 0
            && double.IsFinite(widthDip) && widthDip > 0
            && double.IsFinite(heightDip) && heightDip > 0;

        if (!isValid)
        {
            return new MapViewStatus(
                HasValidViewport: false,
                Center: null,
                Zoom: null,
                StyleZoom: null,
                PixelDensity: safeDensity,
                ViewportSize: new MapViewSize(0, 0, 0, 0),
                TileSource: tileSource,
                IsBusy: isBusy,
                RenderedSeq: renderedSeq);
        }

        var physicalWidth = (int)Math.Round(widthDip * safeDensity);
        var physicalHeight = (int)Math.Round(heightDip * safeDensity);
        var latitude = WebMercator.ToLatitude(centerY);
        var longitude = WebMercator.ToLongitude(centerX);
        var logicalZoom = WebMercator.LogicalZoomFromResolution(resolution);

        if (physicalWidth <= 0
            || physicalHeight <= 0
            || !double.IsFinite(latitude)
            || !double.IsFinite(longitude)
            || !double.IsFinite(logicalZoom))
        {
            return new MapViewStatus(
                HasValidViewport: false,
                Center: null,
                Zoom: null,
                StyleZoom: null,
                PixelDensity: safeDensity,
                ViewportSize: new MapViewSize(0, 0, 0, 0),
                TileSource: tileSource,
                IsBusy: isBusy,
                RenderedSeq: renderedSeq);
        }

        var styleZoom = WebMercator.StyleZoomFromLogicalZoom(logicalZoom);
        return new MapViewStatus(
            HasValidViewport: true,
            Center: new MapViewCenter(latitude, longitude),
            Zoom: logicalZoom,
            StyleZoom: styleZoom,
            PixelDensity: safeDensity,
            ViewportSize: new MapViewSize(widthDip, heightDip, physicalWidth, physicalHeight),
            TileSource: tileSource,
            IsBusy: isBusy,
            RenderedSeq: renderedSeq);
    }

    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
