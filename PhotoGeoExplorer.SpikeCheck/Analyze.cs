using SkiaSharp;

// 実アプリの画面キャプチャ（live-*.png）から、標識（マゼンタ）と画像内の十字（緑）の相対ずれを測る。
// 期待位置もキャプチャの原点も使わない（相対のみ）ため、DPI による原点の丸めの影響を受けない。
internal static class Analyze
{
    public static int Run(string dir)
    {
        var worstAll = 0.0;
        foreach (var path in Directory.GetFiles(dir, "live-*.png").OrderBy(p => p, StringComparer.Ordinal))
        {
            using var bmp = SKBitmap.Decode(path);
            var blobs = FindMagentaBlobs(bmp);
            double worst = 0;
            var counted = 0;
            foreach (var (mx, my) in blobs)
            {
                var g = GreenCentroid(bmp, mx, my, 24);
                if (g is null)
                {
                    continue;
                }

                counted++;
                worst = Math.Max(worst, Math.Sqrt(Math.Pow(g.Value.X - mx, 2) + Math.Pow(g.Value.Y - my, 2)));
            }

            worstAll = Math.Max(worstAll, worst);
            Console.WriteLine($"{Path.GetFileName(path),-50} {bmp.Width}x{bmp.Height} blobs={blobs.Count} paired={counted} maxRelative={worst:0.000}px");
        }

        Console.WriteLine($"worst relative = {worstAll:0.000}px");
        return 0;
    }

    private static List<(double X, double Y)> FindMagentaBlobs(SKBitmap bmp)
    {
        var seen = new bool[bmp.Width * bmp.Height];
        var result = new List<(double, double)>();
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                if (seen[(y * bmp.Width) + x] || !IsMagenta(bmp.GetPixel(x, y)))
                {
                    continue;
                }

                double sum = 0, sx = 0, sy = 0;
                var stack = new Stack<(int X, int Y)>();
                stack.Push((x, y));
                seen[(y * bmp.Width) + x] = true;
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    var c = bmp.GetPixel(cx, cy);
                    var w = Math.Max(0, Math.Min(c.Red, c.Blue) - c.Green - 12) / 35.0;
                    sum += w;
                    sx += w * (cx + 0.5);
                    sy += w * (cy + 0.5);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var nx = cx + dx;
                        var ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= bmp.Width || ny >= bmp.Height || seen[(ny * bmp.Width) + nx] || !IsMagenta(bmp.GetPixel(nx, ny)))
                        {
                            continue;
                        }

                        seen[(ny * bmp.Width) + nx] = true;
                        stack.Push((nx, ny));
                    }
                }

                if (sum > 12)
                {
                    result.Add((sx / sum, sy / sum));
                }
            }
        }

        return result;
    }

    private static bool IsMagenta(SKColor c) => Math.Min(c.Red, c.Blue) - c.Green > 12;

    /// <summary>
    /// 十字の交点を求める。水平の腕は行ごとの緑の合計が大きい行、垂直の腕は列ごとの合計が大きい列の中心。
    /// 腕の長さを使わないため、画像の端で腕が切れていても交点は偏らない。
    /// </summary>
    private static (double X, double Y)? GreenCentroid(SKBitmap bmp, double cx, double cy, int radius)
    {
        var x0 = Math.Max(0, (int)(cx - radius));
        var x1 = Math.Min(bmp.Width - 1, (int)(cx + radius));
        var y0 = Math.Max(0, (int)(cy - radius));
        var y1 = Math.Min(bmp.Height - 1, (int)(cy + radius));
        var rows = new double[y1 - y0 + 1];
        var cols = new double[x1 - x0 + 1];
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var c = bmp.GetPixel(x, y);
                var w = Math.Max(0, (c.Green - Math.Max(c.Red, c.Blue)) - 12) / 35.0;
                rows[y - y0] += w;
                cols[x - x0] += w;
            }
        }

        double Center(double[] sums, int origin)
        {
            var max = sums.Max();
            if (max < 4)
            {
                return double.NaN;
            }

            double sum = 0, weighted = 0;
            for (var i = 0; i < sums.Length; i++)
            {
                if (sums[i] >= 0.5 * max)
                {
                    sum += sums[i];
                    weighted += sums[i] * (origin + i + 0.5);
                }
            }

            return weighted / sum;
        }

        var ycenter = Center(rows, y0);
        var xcenter = Center(cols, x0);
        return double.IsNaN(xcenter) || double.IsNaN(ycenter) ? null : (xcenter, ycenter);
    }
}
