namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 地球上の 1 点（緯度・経度。度）。地図ライブラリの座標系（Web メルカトル）に依存しない。
/// </summary>
/// <param name="Latitude">緯度（度）</param>
/// <param name="Longitude">経度（度）</param>
internal readonly record struct GeoPoint(double Latitude, double Longitude);
