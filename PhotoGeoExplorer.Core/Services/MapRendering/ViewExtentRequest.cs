using System.Diagnostics.CodeAnalysis;

namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 表示範囲 1 枚分の描画要求。不変のスナップショットで、どのスレッドへ渡しても安全。
/// 地図の視点（<c>Viewport</c>）は描画の間に動くため、描画側は視点ではなく必ずこのスナップショットだけを見る。
/// </summary>
internal sealed class ViewExtentRequest
{
    private ViewExtentRequest(
        double centerX,
        double centerY,
        double resolution,
        double widthDip,
        double heightDip,
        double pixelDensity,
        string sourceKey)
    {
        CenterX = centerX;
        CenterY = centerY;
        Resolution = resolution;
        WidthDip = widthDip;
        HeightDip = heightDip;
        PixelDensity = pixelDensity;
        SourceKey = sourceKey;

        var halfWidth = widthDip * resolution / 2;
        var halfHeight = heightDip * resolution / 2;
        Extent = new GeoExtent(centerX - halfWidth, centerY - halfHeight, centerX + halfWidth, centerY + halfHeight);
        ImageWidth = (int)Math.Round(widthDip * pixelDensity);
        ImageHeight = (int)Math.Round(heightDip * pixelDensity);
    }

    public double CenterX { get; }

    public double CenterY { get; }

    /// <summary>表示解像度（m/論理ピクセル）。</summary>
    public double Resolution { get; }

    /// <summary>画面サイズ（論理ピクセル）。</summary>
    public double WidthDip { get; }

    public double HeightDip { get; }

    /// <summary>DPI 倍率（物理ピクセル／論理ピクセル）。</summary>
    public double PixelDensity { get; }

    /// <summary>
    /// 描画元とスタイルの識別子。値が違う要求どうしは互換でなく、片方の結果をもう片方の代わりに表示できない。
    /// </summary>
    public string SourceKey { get; }

    /// <summary>画像が覆う地理範囲。</summary>
    public GeoExtent Extent { get; }

    /// <summary>描く画像の物理ピクセル寸法。</summary>
    public int ImageWidth { get; }

    public int ImageHeight { get; }

    /// <summary>
    /// 描画可能な要求を作る。視点のサイズが未確定（0 や非数）のときは作らず false を返す。
    /// Mapsui はサイズ確定前にも視点変更を通知するため、通知側で例外にしないよう作成の失敗を戻り値で表す。
    /// </summary>
    public static bool TryCreate(
        double centerX,
        double centerY,
        double resolution,
        double widthDip,
        double heightDip,
        double pixelDensity,
        string sourceKey,
        [NotNullWhen(true)] out ViewExtentRequest? request)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);

        request = null;
        if (!double.IsFinite(centerX)
            || !double.IsFinite(centerY)
            || !IsPositive(resolution)
            || !IsPositive(widthDip)
            || !IsPositive(heightDip)
            || !IsPositive(pixelDensity))
        {
            return false;
        }

        var candidate = new ViewExtentRequest(centerX, centerY, resolution, widthDip, heightDip, pixelDensity, sourceKey);
        if (candidate.ImageWidth <= 0 || candidate.ImageHeight <= 0)
        {
            return false;
        }

        request = candidate;
        return true;
    }

    /// <summary>描画元・スタイルが同じで、結果を互いの代わりに使えるか。</summary>
    public bool IsCompatibleWith(ViewExtentRequest other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(SourceKey, other.SourceKey, StringComparison.Ordinal);
    }

    private static bool IsPositive(double value) => double.IsFinite(value) && value > 0;
}
