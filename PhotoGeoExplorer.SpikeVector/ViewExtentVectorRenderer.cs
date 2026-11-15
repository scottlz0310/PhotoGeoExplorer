using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Tiles.Vector;
using SkiaSharp;

namespace PhotoMapStudio.Spike;

/// <summary>
/// 表示範囲全体を 1 枚の画像として描く。photo-map-studio の VectorMapRenderer を元に、
/// ズームを連続値（fractional）で受けられるようにした（#293 の「連続倍率の入口」の実証）。
/// 取得ズームは整数（tileZoom）で、表示倍率は連続（scaleRatio = 2^(zoom - tileZoom)）。
/// </summary>
public sealed class ViewExtentVectorRenderer
{
    private readonly IVectorTileSource _source;
    private readonly IVectorMapStyle _style;

    public ViewExtentVectorRenderer(IVectorTileSource source, IVectorMapStyle style)
    {
        _source = source;
        _style = style;
    }

    /// <summary>世界ピクセル座標（zoom の倍率）。</summary>
    public static (double X, double Y) ToWorldPixel(double latitude, double longitude, double zoom)
    {
        var n = Math.Pow(2.0, zoom) * WebMercator.TileSize;
        var x = (longitude + 180.0) / 360.0 * n;
        var latRad = latitude * Math.PI / 180.0;
        var y = (1.0 - (Math.Asinh(Math.Tan(latRad)) / Math.PI)) / 2.0 * n;
        return (x, y);
    }

    /// <summary>
    /// 中心・連続ズーム・画像寸法（ピクセル）で描いて返す。
    /// overlay は、画像座標（左上原点）で描き足す関数。位置の整合の確認用に、緯度経度の目印を同じ変換で描くために使う。
    /// </summary>
    public async Task<SKBitmap> RenderAsync(
        double centerLatitude,
        double centerLongitude,
        double zoom,
        int width,
        int height,
        Action<SKCanvas, Func<double, double, SKPoint>>? overlay = null,
        CancellationToken cancellationToken = default)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        await _source.PrepareAsync(null, cancellationToken).ConfigureAwait(false);
        var tileZoom = Math.Min((int)Math.Floor(zoom), _source.MaxZoom);
        var scaleRatio = Math.Pow(2.0, zoom - tileZoom);
        var maxTiles = 1 << tileZoom;
        var styleZoom = (int)Math.Floor(zoom);

        var (cx, cy) = ToWorldPixel(centerLatitude, centerLongitude, zoom);
        var left = cx - (width / 2.0);
        var top = cy - (height / 2.0);
        var minTx = (int)Math.Floor(left / scaleRatio / WebMercator.TileSize);
        var minTy = (int)Math.Floor(top / scaleRatio / WebMercator.TileSize);
        var maxTx = (int)Math.Floor((left + width) / scaleRatio / WebMercator.TileSize);
        var maxTy = (int)Math.Floor((top + height) / scaleRatio / WebMercator.TileSize);

        var loaded = new List<(VectorTile Tile, float Scale, float Dx, float Dy)>();
        for (var ty = minTy; ty <= maxTy; ty++)
        {
            if (ty < 0 || ty >= maxTiles)
            {
                continue;
            }

            for (var tx = minTx; tx <= maxTx; tx++)
            {
                var fetchX = ((tx % maxTiles) + maxTiles) % maxTiles;
                var data = await _source.GetTileAsync(tileZoom, fetchX, ty, null, cancellationToken).ConfigureAwait(false);
                if (data is null || data.Length == 0 || !VectorTileDecoder.TryDecode(data, out var tile))
                {
                    continue;
                }

                uint extent = tile.Layers.Count > 0 ? tile.Layers[0].Extent : 4096;
                var scale = (float)(WebMercator.TileSize * scaleRatio / extent);
                var dx = (float)((tx * WebMercator.TileSize * scaleRatio) - left);
                var dy = (float)((ty * WebMercator.TileSize * scaleRatio) - top);
                loaded.Add((tile, scale, dx, dy));
            }
        }

        canvas.Clear(_style.BackgroundColor);
        var viewport = new SKRect(0, 0, width, height);
        var grid = new LabelCollisionGrid();

        foreach (var item in loaded)
        {
            canvas.Save();
            canvas.Translate(item.Dx, item.Dy);
            canvas.Scale(item.Scale, item.Scale);
            _style.RenderPolygons(canvas, item.Tile, item.Scale, styleZoom);
            canvas.Restore();
        }

        foreach (var item in loaded)
        {
            canvas.Save();
            canvas.Translate(item.Dx, item.Dy);
            canvas.Scale(item.Scale, item.Scale);
            _style.RenderLines(canvas, item.Tile, item.Scale, styleZoom);
            canvas.Restore();
        }

        foreach (var item in loaded)
        {
            var matrix = SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy);
            _style.RenderSymbols(canvas, item.Tile, matrix, styleZoom, grid, viewport);
        }

        foreach (var item in loaded)
        {
            var matrix = SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy);
            _style.RenderLineLabels(canvas, item.Tile, matrix, styleZoom, grid, viewport);
        }

        foreach (var item in loaded)
        {
            var matrix = SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy);
            _style.RenderLabels(canvas, item.Tile, matrix, styleZoom, grid, viewport);
        }

        overlay?.Invoke(canvas, (lat, lon) =>
        {
            var (x, y) = ToWorldPixel(lat, lon, zoom);
            return new SKPoint((float)(x - left), (float)(y - top));
        });

        return bitmap;
    }
}
