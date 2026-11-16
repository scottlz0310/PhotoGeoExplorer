#pragma warning disable CA1031, CA1869, CA1822, CA1305, CA1848, CA2007, CA1859, CA1860, CA1861, CA1304, CA1311, CA1307, CA1308, CA2000, CA1812, CA5392, CA1401, CA2201
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.UI;
using Mapsui.UI.WinUI;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Spike;
using SkiaSharp;

namespace PhotoGeoExplorer.Diagnostics.MapBench;

/// <summary>
/// スパイク #293 の P1: 実アプリの MapControl で、表示範囲画像（方式 A）と標識の位置が一致するかを、画面のキャプチャで確認する。
/// 環境変数 PGE_P1_OUT で有効。OSM のラスタは無効にし、保存済みの MVT から描いた画像だけを背景にする（通信しない）。
/// </summary>
internal sealed class P1LiveCheck
{
    private static readonly (double X, double Y)[] Offsets = { (-120, -90), (110, -70), (-100, 85), (130, 95), (0, -140), (0, 150) };

    private readonly MapControl _control;
    private readonly Mapsui.Map _map;
    private readonly string _outDir;
    private readonly ViewExtentVectorRenderer _renderer;
    private readonly List<object> _results = new();
    private MemoryLayer? _imageLayer;
    private SKImage? _image;
    private (double Lat, double Lon)[] _marks = Array.Empty<(double, double)>();

    private P1LiveCheck(MapControl control, Mapsui.Map map, string outDir)
    {
        _control = control;
        _map = map;
        _outDir = outDir;
        var mvt = Environment.GetEnvironmentVariable("PGE_P1_MVT") ?? Path.Combine(Path.GetTempPath(), "pge-bench", "mvt");
        _renderer = new ViewExtentVectorRenderer(new SavedMvtTileSource(mvt), new OpenMapTilesStyle());
    }

    public static void TryStart(MapControl control, Mapsui.Map map)
    {
        var outDir = Environment.GetEnvironmentVariable("PGE_P1_OUT");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        _ = new P1LiveCheck(control, map, outDir).RunAsync();
    }

    private async Task RunAsync()
    {
        var info = new Dictionary<string, object?>();
        try
        {
            ViewExtentImageStyleRenderer.Register();
            while (!(_map.Navigator.Viewport.Width > 0 && _control.ActualWidth > 0 && _control.XamlRoot is not null))
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("PGE_P1_WARMUP_MS"), out var warmup) ? warmup : 2500).ConfigureAwait(true);
            foreach (var tile in _map.Layers.OfType<Mapsui.Tiling.Layers.TileLayer>())
            {
                tile.Enabled = false;
            }

            var density = WindowScale();
            info["rasterizationScale"] = density;
            info["xamlRasterizationScale"] = _control.XamlRoot!.RasterizationScale;
            info["mapsuiPixelDensity"] = ((Mapsui.UI.IMapControl)_control).GetPixelDensity();
            info["windowDpi"] = GetDpiForWindow(Process.GetCurrentProcess().MainWindowHandle);
            info["controlDip"] = new[] { _control.ActualWidth, _control.ActualHeight };
            info["skiaSharp"] = typeof(SKCanvas).Assembly.GetName().Version?.ToString();

