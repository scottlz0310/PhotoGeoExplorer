namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// Web メルカトル（EPSG:3857）と、緯度経度・連続ズームの変換。Mapsui の視点（m 単位の中心、m/論理ピクセルの解像度）を、
/// ベクター描画の入力（緯度経度、256px タイルの物理ピクセル基準の連続ズーム）へ変えるために使う。
/// </summary>
internal static class WebMercator
{
    /// <summary>256px タイルのズーム 0 における 1 物理ピクセルあたりのメートル数。</summary>
    internal const double ZoomZeroResolution = 156543.03392804097;

    /// <summary>球面メルカトルの地球半径（m）。</summary>
    internal const double EarthRadius = 6378137.0;

    /// <summary>Web メルカトルで表現できる最大の緯度（度）。</summary>
    internal const double MaxLatitude = 85.05112877980659;

    /// <summary>世界全体の範囲の、中心から端までの距離（m。赤道上の半周）。東西・南北とも ±この値が世界の範囲になる。</summary>
    internal const double HalfWorldExtent = Math.PI * EarthRadius;

    /// <summary>
    /// ズームレベル 0 から <paramref name="maxLevel"/> までの、各レベルの解像度（m/論理ピクセル）。レベルが 1 上がるごとに半分になる。
    /// </summary>
    internal static double[] ZoomLevelResolutions(int maxLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLevel);
        return [.. Enumerable.Range(0, maxLevel + 1).Select(level => ZoomZeroResolution / Math.Pow(2, level))];
    }

    /// <summary>東西方向の座標（m）から経度（度）を求める。</summary>
    internal static double ToLongitude(double x) => x / EarthRadius * (180.0 / Math.PI);

    /// <summary>南北方向の座標（m）から緯度（度）を求める。</summary>
    internal static double ToLatitude(double y) => Math.Atan(Math.Sinh(y / EarthRadius)) * (180.0 / Math.PI);

    /// <summary>経度（度）から東西方向の座標（m）を求める。</summary>
    internal static double FromLongitude(double longitude) => EarthRadius * longitude * (Math.PI / 180.0);

    /// <summary>緯度（度）から南北方向の座標（m）を求める。Web メルカトルの表現上限（±<see cref="MaxLatitude"/>）へ丸める。</summary>
    internal static double FromLatitude(double latitude)
    {
        var clamped = Math.Clamp(latitude, -MaxLatitude, MaxLatitude);
        return EarthRadius * Math.Log(Math.Tan((Math.PI / 4.0) + (clamped * Math.PI / 360.0)));
    }

    /// <summary>
    /// 表示解像度（m/論理ピクセル）から、設定 <c>mapDefaultZoomLevel</c> と同じ尺度の論理ズーム（連続値）を求める。
    /// </summary>
    internal static double LogicalZoomFromResolution(double resolution)
        => Math.Log2(ZoomZeroResolution / resolution);

    /// <summary>
    /// 論理ズーム（連続値）から表示解像度（m/論理ピクセル）を求める。
    /// </summary>
    internal static double ResolutionFromLogicalZoom(double logicalZoom)
        => ZoomZeroResolution / Math.Pow(2.0, logicalZoom);

    /// <summary>
    /// 論理ズーム（連続値）から、ベクタースタイルが条件判定に使う整数ズームを求める。
    /// </summary>
    internal static int StyleZoomFromLogicalZoom(double logicalZoom)
        => (int)Math.Floor(logicalZoom);

    /// <summary>
    /// 表示解像度（m/論理ピクセル）と画素密度から、256px タイルを物理ピクセルで数えた連続ズームを求める。
    /// 同じ視点でも画素密度が高いほど、物理ピクセルあたりの地上距離が短くなるため、ズームは大きくなる。
    /// </summary>
    internal static double ZoomFromResolution(double resolution, double pixelDensity)
        => Math.Log2(ZoomZeroResolution * pixelDensity / resolution);
}
