using Mapsui;

namespace PhotoMapStudio.Spike;

/// <summary>
/// 表示範囲画像の描画要求（不変のスナップショット）。Mapsui の Viewport と同じ単位（中心は EPSG:3857 の m、解像度は m/DIP、寸法は DIP）で持つ。
/// 画像の寸法は、DIP × 画素密度（物理ピクセル）。取得ズーム（整数）と表示倍率（連続）は、<see cref="Zoom"/> から描画側が分ける。
/// </summary>
public sealed record RenderRequest(
    double CenterX,
    double CenterY,
    double Resolution,
    double WidthDip,
    double HeightDip,
    double PixelDensity,
    string SourceId,
    int StyleVersion,
    long Generation)
{
    public int ImageWidth => (int)Math.Round(WidthDip * PixelDensity);

    public int ImageHeight => (int)Math.Round(HeightDip * PixelDensity);

    /// <summary>連続ズーム（256px タイルの物理ピクセル基準）。</summary>
    public double Zoom => ViewExtentInjection.ZoomFromResolution(Resolution, PixelDensity);

    /// <summary>画像が覆う地理範囲（EPSG:3857）。</summary>
    public MRect Extent
    {
        get
        {
            var halfW = WidthDip * Resolution / 2;
            var halfH = HeightDip * Resolution / 2;
            return new MRect(CenterX - halfW, CenterY - halfH, CenterX + halfW, CenterY + halfH);
        }
    }

    /// <summary>同じ取得元・スタイルで描かれた画像を、互いに差し替えてよいか。</summary>
    public bool IsCompatibleWith(RenderRequest other) => SourceId == other.SourceId && StyleVersion == other.StyleVersion;
}
