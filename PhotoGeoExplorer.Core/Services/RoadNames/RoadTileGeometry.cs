namespace PhotoGeoExplorer.Services.RoadNames;

/// <summary>
/// Web メルカトルのタイル座標と、タイル内の距離の計算
/// </summary>
internal static class RoadTileGeometry
{
    private const double EquatorLengthMeters = 40075016.686;

    /// <summary>Web メルカトルで表せる緯度の上限（度）</summary>
    internal const double MaxLatitude = 85.0511;

    internal static bool IsRepresentable(double latitude, double longitude)
        => double.IsFinite(latitude) && double.IsFinite(longitude)
            && Math.Abs(latitude) <= MaxLatitude && Math.Abs(longitude) <= 180d;

    /// <returns>タイルの座標（整数部がタイルの番号、小数部がタイル内の位置）</returns>
    internal static (double X, double Y) ToTileCoordinates(double latitude, double longitude, int zoom)
    {
        var tiles = Math.Pow(2, zoom);
        var radians = latitude * Math.PI / 180d;
        var x = (longitude + 180d) / 360d * tiles;
        var y = (1d - (Math.Log(Math.Tan(radians) + (1d / Math.Cos(radians))) / Math.PI)) / 2d * tiles;
        return (x, y);
    }

    /// <summary>
    /// タイルの幅（メートル）。メルカトルは等角なので、縦も同じ縮尺になる
    /// </summary>
    internal static double TileWidthMeters(double latitude, int zoom)
        => EquatorLengthMeters * Math.Cos(latitude * Math.PI / 180d) / Math.Pow(2, zoom);

    internal static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0d ? 0d : Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / lengthSquared, 0d, 1d);
        var cx = ax + (t * dx);
        var cy = ay + (t * dy);
        return Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
    }
}
