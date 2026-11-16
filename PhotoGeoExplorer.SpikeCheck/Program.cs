using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Spike;
using SkiaSharp;

// スパイク #293 の P1: 表示範囲画像の位置の整合（パン・ズーム・リサイズ・DPI・複数ピン）を、Mapsui の描画結果の画素で確認する。
// 画像の中には、緯度経度の目印（緑の十字）を、画像の描画と同じ変換で描く。Mapsui 側には、同じ緯度経度に標識（マゼンタの円）を置く。
// 両者の重心と、Mapsui の座標変換が示す位置の差（デバイスピクセル）を測る。

if (args.Length >= 2 && args[0] == "analyze")
{
    return Analyze.Run(args[1]);
}

if (args.Length >= 2 && args[0] == "p3a-profile")
{
    return await P3aCheck.RunProfileAsync(args[1], args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1.0, args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 9);
}

if (args.Length >= 2 && args[0] == "p3a-phases")
{
    var dp = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1.0;
    return await P3aCheck.RunPhasesAsync(args[1], dp, args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 9);
}

if (args.Length >= 3 && args[0] == "p3a")
{
    var d3 = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 1.0;
    var reps = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 11;
    return await P3aCheck.RunAsync(args[1], args[2], d3, reps, args.Length > 5 && args[5] == "images");
}

if (args.Length >= 3 && args[0] == "p2")
{
    var d = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 1.0;
    return await P2Check.RunAsync(args[1], args[2], d);
}

var mvtRoot = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pge-bench", "mvt");
var outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "pge-bench", "p1");
Directory.CreateDirectory(outDir);

ViewExtentImageStyleRenderer.Register();
var renderer = new ViewExtentVectorRenderer(new SavedMvtTileSource(mvtRoot), new OpenMapTilesStyle());

var locations = new (string Name, double Lat, double Lon)[]
{
    ("dense-odori", 43.0606, 141.3469),
    ("suburb-okadama", 43.1180, 141.3900),
};

// 画像を描いた視点（image）と、表示する視点（view）の組。view が image と違えば、操作中（古い画像を変形して表示）を表す。
var cases = new List<Case>();
foreach (var density in new[] { 1.0, 1.25, 1.5, 2.0 })
{
    foreach (var (name, lat, lon) in locations)
    {
        foreach (var zoom in new[] { 15.0, 16.0, 17.4, 18.0, 18.7, 19.0 })
        {
            cases.Add(new Case($"{name} z{zoom.ToString("0.0", CultureInfo.InvariantCulture)} d{density.ToString("0.00", CultureInfo.InvariantCulture)} static", lat, lon, zoom, 683, 610, density, lat, lon, zoom, 683, 610));
        }

        // 操作中の見え方（画像は z17.4・683x610 で描いたまま、視点を動かす）
        var (px, py) = (60.0, 40.0);
        cases.Add(new Case($"{name} pan(+60,+40px) d{density:0.00}", lat, lon, 17.4, 683, 610, density, lat, lon, 17.4, 683, 610, PanX: px, PanY: py));
        cases.Add(new Case($"{name} zoom-in(+0.6) d{density:0.00}", lat, lon, 17.4, 683, 610, density, lat, lon, 18.0, 683, 610));
        cases.Add(new Case($"{name} zoom-out(-0.8) d{density:0.00}", lat, lon, 17.4, 683, 610, density, lat, lon, 16.6, 683, 610));
        cases.Add(new Case($"{name} resize(471x294) d{density:0.00}", lat, lon, 17.4, 683, 610, density, lat, lon, 17.4, 471, 294));
        cases.Add(new Case($"{name} resize(800x600) d{density:0.00}", lat, lon, 17.4, 683, 610, density, lat, lon, 17.4, 800, 600));
    }
}

var results = new List<object>();
var failures = 0;
foreach (var c in cases)
{
    foreach (var method in new[] { "A", "B" })
    {
        var r = await RunCaseAsync(c, method);
        results.Add(r);
        if (!r.Pass)
        {
            failures++;
        }
    }
}

