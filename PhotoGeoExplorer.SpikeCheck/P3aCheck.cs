using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Spike;
using SkiaSharp;

// スパイク #293 の手順 1（P3a）: 確定描画（鮮明な画像の描画時間）を縮める案を、同じ地点・ズーム・画像寸法で比べる（ヘッドレス）。
// 各案の描画時間（デコード済みキャッシュあり）と、基準（現行の描き方）との画像の差を測る。
internal static class P3aCheck
{
    private const double Width = 683;
    private const double Height = 610;
    private const int Warmup = 2;

    private static readonly (string Name, double Lat, double Lon)[] Locations =
    {
        ("dense-odori", 43.0606, 141.3469),
        ("suburb-okadama", 43.1180, 141.3900),
        ("tile-boundary-z15", BoundaryLat(), BoundaryLon()),
    };

    private static readonly double[] Zooms = { 15, 16, 17, 18, 19 };

    private static readonly FastStyleOptions Fast = new() { Prep = true, Cull = true, ClipVertices = true };

    private static readonly (string Name, FastStyleOptions? Style, ViewRenderOptions Render)[] Variants =
    {
        ("base", null, new ViewRenderOptions()),
        ("pcv", Fast, new ViewRenderOptions()),
        ("pcv+pl", Fast, new ViewRenderOptions { ParallelLabels = true }),
        ("pcv+b8+pl", Fast, new ViewRenderOptions { Bands = 8, ParallelLabels = true }),
        ("cv+b8+pl", new FastStyleOptions { Cull = true, ClipVertices = true }, new ViewRenderOptions { Bands = 8, ParallelLabels = true }),
        ("thin+b8+pl", Fast with { MinPolygonPx = 4f, MinRoadOrder = 40, HidePathsBelowZoom = 16 }, new ViewRenderOptions { Bands = 8, ParallelLabels = true }),
    };
    public static async Task<int> RunAsync(string mvtRoot, string outDir, double density, int reps, bool saveImages)
    {
        Directory.CreateDirectory(outDir);
        var width = (int)Math.Round(Width * density);
        var height = (int)Math.Round(Height * density);
        var source = new SavedMvtTileSource(mvtRoot);
        var rows = new List<Dictionary<string, object?>>();
        var table = new Dictionary<(string Scenario, string Variant), (double Median, double P95, Diff? Diff)>();

        foreach (var (location, lat, lon) in Locations)
        {
            foreach (var zoom in Zooms)
            {
                var scenario = $"{location} z{zoom:0}";
                var styles = Variants.Select(v => v.Style is null ? null : new FastVectorStyle(v.Style)).ToArray();
                var renderers = Variants.Select((v, i) => new ViewExtentVectorRenderer(source, styles[i] is null ? new OpenMapTilesStyle() : styles[i]!, new DecodedTileCache(256L * 1024 * 1024), v.Render)).ToArray();
                var times = Variants.Select(_ => new List<double>()).ToArray();
                var firsts = new SKBitmap?[Variants.Length];

                // 変種ごとに連続したブロックで測り、順方向・逆方向の 2 周する（環境のゆらぎが特定の変種に偏らないように。強制 GC はブロックの前に 1 回だけ）。
                // 最初のブロックの前に、デコード・JIT のための描画を Warmup 回行う（記録しない）
                var block = Math.Max(1, reps / 2);
                for (var pass = 0; pass < 2; pass++)
                {
                    for (var k = 0; k < Variants.Length; k++)
                    {
                        var v = pass == 0 ? k : Variants.Length - 1 - k;
                        GC.Collect(2, GCCollectionMode.Forced, true);
                        var runs = block + (pass == 0 ? Warmup : 0);
                        for (var i = 0; i < runs; i++)
                        {
                            var sw = Stopwatch.StartNew();
                            var bitmap = await renderers[v].RenderAsync(lat, lon, zoom, width, height).ConfigureAwait(false);
                            var ms = sw.Elapsed.TotalMilliseconds;
                            if (pass == 0 && i == 0)
                            {
                                firsts[v] = bitmap;
                            }
                            else
                            {
                                bitmap.Dispose();
                            }

                            if (!(pass == 0 && i < Warmup))
                            {
                                times[v].Add(ms);
                            }
                        }
                    }
                }
                for (var v = 0; v < Variants.Length; v++)
                {
                    var name = Variants[v].Name;
                    times[v].Sort();
                    var median = Median(times[v]);
                    var p95 = times[v][Math.Min(times[v].Count - 1, (int)Math.Ceiling(times[v].Count * 0.95) - 1)];
                    var diff = v == 0 ? null : Compare(firsts[0]!, firsts[v]!);
                    if (saveImages && scenario is "dense-odori z15" or "dense-odori z17" or "suburb-okadama z17" or "tile-boundary-z15 z15")
                    {
                        var dir = Path.Combine(outDir, "img");
                        Directory.CreateDirectory(dir);
                        using var image = SKImage.FromBitmap(firsts[v]!);
                        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                        File.WriteAllBytes(Path.Combine(dir, $"{name}-{scenario.Replace(' ', '_')}-d{density:0.00}.png"), data.ToArray());
                    }

                    table[(scenario, name)] = (median, times[v][0], diff);
                    rows.Add(new Dictionary<string, object?>
                    {
                        ["scenario"] = scenario,
                        ["variant"] = name,
                        ["density"] = density,
                        ["medianMs"] = Math.Round(median, 1),
                        ["p95Ms"] = Math.Round(p95, 1),
                        ["minMs"] = Math.Round(times[v][0], 1),
                        ["meanAbsDiff"] = diff is null ? null : Math.Round(diff.MeanAbs, 3),
                        ["pctPixelsDiffGt8"] = diff is null ? null : Math.Round(diff.PctGt8, 3),
                        ["maxDiff"] = diff?.Max,
                    });
                }

                foreach (var bitmap in firsts)
                {
                    bitmap?.Dispose();
                }

                foreach (var style in styles)
                {
                    style?.Dispose();
                }
            }
        }

        var report = new
        {
            density,
            width,
            height,
            reps,
            skiaSharp = typeof(SKCanvas).Assembly.GetName().Version?.ToString(),
            runtime = Environment.Version.ToString(),
            processors = Environment.ProcessorCount,
            rows,
        };
        await File.WriteAllTextAsync(Path.Combine(outDir, $"p3a-d{density.ToString("0.00", CultureInfo.InvariantCulture)}.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        Console.WriteLine(Format(table, density, width, height, reps));
        return 0;
    }
    /// <summary>フェーズ別（面・線・シンボル・線ラベル・ラベル）の描画時間。非並列の変種だけ（並列はフェーズが重なるため）。</summary>
    public static async Task<int> RunPhasesAsync(string mvtRoot, double density, int reps)
    {
        var source = new SavedMvtTileSource(mvtRoot);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## フェーズ別（density {density:0.00}、各 {reps} 回の中央値 ms）: 面 / 線 / シンボル / 線ラベル / ラベル / 描画合計");
        sb.AppendLine("| scenario | variant | polygons | lines | symbols | lineLabels | labels | draw |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var (location, lat, lon) in Locations)
        {
            foreach (var zoom in new[] { 15.0, 17.0, 19.0 })
            {
                foreach (var (name, style, render) in Variants.Where(v => v.Render.Bands <= 1 && !v.Render.ParallelLabels))
                {
                    using var fast = style is null ? null : new FastVectorStyle(style);
                    IVectorMapStyle used = fast is null ? new OpenMapTilesStyle() : fast;
                    var renderer = new ViewExtentVectorRenderer(source, used, new DecodedTileCache(256L * 1024 * 1024), render);
                    var center = Mapsui.Projections.SphericalMercator.FromLonLat(new Mapsui.MPoint(lon, lat));
                    var request = new RenderRequest(center.X, center.Y, ViewExtentInjection.ResolutionFromZoom(zoom, density), Width, Height, density, "saved", 1, 1);
                    var runs = new List<RenderTimings>();
                    for (var i = 0; i < Warmup + reps; i++)
                    {
                        var output = await renderer.RenderAsync(request).ConfigureAwait(false);
                        if (i >= Warmup)
                        {
                            runs.Add(output.Timings);
                        }

                        output.Image.Release();
                    }

                    double M(Func<RenderTimings, TimeSpan> f) => Median(runs.Select(r => f(r).TotalMilliseconds).OrderBy(x => x).ToList());
                    sb.AppendLine(CultureInfo.InvariantCulture, $"| {location} z{zoom:0} | {name} | {M(r => r.Polygons):0.0} | {M(r => r.Lines):0.0} | {M(r => r.Symbols):0.0} | {M(r => r.LineLabels):0.0} | {M(r => r.Labels):0.0} | {M(r => r.Draw):0.0} |");
                }
            }
        }

        Console.WriteLine(sb.ToString());
        return 0;
    }

    /// <summary>線のステップ別の時間（非並列・pc 相当）。深いズームで線が支配的な理由を探る。</summary>
    public static async Task<int> RunProfileAsync(string mvtRoot, double density, int reps)
    {
        var source = new SavedMvtTileSource(mvtRoot);
        foreach (var (location, lat, lon) in Locations)
        {
            foreach (var zoom in new[] { 15.0, 17.0, 19.0 })
            {
                using var style = new FastVectorStyle(new FastStyleOptions { Prep = true, Cull = true, ClipVertices = Environment.GetEnvironmentVariable("P3A_CLIP") == "1" });
                var renderer = new ViewExtentVectorRenderer(source, style, new DecodedTileCache(256L * 1024 * 1024));
                var width = (int)Math.Round(Width * density);
                var height = (int)Math.Round(Height * density);
                for (var i = 0; i < Warmup; i++)
                {
                    (await renderer.RenderAsync(lat, lon, zoom, width, height).ConfigureAwait(false)).Dispose();
                }

                FastVectorStyle.Profile.Clear();
                FastVectorStyle.ProfileEnabled = true;
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < reps; i++)
                {
                    (await renderer.RenderAsync(lat, lon, zoom, width, height).ConfigureAwait(false)).Dispose();
                }

                var total = sw.Elapsed.TotalMilliseconds / reps;
                FastVectorStyle.ProfileEnabled = false;
                var parts = FastVectorStyle.Profile.OrderBy(kv => kv.Key).Select(kv => kv.Key.StartsWith("roads.c", StringComparison.Ordinal) && kv.Key is "roads.count" or "roads.points" ? $"{kv.Key}={kv.Value / reps}" : $"{kv.Key}={kv.Value * 1000.0 / Stopwatch.Frequency / reps:0.0}");
                Console.WriteLine($"{location} z{zoom:0} total={total:0.0}ms  " + string.Join("  ", parts));
            }
        }

        return 0;
    }

    /// <summary>パスの事前生成（Prep）によるメモリの増加。3 地点の z15・z17 を描いたあとの、プロセスの専有メモリの差。</summary>
    public static async Task<int> RunMemoryAsync(string mvtRoot, string only)
    {
        var source = new SavedMvtTileSource(mvtRoot);
        foreach (var (name, options) in new (string, FastStyleOptions?)[] { ("base", null), ("pcv", new FastStyleOptions { Prep = true, Cull = true, ClipVertices = true }) }.Where(v => v.Item1 == only))
        {
            (await new ViewExtentVectorRenderer(source, new OpenMapTilesStyle(), null).RenderAsync(43.0606, 141.3469, 17, 100, 100).ConfigureAwait(false)).Dispose();
            GC.Collect(2, GCCollectionMode.Forced, true);
            var before = Process.GetCurrentProcess().PrivateMemorySize64;
            var cache = new DecodedTileCache(256L * 1024 * 1024);
            using var fast = options is null ? null : new FastVectorStyle(options);
            IVectorMapStyle style = fast is null ? new OpenMapTilesStyle() : fast;
            var renderer = new ViewExtentVectorRenderer(source, style, cache);
            foreach (var (_, lat, lon) in Locations)
            {
                foreach (var zoom in new[] { 15.0, 17.0 })
                {
                    (await renderer.RenderAsync(lat, lon, zoom, 683, 610).ConfigureAwait(false)).Dispose();
                }
            }

            GC.Collect(2, GCCollectionMode.Forced, true);
            var after = Process.GetCurrentProcess().PrivateMemorySize64;
            Console.WriteLine($"{name}: 専有メモリ +{(after - before) / 1048576.0:0.0} MB、デコード済みキャッシュ {cache.Bytes / 1048576.0:0.0} MB（見積もり）、タイル {cache.Count}");
        }

        return 0;
    }

    /// <summary>UI スレッドの 1 フレームの描画コストの切り分け: 画像の色形式（RGBA／BGRA）と、配置（整数／小数のずれ／拡大）と、補間による違い。</summary>
    public static Task<int> RunDrawBenchAsync(double density)
    {
        var w = (int)Math.Round(Width * density);
        var h = (int)Math.Round(Height * density);
        var target = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(target);
        var rng = new Random(1);
        Console.WriteLine($"キャンバス {w}x{h} Bgra8888。1 回の DrawImage の中央値（ms）");
        foreach (var colorType in new[] { SKColorType.Rgba8888, SKColorType.Bgra8888 })
        {
            using var src = new SKBitmap(new SKImageInfo(w, h, colorType, SKAlphaType.Premul));
            var px = src.GetPixelSpan();
            for (var i = 0; i < px.Length; i += 4)
            {
                px[i] = (byte)rng.Next(256);
                px[i + 1] = (byte)rng.Next(256);
                px[i + 2] = (byte)rng.Next(256);
                px[i + 3] = 255;
            }

            src.SetImmutable();
            using var image = SKImage.FromBitmap(src);
            foreach (var (label, dx, dy, scale, sampling) in new (string, float, float, float, SKSamplingOptions)[]
            {
                ("整数配置・Linear", 0, 0, 1f, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)),
                ("整数配置・Nearest", 0, 0, 1f, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)),
                ("小数ずれ(0.37px)・Linear", 0.37f, 0.61f, 1f, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)),
                ("小数ずれ(0.37px)・Nearest", 0.37f, 0.61f, 1f, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)),
                ("拡大 1.3x・Linear", 0f, 0f, 1.3f, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)),
                ("縮小 0.7x・Linear+Mip", 0f, 0f, 0.7f, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)),
            })
            {
                var times = new List<double>();
                for (var i = 0; i < 40; i++)
                {
                    var sw = Stopwatch.StartNew();
                    canvas.DrawImage(image, new SKRect(dx, dy, dx + (w * scale), dy + (h * scale)), sampling);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }

                times.RemoveRange(0, 5);
                times.Sort();
                Console.WriteLine($"  {colorType,-10} {label,-28} {Median(times):0.00} ms");
            }
        }

        return Task.FromResult(0);
    }

    /// <summary>デコード済みキャッシュが空の状態（ジャンプ直後）の確定描画。タイルの取得・デコードを直列／並列で比べる。</summary>
    public static async Task<int> RunColdAsync(string mvtRoot, double density, int reps)
    {
        var source = new SavedMvtTileSource(mvtRoot);
        var width = (int)Math.Round(Width * density);
        var height = (int)Math.Round(Height * density);
        var fast = new FastStyleOptions { Prep = true, Cull = true, ClipVertices = true };
        var noPrep = new FastStyleOptions { Cull = true, ClipVertices = true };
        var variants = new (string Name, FastStyleOptions Style, ViewRenderOptions Render)[]
        {
            ("prep+serial", fast, new ViewRenderOptions { Bands = 8, ParallelLabels = true }),
            ("noprep+serial", noPrep, new ViewRenderOptions { Bands = 8, ParallelLabels = true }),
            ("noprep+pdecode", noPrep, new ViewRenderOptions { Bands = 8, ParallelLabels = true, ParallelDecode = true }),
        };
        Console.WriteLine($"## キャッシュ空（ジャンプ直後）の確定描画 ms（density {density:0.00}、{reps} 回の中央値（最小））。デコードの Prep（パス生成）も毎回やり直し");
        foreach (var (location, lat, lon) in Locations)
        {
            foreach (var zoom in new[] { 15.0, 17.0, 19.0 })
            {
                var line = $"{location} z{zoom:0}: ";
                foreach (var (name, styleOptions, render) in variants)
                {
                    using var style = new FastVectorStyle(styleOptions);
                    var times = new List<double>();
                    for (var i = 0; i < reps + 2; i++)
                    {
                        // 毎回、新しいキャッシュ（デコード済みモデルも新規。パスの事前生成はモデルに紐づくので、やり直しになる）
                        var renderer = new ViewExtentVectorRenderer(source, style, new DecodedTileCache(256L * 1024 * 1024), render);
                        var sw = Stopwatch.StartNew();
                        (await renderer.RenderAsync(lat, lon, zoom, width, height).ConfigureAwait(false)).Dispose();
                        if (i >= 2)
                        {
                            times.Add(sw.Elapsed.TotalMilliseconds);
                        }
                    }

                    times.Sort();
                    line += $"{name} {Median(times):0.0} ({times[0]:0.0})  ";
                }

                Console.WriteLine(line);
            }
        }

        return 0;
    }

    private static string Format(Dictionary<(string Scenario, string Variant), (double Median, double P95, Diff? Diff)> table, double density, int width, int height, int reps)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## density {density:0.00}（{width}x{height}px、中央値 ms。()内は最小値。{reps} 回）");
        sb.Append("| scenario |");
        foreach (var v in Variants)
        {
            sb.Append(' ').Append(v.Name).Append(" |");
        }

        sb.AppendLine();
        sb.Append("| --- |");
        foreach (var _ in Variants)
        {
            sb.Append(" ---: |");
        }

        sb.AppendLine();
        foreach (var scenario in table.Keys.Select(k => k.Scenario).Distinct())
        {
            sb.Append("| ").Append(scenario).Append(" |");
            foreach (var v in Variants)
            {
                var (median, min, _) = table[(scenario, v.Name)];
                sb.Append(CultureInfo.InvariantCulture, $" {median:0.0} ({min:0.0}) |");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("## 基準（base）との画像の差（平均絶対差 / 差が 8 を超える画素の割合 % / 最大差）");
        sb.Append("| scenario |");
        foreach (var v in Variants.Skip(1))
        {
            sb.Append(' ').Append(v.Name).Append(" |");
        }

        sb.AppendLine();
        sb.Append("| --- |");
        foreach (var _ in Variants.Skip(1))
        {
            sb.Append(" ---: |");
        }

        sb.AppendLine();
        foreach (var scenario in table.Keys.Select(k => k.Scenario).Distinct())
        {
            sb.Append("| ").Append(scenario).Append(" |");
            foreach (var v in Variants.Skip(1))
            {
                var d = table[(scenario, v.Name)].Diff;
                sb.Append(CultureInfo.InvariantCulture, $" {d?.MeanAbs:0.000} / {d?.PctGt8:0.000} / {d?.Max} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static Diff Compare(SKBitmap a, SKBitmap b)
    {
        var pa = a.GetPixelSpan();
        var pb = b.GetPixelSpan();
        double sum = 0;
        long over = 0;
        var max = 0;
        var pixels = pa.Length / 4;
        for (var i = 0; i < pa.Length; i += 4)
        {
            var worst = 0;
            for (var c = 0; c < 3; c++)
            {
                var d = Math.Abs(pa[i + c] - pb[i + c]);
                sum += d;
                worst = Math.Max(worst, d);
            }

            if (worst > 8)
            {
                over++;
            }

            max = Math.Max(max, worst);
        }

        return new Diff(sum / (pixels * 3.0), over * 100.0 / pixels, max);
    }

    private static double Median(List<double> sorted)
    {
        var mid = (sorted.Count - 1) / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid] + sorted[mid + 1]) / 2;
    }

    private static double BoundaryLat()
    {
        var n = Math.Pow(2, 15);
        var tileY = Math.Floor((1 - (Math.Asinh(Math.Tan(43.0606 * Math.PI / 180)) / Math.PI)) / 2 * n) + 1;
        return Math.Atan(Math.Sinh(Math.PI * (1 - (2 * tileY / n)))) * 180 / Math.PI;
    }

    private static double BoundaryLon()
    {
        var n = Math.Pow(2, 15);
        var tileX = Math.Floor((141.3469 + 180) / 360 * n) + 1;
        return (tileX / n * 360) - 180;
    }

    private sealed record Diff(double MeanAbs, double PctGt8, int Max);
}
