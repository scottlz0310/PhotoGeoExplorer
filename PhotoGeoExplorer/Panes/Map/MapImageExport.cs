using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 保存する地図画像の条件。表示中の地図とは独立に描く（#366）。
/// </summary>
/// <param name="Background">画像の背景。表示中の背景と違ってよい</param>
/// <param name="CenterX">中心の X（Web メルカトル、メートル）</param>
/// <param name="CenterY">中心の Y（Web メルカトル、メートル）</param>
/// <param name="LogicalZoom">論理ズーム（小数可）。画面のズーム表示と同じ基準</param>
/// <param name="PixelWidth">画像の幅（ピクセル）</param>
/// <param name="PixelHeight">画像の高さ（ピクセル）</param>
/// <param name="PixelDensity">文字や線の大きさの倍率（物理ピクセル／論理ピクセル）。大きいほど写る範囲は狭くなる</param>
/// <param name="Markers">画像に載せる写真ピン</param>
internal sealed record MapImageExportRequest(
    MapBackgroundRequest Background,
    double CenterX,
    double CenterY,
    double LogicalZoom,
    int PixelWidth,
    int PixelHeight,
    double PixelDensity,
    IReadOnlyList<MapMarker> Markers);

/// <summary>
/// 条件のままでは画像を作れない理由
/// </summary>
internal enum MapImageExportProblem
{
    None,
    CenterOutOfRange,
    SizeOutOfRange,
    ZoomOutOfRange,
    PixelDensityOutOfRange,
    TooManyTiles,
}

/// <summary>
/// 条件の検査結果
/// </summary>
/// <param name="Problem">作れない理由。作れるときは <see cref="MapImageExportProblem.None"/></param>
/// <param name="TileCount">1 回の描画で使うタイルの枚数。数値の条件が範囲外のときは 0</param>
internal sealed record MapImageExportPlan(MapImageExportProblem Problem, int TileCount);

/// <summary>
/// 保存する地図画像の条件の上限と下限
/// </summary>
internal static class MapImageExportLimits
{
    internal const int MinPixelSize = 64;
    internal const int MaxPixelSize = 4096;
    internal const double MinLogicalZoom = 0;
    internal const double MaxLogicalZoom = 22;
    internal const double MinPixelDensity = 1;

    /// <summary>Windows の表示倍率の上限（500%）</summary>
    internal const double MaxPixelDensity = 5;

    /// <summary>
    /// 1 回の描画で使うタイルの上限。公開タイルサーバーの大量取得を避ける（プレビューは条件を変えるたびに描き直す）
    /// </summary>
    internal const int MaxTileCount = 256;

    /// <summary>
    /// タイルの枚数以外の条件を検査する
    /// </summary>
    internal static MapImageExportProblem Validate(MapImageExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Web メルカトルの世界の端（赤道の半周）
        const double worldHalfExtent = 20037508.342789244;
        if (!(Math.Abs(request.CenterX) <= worldHalfExtent && Math.Abs(request.CenterY) <= worldHalfExtent))
        {
            return MapImageExportProblem.CenterOutOfRange;
        }

        if (request.PixelWidth is < MinPixelSize or > MaxPixelSize
            || request.PixelHeight is < MinPixelSize or > MaxPixelSize)
        {
            return MapImageExportProblem.SizeOutOfRange;
        }

        if (!(request.LogicalZoom is >= MinLogicalZoom and <= MaxLogicalZoom))
        {
            return MapImageExportProblem.ZoomOutOfRange;
        }

        return request.PixelDensity is >= MinPixelDensity and <= MaxPixelDensity
            ? MapImageExportProblem.None
            : MapImageExportProblem.PixelDensityOutOfRange;
    }
}

/// <summary>
/// 条件を指定して地図画像を作る。表示中の地図と背景には触らない。
/// </summary>
internal interface IMapImageExporter : IDisposable
{
    /// <summary>
    /// 通信せずに、条件の可否と使うタイルの枚数を調べる
    /// </summary>
    MapImageExportPlan Plan(MapImageExportRequest request);

    /// <summary>
    /// 地図画像を PNG に描く。取得できないタイルがある場合は、欠けた画像を返さずに失敗する
    /// （配信元の提供範囲外は空白になる）。
    /// </summary>
    /// <returns>PNG のストリーム（先頭へ巻き戻し済み）</returns>
    /// <exception cref="InvalidOperationException">条件が作れないものか、描画に失敗した場合</exception>
    Task<Stream> RenderPngAsync(MapImageExportRequest request, CancellationToken cancellationToken);
}
