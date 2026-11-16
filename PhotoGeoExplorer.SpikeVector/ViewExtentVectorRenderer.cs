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
    private readonly ViewRenderOptions _options;

    public ViewExtentVectorRenderer(IVectorTileSource source, IVectorMapStyle style, DecodedTileCache? cache = null, ViewRenderOptions? options = null)
    {
        _source = source;
        _style = style;
        _cache = cache;
        _options = options ?? new ViewRenderOptions();
    }

    /// <summary>描画要求から 1 枚描く（P2）。画像の参照は 1 つ持って返す。</summary>
    public async Task<RenderOutput> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default)
    {
        var center = Mapsui.Projections.SphericalMercator.ToLonLat(new MPoint(request.CenterX, request.CenterY));
        var timings = new TimingsAccumulator();
        using var bitmap = await RenderCoreAsync(center.Y, center.X, request.Zoom, request.ImageWidth, request.ImageHeight, null, timings, cancellationToken).ConfigureAwait(false);
        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);
        if (Environment.GetEnvironmentVariable("PGE_P3_PREWARM") == "1")
        {
            // 実験: 背景スレッドで画像を 1 回描いておく（UI スレッドの初回描画のコストが、画像ごとの準備なら、ここへ移る）
            using var tiny = new SKBitmap(new SKImageInfo(64, 64, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
            using var tinyCanvas = new SKCanvas(tiny);
            tinyCanvas.DrawImage(image, SKRect.Create(0, 0, 64, 64), SKRect.Create(0, 0, 64, 64), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }

        return new RenderOutput(new RefCountedImage(image), timings.ToTimings());
    }

    /// <summary>
    /// 操作が止まっている間に、中心付近のタイル（取得ズームで、中心を囲む 2×2 枚）を、デコード済みキャッシュへ入れておく。
    /// ズームアウトで、これまで見ていない隣のタイルが必要になったとき、描画の中でデコードする（密集市街地で 1 枚 30 ms 以上）のを避ける。
    /// </summary>
    public async Task PrefetchAsync(double centerLatitude, double centerLongitude, double zoom, CancellationToken cancellationToken = default)
    {
        await _source.PrepareAsync(null, cancellationToken).ConfigureAwait(false);
        var tileZoom = Math.Min((int)Math.Floor(zoom), _source.MaxZoom);
        var maxTiles = 1 << tileZoom;
        var (px, py) = ToWorldPixel(centerLatitude, centerLongitude, tileZoom);
        var tx = px / WebMercator.TileSize;
        var ty = py / WebMercator.TileSize;
        var timings = new TimingsAccumulator();
        foreach (var y in new[] { (int)Math.Floor(ty - 0.5), (int)Math.Floor(ty + 0.5) }.Distinct())
        {
            foreach (var x in new[] { (int)Math.Floor(tx - 0.5), (int)Math.Floor(tx + 0.5) }.Distinct())
            {
                if (y < 0 || y >= maxTiles)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await GetTileAsync(tileZoom, ((x % maxTiles) + maxTiles) % maxTiles, y, timings, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class TimingsAccumulator
    {
        public TimeSpan Fetch;
        public TimeSpan Decode;
        public TimeSpan Draw;
        public int Tiles;
        public int Hits;
        public TimeSpan Polygons;
        public TimeSpan Lines;
        public TimeSpan Symbols;
        public TimeSpan LineLabels;
        public TimeSpan Labels;

        public RenderTimings ToTimings() => new(Fetch, Decode, Draw, Tiles, Hits, Polygons, Lines, Symbols, LineLabels, Labels);
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
        var bitmap = new SKBitmap(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
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

        var coordinates = new List<(int Tx, int Ty, int FetchX)>();
        for (var ty = minTy; ty <= maxTy; ty++)
        {
            if (ty < 0 || ty >= maxTiles)
            {
                continue;
            }

            for (var tx = minTx; tx <= maxTx; tx++)
            {
                coordinates.Add((tx, ty, ((tx % maxTiles) + maxTiles) % maxTiles));
            }
        }

        // 取得・デコードは、タイルごとに独立。ParallelDecode のときは、全タイルを同時に始める（キャッシュにないタイルのデコードが、描画の前に直列で並ぶのを避ける）
        VectorTile?[] tiles;
        if (_options.ParallelDecode && coordinates.Count > 1)
        {
            tiles = await Task.WhenAll(coordinates.Select(c => Task.Run(() => GetTileAsync(tileZoom, c.FetchX, c.Ty, timings, cancellationToken), cancellationToken))).ConfigureAwait(false);
        }
        else
        {
            tiles = new VectorTile?[coordinates.Count];
            for (var i = 0; i < coordinates.Count; i++)
            {
                tiles[i] = await GetTileAsync(tileZoom, coordinates[i].FetchX, coordinates[i].Ty, timings, cancellationToken).ConfigureAwait(false);
            }
        }

        var loaded = new List<(VectorTile Tile, float Scale, float Dx, float Dy)>();
        for (var i = 0; i < coordinates.Count; i++)
        {
            var tile = tiles[i];
            if (tile is null)
            {
                continue;
            }

            uint extent = tile.Layers.Count > 0 ? tile.Layers[0].Extent : 4096;
            var scale = (float)(WebMercator.TileSize * scaleRatio / extent);
            var dx = (float)((coordinates[i].Tx * WebMercator.TileSize * scaleRatio) - left);
            var dy = (float)((coordinates[i].Ty * WebMercator.TileSize * scaleRatio) - top);
            loaded.Add((tile, scale, dx, dy));
        }
        var drawStart = Stopwatch.StartNew();
        if (_options.Bands > 1 || _options.ParallelLabels)
        {
            await DrawParallelAsync(bitmap, canvas, loaded, styleZoom, width, height, cancellationToken).ConfigureAwait(false);
            timings.Draw += drawStart.Elapsed;
            overlay?.Invoke(canvas, (lat, lon) =>
            {
                var (x, y) = ToWorldPixel(lat, lon, zoom);
                return new SKPoint((float)(x - left), (float)(y - top));
            });
            return bitmap;
        }

        canvas.Clear(_style.BackgroundColor);
        var viewport = new SKRect(0, 0, width, height);
        var grid = new LabelCollisionGrid();

        var phase = Stopwatch.StartNew();
        foreach (var item in loaded)
        {
            canvas.Save();
            canvas.Translate(item.Dx, item.Dy);
            canvas.Scale(item.Scale, item.Scale);
            _style.RenderPolygons(canvas, item.Tile, item.Scale, styleZoom);
            canvas.Restore();
        }

        timings.Polygons += phase.Elapsed;
        phase.Restart();

        foreach (var item in loaded)
        {
            canvas.Save();
            canvas.Translate(item.Dx, item.Dy);
            canvas.Scale(item.Scale, item.Scale);
            _style.RenderLines(canvas, item.Tile, item.Scale, styleZoom);
            canvas.Restore();
        }

        timings.Lines += phase.Elapsed;
        phase.Restart();

        foreach (var item in loaded)
        {
            var matrix = SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy);
            _style.RenderSymbols(canvas, item.Tile, matrix, styleZoom, grid, viewport);
        }

        timings.Symbols += phase.Elapsed;
        phase.Restart();

        foreach (var item in loaded)
        {
            var matrix = SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy);
            _style.RenderLineLabels(canvas, item.Tile, matrix, styleZoom, grid, viewport);
        }

        timings.LineLabels += phase.Elapsed;
        phase.Restart();

        foreach (var item in loaded)
        {
            var matrix = SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy);
            _style.RenderLabels(canvas, item.Tile, matrix, styleZoom, grid, viewport);
        }

        timings.Labels += phase.Elapsed;

        timings.Draw += drawStart.Elapsed;
        overlay?.Invoke(canvas, (lat, lon) =>
        {
            var (x, y) = ToWorldPixel(lat, lon, zoom);
            return new SKPoint((float)(x - left), (float)(y - top));
        });

        return bitmap;
    }

    /// <summary>
    /// 面・線を、画像を横に分けた帯ごとに並列で描く（帯は連続した行なので、同じ画素メモリの別範囲を別のサーフェスで描ける）。
    /// 注記は、衝突判定が逐次のため 1 スレッドで、別の透明な画像に描いて最後に重ねる（<see cref="ViewRenderOptions.ParallelLabels"/>）か、帯の描画のあとに本体へ直接描く。
    /// </summary>
    private async Task DrawParallelAsync(SKBitmap bitmap, SKCanvas canvas, List<(VectorTile Tile, float Scale, float Dx, float Dy)> loaded, int styleZoom, int width, int height, CancellationToken cancellationToken)
    {
        var viewport = new SKRect(0, 0, width, height);
        var grid = new LabelCollisionGrid();

        void DrawLabels(SKCanvas target)
        {
            foreach (var item in loaded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _style.RenderSymbols(target, item.Tile, SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy), styleZoom, grid, viewport);
            }

            foreach (var item in loaded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _style.RenderLineLabels(target, item.Tile, SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy), styleZoom, grid, viewport);
            }

            foreach (var item in loaded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _style.RenderLabels(target, item.Tile, SKMatrix.CreateScaleTranslation(item.Scale, item.Scale, item.Dx, item.Dy), styleZoom, grid, viewport);
            }
        }

        SKBitmap? labelBitmap = null;
        Task? labelTask = null;
        if (_options.ParallelLabels)
        {
            labelBitmap = new SKBitmap(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            labelTask = Task.Run(() =>
            {
                using var labelCanvas = new SKCanvas(labelBitmap);
                labelCanvas.Clear(SKColors.Transparent);
                DrawLabels(labelCanvas);
            });
        }

        var bands = Math.Max(1, _options.Bands);
        var pixels = bitmap.GetPixels();
        var rowBytes = bitmap.RowBytes;
        var background = _style.BackgroundColor;
        try
        {
            Parallel.For(0, bands, new ParallelOptions { CancellationToken = cancellationToken }, band =>
            {
                var y0 = height * band / bands;
                var y1 = height * (band + 1) / bands;
                if (y1 <= y0)
                {
                    return;
                }

                var info = new SKImageInfo(width, y1 - y0, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info, pixels + (y0 * rowBytes), rowBytes);
                var bandCanvas = surface.Canvas;
                bandCanvas.Clear(background);
                bandCanvas.Translate(0, -y0);
                foreach (var item in loaded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bandCanvas.Save();
                    bandCanvas.Translate(item.Dx, item.Dy);
                    bandCanvas.Scale(item.Scale, item.Scale);
                    _style.RenderPolygons(bandCanvas, item.Tile, item.Scale, styleZoom);
                    bandCanvas.Restore();
                }

                foreach (var item in loaded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bandCanvas.Save();
                    bandCanvas.Translate(item.Dx, item.Dy);
                    bandCanvas.Scale(item.Scale, item.Scale);
                    _style.RenderLines(bandCanvas, item.Tile, item.Scale, styleZoom);
                    bandCanvas.Restore();
                }
            });
        }
        catch
        {
            // 中断・失敗のときも、別スレッドの注記の描画が終わるまで待ち、画像を解放する（描画中の画像を解放しない）
            if (labelTask is not null)
            {
                try
                {
                    await labelTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 元の例外を優先する
                }

                labelBitmap!.Dispose();
            }

            throw;
        }

        if (labelTask is null)
        {
            DrawLabels(canvas);
            return;
        }

        await labelTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        canvas.DrawBitmap(labelBitmap!, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        labelBitmap!.Dispose();
    }

    private async Task<VectorTile?> GetTileAsync(int tileZoom, int x, int y, TimingsAccumulator timings, CancellationToken cancellationToken)
    {
        lock (timings)
        {
            timings.Tiles++;
        }

        var key = new TileKey(_source.SourceId, tileZoom, x, y);
        if (_cache is not null && _cache.TryGet(key, out var cached))
        {
            lock (timings)
            {
                timings.Hits++;
            }

            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        var data = await _source.GetTileAsync(tileZoom, x, y, null, cancellationToken).ConfigureAwait(false);
        lock (timings)
        {
            timings.Fetch += stopwatch.Elapsed;
        }

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
        lock (timings)
        {
            timings.Decode += stopwatch.Elapsed;
        }

        if (!ok)
        {
            _cache?.Add(key, null, 0);
            return null;
        }

        _cache?.Add(key, tile, allocated);
        return tile;
    }
}

/// <summary>手順 1（#293 P3a）の実験用の描画オプション。既定は、従来どおり 1 スレッドで描く。</summary>
public sealed record ViewRenderOptions
{
    /// <summary>面・線を、画像を横に分けた帯ごとに並列で描く（1 で無効）。</summary>
    public int Bands { get; init; } = 1;

    /// <summary>注記を、別スレッドで透明な画像へ描いて、最後に重ねる。</summary>
    public bool ParallelLabels { get; init; }

    /// <summary>タイルの取得・デコードを、全タイル同時に行う（既定は 1 枚ずつ）。</summary>
    public bool ParallelDecode { get; init; }
}