namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 投影座標系（EPSG:3857 の m）での矩形範囲。
/// </summary>
internal readonly record struct GeoExtent(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>辺が接するだけの範囲は重なりとみなさない。</summary>
    public bool Intersects(GeoExtent other)
        => MinX < other.MaxX && MaxX > other.MinX && MinY < other.MaxY && MaxY > other.MinY;
}