            // 場所: 大通（密集市街地）。固定の 5 点を、画像の中心からの画面オフセットで置く（P1 の位置確認と同じ）
            var lat = 43.0606;
            var lon = 141.3469;
            var center = SphericalMercator.FromLonLat(new MPoint(lon, lat));
            var zoom = 17.4;
            _map.Navigator.CenterOnAndZoomTo(center, ViewExtentInjection.ResolutionFromZoom(zoom, density), 0, Mapsui.Animations.Easing.Linear);
            await Task.Delay(500).ConfigureAwait(true);
            var vp = _map.Navigator.Viewport;
            _marks = Offsets.Select(o => SphericalMercator.ToLonLat(new MPoint(vp.CenterX + (o.X * vp.Resolution), vp.CenterY - (o.Y * vp.Resolution)))).Select(p => (p.Y, p.X)).ToArray();
            var markerLayer = new MemoryLayer
            {
                Name = "P1Markers",
                Features = _marks.Select(m => (IFeature)new PointFeature(SphericalMercator.FromLonLat(new MPoint(m.Lon, m.Lat)))).ToArray(),
                Style = new SymbolStyle { SymbolType = SymbolType.Ellipse, SymbolScale = 0.3, Fill = new Brush(new Color(255, 0, 255)) },
            };
            _map.Layers.Add(markerLayer);

            // 1. 静止（現在の視点で描いた画像）
            await UpdateImageAsync().ConfigureAwait(true);
            await Capture("static", density).ConfigureAwait(true);

            // 2. 操作中（画像はそのままで、視点だけ動かす）: パン・ズーム
            var cur = _map.Navigator.Viewport;
            _map.Navigator.CenterOn(cur.CenterX + (50 * cur.Resolution), cur.CenterY - (30 * cur.Resolution), 0, Mapsui.Animations.Easing.Linear);
            await Task.Delay(300).ConfigureAwait(true);
            await Capture("stale-pan(+50,+30)", density).ConfigureAwait(true);

            _map.Navigator.ZoomTo(cur.Resolution / Math.Pow(2, 0.5), 0, Mapsui.Animations.Easing.Linear);
            await Task.Delay(300).ConfigureAwait(true);
            await Capture("stale-zoom-in(+0.5)", density).ConfigureAwait(true);

            _map.Navigator.ZoomTo(cur.Resolution * Math.Pow(2, 0.6), 0, Mapsui.Animations.Easing.Linear);
            await Task.Delay(300).ConfigureAwait(true);
            await Capture("stale-zoom-out(-0.6)", density).ConfigureAwait(true);

