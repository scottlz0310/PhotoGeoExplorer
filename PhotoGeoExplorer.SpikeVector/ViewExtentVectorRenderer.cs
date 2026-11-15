using System.Diagnostics;
using Mapsui;
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
    private readonly DecodedTileCache? _cache;

    public ViewExtentVectorRenderer(IVectorTileSource source, IVectorMapStyle style, DecodedTileCache? cache = null)
    {
        _source = source;
        _style = style;
        _cache = cache;
    }

    /// <summary>描画要求から 1 枚描く（P2）。画像の参照は 1 つ持って返す。</summary>
    public async Task<RenderOutput> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default)
    {
        var center = Mapsui.Projections.SphericalMercator.ToLonLat(new MPoint(request.CenterX, request.CenterY));
        var timings = new TimingsAccumulator();
        using var bitmap = await RenderCoreAsync(center.Y, center.X, request.Zoom, request.ImageWidth, request.ImageHeight, null, timings, cancellationToken).ConfigureAwait(false);
        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);
        return new RenderOutput(new RefCountedImage(image), timings.ToTimings());
    }

    private sealed class TimingsAccumulator
    {
        public TimeSpan Fetch;
        public TimeSpan Decode;
        public TimeSpan Draw;
        public int Tiles;
        public int Hits;

        public RenderTimings ToTimings() => new(Fetch, Decode, Draw, Tiles, Hits);
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
    public Task<SKBitmap> RenderAsync(
        double centerLatitude,
        double centerLongitude,
        double zoom,
        int width,
        int height,
        Action<SKCanvas, Func<double, double, SKPoint>>? overlay = null,
        CancellationToken cancellationToken = default)
        => RenderCoreAsync(centerLatitude, centerLongitude, zoom, width, height, overlay, new TimingsAccumulator(), cancellationToken);

    private async Task<SKBitmap> RenderCoreAsync(
        double centerLatitude,
        double centerLongitude,
        double zoom,
        int width,
        int height,
        Action<SKCanvas, Func<double, double, SKPoint>>? overlay,
        TimingsAccumulator timings,
        CancellationToken cancellationToken)
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
                var tile = await GetTileAsync(tileZoom, fetchX, ty, timings, cancellationToken).ConfigureAwait(false);
                if (tile is null)
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

        var drawStart = Stopwatch.StartNew();
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

        timings.Draw += drawStart.Elapsed;
        overlay?.Invoke(canvas, (lat, lon) =>
        {
            var (x, y) = ToWorldPixel(lat, lon, zoom);
            return new SKPoint((float)(x - left), (float)(y - top));
        });

        return bitmap;
    }

    private async Task<VectorTile?> GetTileAsync(int tileZoom, int x, int y, TimingsAccumulator timings, CancellationToken cancellationToken)
    {
        timings.Tiles++;
        var key = new TileKey(_source.SourceId, tileZoom, x, y);
        if (_cache is not null && _cache.TryGet(key, out var cached))
        {
            timings.Hits++;
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        var data = await _source.GetTileAsync(tileZoom, x, y, null, cancellationToken).ConfigureAwait(false);
        timings.Fetch += stopwatch.Elapsed;
        if (data is null || data.Length == 0)
        {
            _cache?.Add(key, null, 0);
            return null;
        }

        // デコードは同期処理なので、同じスレッドの割り当て量の差を、モデルの大きさの見積もりにする
        stopwatch.Restart();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var ok = VectorTileDecoder.TryDecode(data, out var tile);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        timings.Decode += stopwatch.Elapsed;
        if (!ok)
        {
            _cache?.Add(key, null, 0);
            return null;
        }

        _cache?.Add(key, tile, allocated);
        return tile;
    }
}