var bench = await BenchAsync();
var summary = new
{
    bench,
    cases = cases.Count,
    runs = results.Count,
    failures,
    skiaSharp = typeof(SKCanvas).Assembly.GetName().Version?.ToString(),
    thresholdPx = Threshold,
    results,
};
await File.WriteAllTextAsync(Path.Combine(outDir, "p1-check.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"cases={cases.Count} runs={results.Count} failures={failures} (threshold {Threshold}px)");
return failures == 0 ? 0 : 1;

async Task<object> BenchAsync()
{
    // 同じ視点で、画像を置く費用と、1 フレーム分の描画（キャンバスへ直接）の費用を、方式ごとに測る（683x610・z17.4・density 1.0）
    var lat = locations[0].Lat; var lon = locations[0].Lon;
    var res = ViewExtentInjection.ResolutionFromZoom(17.4, 1.0);
    var center = SphericalMercator.FromLonLat(new MPoint(lon, lat));
    using var bmp = await renderer.RenderAsync(lat, lon, 17.4, 683, 610);
    using var img = SKImage.FromBitmap(bmp);
    var extent = new MRect(center.X - (683 * res / 2), center.Y - (610 * res / 2), center.X + (683 * res / 2), center.Y + (610 * res / 2));
    var map = new Mapsui.Map();
    var viewport = new Viewport(center.X, center.Y, res, 0, 683, 610);
    var output = new Dictionary<string, object>();
    foreach (var method in new[] { "A", "B" })
    {
        var place = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var sw0 = Stopwatch.StartNew();
            _ = method == "A" ? ViewExtentInjection.CreateLayerA(img, extent) : ViewExtentInjection.CreateLayerB(img, extent);
            place.Add(sw0.Elapsed.TotalMilliseconds);
        }

        var layer = method == "A" ? ViewExtentInjection.CreateLayerA(img, extent) : ViewExtentInjection.CreateLayerB(img, extent);
        using var target = new SKBitmap(683, 610);
        using var canvas = new SKCanvas(target);
        var mr = new MapRenderer();
        var draw = new List<double>();
        for (var i = 0; i < 60; i++)
        {
            var sw1 = Stopwatch.StartNew();
            mr.Render(canvas, viewport, new ILayer[] { layer }, Array.Empty<Mapsui.Widgets.IWidget>(), map.RenderService, null, null, CoordinateSpace.World);
            draw.Add(sw1.Elapsed.TotalMilliseconds);
        }

        draw.RemoveRange(0, 10);
        draw.Sort(); place.RemoveRange(0, 2); place.Sort();
        var first = new List<double>();
        for (var i = 0; i < 12; i++)
        {
            using var fresh = SKImage.FromBitmap(bmp);
            var sw2 = Stopwatch.StartNew();
            var fl = method == "A" ? ViewExtentInjection.CreateLayerA(fresh, extent) : ViewExtentInjection.CreateLayerB(fresh, extent);
            mr.Render(canvas, viewport, new ILayer[] { fl }, Array.Empty<Mapsui.Widgets.IWidget>(), map.RenderService, null, null, CoordinateSpace.World);
            first.Add(sw2.Elapsed.TotalMilliseconds);
        }

        first.RemoveRange(0, 2); first.Sort();
        output[method] = new { placeAndFirstDrawMedianMs = Math.Round(first[first.Count / 2], 3), placeMedianMs = Math.Round(place[place.Count / 2], 3), placeMaxMs = Math.Round(place[^1], 3), drawMedianMs = Math.Round(draw[draw.Count / 2], 3), drawP95Ms = Math.Round(draw[(int)(draw.Count * 0.95)], 3) };
    }

    return output;
}

async Task<Result> RunCaseAsync(Case c, string method)
{
    // Mapsui の viewport の解像度は DIP あたりの世界座標（m）。画像は物理ピクセル（DIP × density）で描く
    var dipRes = ViewExtentInjection.ResolutionFromZoom(c.ImageZoom, c.Density);
    var center = SphericalMercator.FromLonLat(new MPoint(c.ImageLon, c.ImageLat));

    // 1. 目印: 画像の中心からの画面オフセット（DIP）で置く。画像・視点のどちらにも入る範囲
    var offsets = new (double X, double Y)[] { (0, 0), (-120, -90), (110, -70), (-100, 85), (130, 95) };
    var marks = offsets.Select(o => SphericalMercator.ToLonLat(new MPoint(center.X + (o.X * dipRes), center.Y - (o.Y * dipRes)))).ToArray();

    // 2. 画像を描く（物理ピクセル寸法）
    var widthPx = (int)Math.Round(c.ImageWidth * c.Density);
    var heightPx = (int)Math.Round(c.ImageHeight * c.Density);
    var zoom = ViewExtentInjection.ZoomFromResolution(dipRes, c.Density); // c.ImageZoom に戻ることの確認を兼ねる
    var sw = Stopwatch.StartNew();
    using var bitmap = await renderer.RenderAsync(
        c.ImageLat, c.ImageLon, zoom, widthPx, heightPx,
        (canvas, toPixel) =>
        {
            using var paint = new SKPaint { Color = new SKColor(0, 255, 0), StrokeWidth = 3f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            foreach (var m in marks)
            {
                var p = toPixel(m.Y, m.X);
                canvas.DrawLine(p.X - 20, p.Y, p.X + 20, p.Y, paint);
                canvas.DrawLine(p.X, p.Y - 20, p.X, p.Y + 20, paint);
            }
        });
    var renderMs = sw.Elapsed.TotalMilliseconds;
    using var image = SKImage.FromBitmap(bitmap);

    // 画像の地理範囲（DIP 単位の viewport 幅 × dipRes）
    var halfW = c.ImageWidth * dipRes / 2;
    var halfH = c.ImageHeight * dipRes / 2;
    var extent = new MRect(center.X - halfW, center.Y - halfH, center.X + halfW, center.Y + halfH);

    sw.Restart();
    var imageLayer = method == "A" ? ViewExtentInjection.CreateLayerA(image, extent) : ViewExtentInjection.CreateLayerB(image, extent);
    var placeMs = sw.Elapsed.TotalMilliseconds;

    // 3. 標識の層
    var markerLayer = new MemoryLayer
    {
        Name = "Markers",
        Features = marks.Select(m => (IFeature)new PointFeature(SphericalMercator.FromLonLat(new MPoint(m.X, m.Y)))).ToArray(),
        Style = new SymbolStyle { SymbolType = SymbolType.Ellipse, SymbolScale = 0.3, Fill = new Brush(new Color(255, 0, 255)) },
    };

    // 4. 表示する視点で描く（視点の中心は、パンの分だけずらす）
    var viewRes = ViewExtentInjection.ResolutionFromZoom(c.ViewZoom, c.Density);
    var viewCenter = SphericalMercator.FromLonLat(new MPoint(c.ViewLon, c.ViewLat));
    var viewport = new Viewport(viewCenter.X + (c.PanX * viewRes), viewCenter.Y - (c.PanY * viewRes), viewRes, 0, c.ViewWidth, c.ViewHeight);
    var map = new Mapsui.Map();
    var layers = new ILayer[] { imageLayer, markerLayer };
    sw.Restart();
    using var stream = new MapRenderer().RenderToBitmapStream(viewport, layers, map.RenderService, Color.Transparent, (float)c.Density, null, RenderFormat.Png, 100);
    var drawMs = sw.Elapsed.TotalMilliseconds;
    var pngPath = Path.Combine(outDir, $"{Sanitize(c.Name)}-{method}.png");
    await File.WriteAllBytesAsync(pngPath, stream.ToArray());

    // 5. 画素の分析
    stream.Position = 0;
    using var rendered = SKBitmap.Decode(stream.ToArray());
    var worst = 0.0;
    var worstMarker = 0.0;
    var used = 0;
    foreach (var m in marks)
    {
        var world = SphericalMercator.FromLonLat(new MPoint(m.X, m.Y));
        var ex = (((world.X - viewport.CenterX) / viewport.Resolution) + (viewport.Width / 2)) * c.Density;
        var ey = (((viewport.CenterY - world.Y) / viewport.Resolution) + (viewport.Height / 2)) * c.Density;
        if (ex < 30 || ey < 30 || ex > rendered.Width - 30 || ey > rendered.Height - 30)
        {
            continue;
        }

        var green = Centroid(rendered, ex, ey, 26, Greenness);
        var magenta = Centroid(rendered, ex, ey, 26, Magentaness);
        if (green is null || magenta is null)
        {
            worst = double.PositiveInfinity;
            continue;
        }

        used++;
        worst = Math.Max(worst, Math.Sqrt(Math.Pow(green.Value.X - ex, 2) + Math.Pow(green.Value.Y - ey, 2)));
        worstMarker = Math.Max(worstMarker, Math.Sqrt(Math.Pow(magenta.Value.X - ex, 2) + Math.Pow(magenta.Value.Y - ey, 2)));
    }

    // 拡大して見るとき（image と view の倍率差が大きい）は、十字が太くなり、サンプリングの影響が出るため、許容を広げる
    var scaleDiff = Math.Abs(c.ViewZoom - c.ImageZoom);
    var threshold = Threshold + (scaleDiff * 0.6);
    var pass = used >= 2 && worst <= threshold && worstMarker <= Threshold;
    return new Result(c.Name, method, used, double.IsInfinity(worst) ? -1 : Math.Round(worst, 3), double.IsInfinity(worstMarker) ? -1 : Math.Round(worstMarker, 3), Math.Round(threshold, 2), pass, Math.Round(renderMs, 1), Math.Round(placeMs, 2), Math.Round(drawMs, 1));
}

static (double X, double Y)? Centroid(SKBitmap bitmap, double cx, double cy, int radius, Func<SKColor, double> weight)
{
    double sum = 0, sx = 0, sy = 0;
    var x0 = Math.Max(0, (int)(cx - radius));
    var x1 = Math.Min(bitmap.Width - 1, (int)(cx + radius));
    var y0 = Math.Max(0, (int)(cy - radius));
    var y1 = Math.Min(bitmap.Height - 1, (int)(cy + radius));
    for (var y = y0; y <= y1; y++)
    {
        for (var x = x0; x <= x1; x++)
        {
            var w = weight(bitmap.GetPixel(x, y));
            if (w <= 0)
            {
                continue;
            }

            sum += w;
            sx += w * (x + 0.5);
            sy += w * (y + 0.5);
        }
    }

    return sum < 1.0 ? null : (sx / sum, sy / sum);
}

static double Greenness(SKColor c) => Math.Max(0, (c.Green - Math.Max(c.Red, c.Blue)) - 70) / 185.0;

static double Magentaness(SKColor c) => Math.Max(0, (Math.Min(c.Red, c.Blue) - c.Green) - 70) / 185.0;

static string Sanitize(string name) => string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' ? ch : '_'));

partial class Program
{
    public const double Threshold = 1.0;
}

record Case(string Name, double ImageLat, double ImageLon, double ImageZoom, int ImageWidth, int ImageHeight, double Density, double ViewLat, double ViewLon, double ViewZoom, int ViewWidth, int ViewHeight, double PanX = 0, double PanY = 0);

record Result(string Name, string Method, int Marks, double MaxCrossErrorPx, double MaxMarkerErrorPx, double ThresholdPx, bool Pass, double ImageRenderMs, double PlaceMs, double DrawMs);