            // 3. 画像を描き直したあと（連続倍率 z17.9 の視点）
            _map.Navigator.CenterOnAndZoomTo(center, ViewExtentInjection.ResolutionFromZoom(17.9, density), 0, Mapsui.Animations.Easing.Linear);
            await Task.Delay(300).ConfigureAwait(true);
            await UpdateImageAsync().ConfigureAwait(true);
            await Capture("fractional-z17.9", density).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            info["error"] = ex.ToString();
        }
        finally
        {
            info["results"] = _results;
            await File.WriteAllTextAsync(Path.Combine(_outDir, $"p1-live-d{info.GetValueOrDefault("rasterizationScale")}.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(true);
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    /// <summary>現在の視点（中心・解像度・寸法）で画像を描き、地理範囲へ置く。</summary>
    private async Task UpdateImageAsync()
    {
        var vp = _map.Navigator.Viewport;
        var density = WindowScale();
        var zoom = ViewExtentInjection.ZoomFromResolution(vp.Resolution, density);
        var centerLonLat = SphericalMercator.ToLonLat(new MPoint(vp.CenterX, vp.CenterY));
        var widthPx = (int)Math.Round(vp.Width * density);
        var heightPx = (int)Math.Round(vp.Height * density);
        var sw = Stopwatch.StartNew();
        using var bitmap = await _renderer.RenderAsync(
            centerLonLat.Y, centerLonLat.X, zoom, widthPx, heightPx,
            (canvas, toPixel) =>
            {
                using var paint = new SKPaint { Color = new SKColor(0, 255, 0), StrokeWidth = 3f, IsAntialias = true, Style = SKPaintStyle.Stroke };
                foreach (var m in _marks)
                {
                    var p = toPixel(m.Lat, m.Lon);
                    canvas.DrawLine(p.X - 20, p.Y, p.X + 20, p.Y, paint);
                    canvas.DrawLine(p.X, p.Y - 20, p.X, p.Y + 20, paint);
                }
            }).ConfigureAwait(true);
        var renderMs = sw.Elapsed.TotalMilliseconds;
        var newImage = SKImage.FromBitmap(bitmap);
        var halfW = vp.Width * vp.Resolution / 2;
        var halfH = vp.Height * vp.Resolution / 2;
        var extent = new MRect(vp.CenterX - halfW, vp.CenterY - halfH, vp.CenterX + halfW, vp.CenterY + halfH);
        var layer = ViewExtentInjection.CreateLayerA(newImage, extent);
        if (_imageLayer is not null)
        {
            _map.Layers.Remove(_imageLayer);
        }

        _map.Layers.Insert(0, layer);
        _imageLayer = layer;
        _image?.Dispose();
        _image = newImage;
        _map.Refresh();
        _results.Add(new { step = "update-image", zoom = Math.Round(zoom, 3), widthPx, heightPx, renderMs = Math.Round(renderMs, 1) });
        await Task.Delay(400).ConfigureAwait(true);
    }

    private async Task Capture(string name, double density)
    {
        var vp = _map.Navigator.Viewport;
        var hwnd = Process.GetCurrentProcess().MainWindowHandle;
        var origin = new Point2 { X = 0, Y = 0 };
        ClientToScreen(hwnd, ref origin);
        var offset = _control.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var left = origin.X + (int)Math.Round(offset.X * density);
        var top = origin.Y + (int)Math.Round(offset.Y * density);
        var width = (int)Math.Round(_control.ActualWidth * density);
        var height = (int)Math.Round(_control.ActualHeight * density);
        using var bmp = new System.Drawing.Bitmap(width, height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
        }

        var path = Path.Combine(_outDir, $"live-d{density:0.00}-{name.Replace('(', '_').Replace(')', '_').Replace(',', '_').Replace('+', 'p')}.png");
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        double worstCross = 0, worstMarker = 0;
        var used = 0;
        foreach (var m in _marks)
        {
            var world = SphericalMercator.FromLonLat(new MPoint(m.Lon, m.Lat));
            var ex = (((world.X - vp.CenterX) / vp.Resolution) + (vp.Width / 2)) * density;
            var ey = (((vp.CenterY - world.Y) / vp.Resolution) + (vp.Height / 2)) * density;
            if (ex < 30 || ey < 30 || ex > width - 30 || ey > height - 30)
            {
                continue;
            }

            var green = Centroid(bmp, ex, ey, 26, c => Math.Max(0, (c.G - Math.Max(c.R, c.B)) - 12) / 35.0);
            var magenta = Centroid(bmp, ex, ey, 26, c => Math.Max(0, (Math.Min(c.R, c.B) - c.G) - 12) / 35.0);
            if (green is null || magenta is null)
            {
                continue;
            }

            used++;
            worstCross = Math.Max(worstCross, Dist(green.Value, ex, ey));
            worstMarker = Math.Max(worstMarker, Dist(magenta.Value, ex, ey));
        }

        _results.Add(new { step = name, marks = used, maxCrossErrorPx = Math.Round(worstCross, 3), maxMarkerErrorPx = Math.Round(worstMarker, 3), capture = path });
        await Task.CompletedTask.ConfigureAwait(true);
    }

    // 画像の寸法と期待位置は、Mapsui が実際に使う画素密度（キャンバスの画素数 / DIP 幅。丸めでウィンドウの DPI と少し違う）で求める
    private double WindowScale() => ((IMapControl)_control).GetPixelDensity() ?? 1.0;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static double Dist((double X, double Y) p, double x, double y) => Math.Sqrt(((p.X - x) * (p.X - x)) + ((p.Y - y) * (p.Y - y)));

    private static (double X, double Y)? Centroid(System.Drawing.Bitmap bitmap, double cx, double cy, int radius, Func<System.Drawing.Color, double> weight)
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Point2
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref Point2 point);
}
