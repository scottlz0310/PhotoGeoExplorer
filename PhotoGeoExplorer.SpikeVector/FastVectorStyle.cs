using System.Runtime.CompilerServices;
using PhotoMapStudio.Core.Tiles.Vector;
using PhotoMapStudio.Tiles.Vector;
using SkiaSharp;

namespace PhotoMapStudio.Spike;

/// <summary>
/// 手順 1（#293 P3a）の実験用オプション。既定（すべて無効）は、OpenMapTilesStyle の面・線と同じ描き方（描画のたびに、地物ごとにパスを作って 1 回描く）。
/// </summary>
public sealed record FastStyleOptions
{
    /// <summary>地物のパス（SKPath）を、タイルのデコード済みモデルに紐づけて 1 回だけ作り、描画のたびに作り直さない（出力は同じ）。</summary>
    public bool Prep { get; init; }

    /// <summary>描画先のクリップ範囲（タイル座標）と交わらない地物は、描かない（出力は同じ）。</summary>
    public bool Cull { get; init; }

    /// <summary>道路を、クリップ範囲（＋線幅）と交わる区間の頂点だけに切り詰めて描く。深いズームで、表示範囲外の頂点まで線幅をつけて描くのを避ける（出力は同じ）。</summary>
    public bool ClipVertices { get; init; }

    /// <summary>同じ見た目（色・幅）の道路・線を 1 つのパスにまとめて、1 回で描く（出力はほぼ同じ）。</summary>
    public bool BatchRoads { get; init; }

    /// <summary>この大きさ（デバイスピクセル）より小さい面（建物・小さな土地利用）を描かない。0 で無効（見た目が変わる）。</summary>
    public float MinPolygonPx { get; init; }

    /// <summary>ズームが <see cref="ThinBelowZoom"/> 未満のとき、この順位未満の道路（service・track など）を描かない。0 で無効（見た目が変わる）。</summary>
    public int MinRoadOrder { get; init; }

    public int ThinBelowZoom { get; init; } = 17;

    /// <summary>ズームがこの値未満のとき、破線の小道（path・footway など）を描かない。0 で無効（見た目が変わる）。</summary>
    public int HidePathsBelowZoom { get; init; }
}

/// <summary>
/// OpenMapTilesStyle の面・線の描画を、実験用に再実装したもの。注記（シンボル・線ラベル・ラベル）は元のスタイルへ委譲する。
/// </summary>
public sealed class FastVectorStyle : IVectorMapStyle, IDisposable
{
    private static readonly ConditionalWeakTable<VectorTileLayer, Prepared> Layers = new();

    private static readonly Dictionary<string, SKColor> LandcoverColors = new()
    {
        ["landcover:wood"] = SKColor.Parse("#c8dfb8"),
        ["landcover:grass"] = SKColor.Parse("#d8e8c8"),
        ["landcover:wetland"] = SKColor.Parse("#cfe2d6"),
        ["landcover:sand"] = SKColor.Parse("#f2e8c4"),
        ["landcover:ice"] = SKColor.Parse("#f4f8fb"),
        ["landuse:pitch"] = SKColor.Parse("#cfe6c4"),
        ["landuse:track"] = SKColor.Parse("#cfe6c4"),
        ["landuse:cemetery"] = SKColor.Parse("#d3dfcb"),
        ["landuse:school"] = SKColor.Parse("#ebe3cf"),
        ["landuse:college"] = SKColor.Parse("#ebe3cf"),
        ["landuse:university"] = SKColor.Parse("#ebe3cf"),
        ["landuse:hospital"] = SKColor.Parse("#ebe3cf"),
        ["landuse:industrial"] = SKColor.Parse("#dfdad5"),
        ["landuse:railway"] = SKColor.Parse("#dfdad5"),
        ["landuse:residential"] = SKColor.Parse("#e6e1d8"),
    };

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> Profile = new();
    public static bool ProfileEnabled;

    private static long Mark() => ProfileEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

    private static void Lap(string name, ref long since)
    {
        if (!ProfileEnabled)
        {
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var delta = now - since;
        Profile.AddOrUpdate(name, delta, (_, v) => v + delta);
        since = now;
    }

    private readonly OpenMapTilesStyle _inner = new();
    private readonly FastStyleOptions _options;

    private readonly SKPaint _water = new() { Color = new SKColor(0xA0, 0xC8, 0xF0), Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _park = new() { Color = new SKColor(0xD8, 0xE8, 0xC8), Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _buildingFill = new() { Color = new SKColor(0xDC, 0xD5, 0xC9), Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _buildingStroke = new() { Color = new SKColor(0xC9, 0xC0, 0xB1), Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f, IsAntialias = true };
    private readonly SKPaint _railway = new() { Color = new SKColor(0x77, 0x77, 0x77), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };

    public FastVectorStyle(FastStyleOptions options)
    {
        _options = options;
    }

    public string StyleId => "openmaptiles-fast";

    public SKColor BackgroundColor => _inner.BackgroundColor;

    public void RenderPolygons(SKCanvas canvas, VectorTile tile, float tileScale, int zoom)
    {
        var clip = canvas.LocalClipBounds;
        RenderLandcover(canvas, tile.GetLayer("landcover"), landuse: false, clip, tileScale);
        RenderLandcover(canvas, tile.GetLayer("landuse"), landuse: true, clip, tileScale);
        RenderPolygonLayer(canvas, tile.GetLayer("park"), _park, null, clip, tileScale, 0f);
        RenderPolygonLayer(canvas, tile.GetLayer("water"), _water, null, clip, tileScale, 0f);
        if (zoom >= 15)
        {
            var building = tile.GetLayer("building");
            if (building is not null)
            {
                using var outline = _buildingStroke.Clone();
                outline.StrokeWidth = 1f / tileScale;
                RenderPolygonLayer(canvas, building, _buildingFill, outline, clip, tileScale, _options.MinPolygonPx);
            }
        }
    }

    public void RenderLines(SKCanvas canvas, VectorTile tile, float tileScale, int zoom)
    {
        var clip = canvas.LocalClipBounds;
        var lap = Mark();

        using var railway = _railway.Clone();
        railway.StrokeWidth = 2.5f / tileScale;

        using var river = _railway.Clone();
        river.Color = _water.Color;
        river.StrokeWidth = 2.5f * ZoomScale(zoom) / tileScale;
        DrawLineGroup(canvas, tile.GetLayer("waterway"), river, clip, tileScale, static (p, i) => p.Features[i].GeometryType == VectorGeometryType.LineString);
        Lap("lines.waterway", ref lap);

        using var boundary = _railway.Clone();
        boundary.Color = new SKColor(0xB0, 0x8F, 0xB5);
        boundary.StrokeWidth = 1.8f / tileScale;
        using var boundaryDash = SKPathEffect.CreateDash([7f / tileScale, 4f / tileScale], 0);
        boundary.PathEffect = boundaryDash;
        DrawLineGroup(canvas, tile.GetLayer("boundary"), boundary, clip, tileScale, static (p, i) => p.Features[i].GeometryType == VectorGeometryType.LineString && p.Admin[i] <= 4);

        Lap("lines.boundary", ref lap);
        var roadLayer = tile.GetLayer("transportation");
        if (roadLayer is not null)
        {
            RenderRoads(canvas, roadLayer, tileScale, zoom, clip);
            lap = Mark();

            using var path = railway.Clone();
            path.Color = new SKColor(0xB9, 0x9C, 0x80, 160);
            path.StrokeWidth = 1.2f * Math.Max(1f, ZoomScale(zoom) * 0.45f) / tileScale;
            using var pathDash = SKPathEffect.CreateDash([4f / tileScale, 3f / tileScale], 0);
            path.PathEffect = pathDash;
            if (_options.HidePathsBelowZoom == 0 || zoom >= _options.HidePathsBelowZoom)
            {
                if (_options.ClipVertices)
                {
                    DrawDashedGroup(canvas, roadLayer, path, 4f / tileScale, 3f / tileScale, clip, tileScale);
                }
                else
                {
                    DrawLineGroup(canvas, roadLayer, path, clip, tileScale, static (p, i) => p.Features[i].GeometryType == VectorGeometryType.LineString && p.RoadCode[i] == 8);
                }
            }


            Lap("lines.paths", ref lap);
            DrawLineGroup(canvas, roadLayer, railway, clip, tileScale, static (p, i) => IsRail(p, i));
            Lap("lines.rail", ref lap);
            using var ties = railway.Clone();
            ties.Color = SKColors.White;
            ties.StrokeWidth = 1.2f / tileScale;
            using var railDash = SKPathEffect.CreateDash([6f / tileScale, 6f / tileScale], 0);
            ties.PathEffect = railDash;
            DrawLineGroup(canvas, roadLayer, ties, clip, tileScale, static (p, i) => IsRail(p, i));
            Lap("lines.ties", ref lap);
        }

        var fallback = tile.GetLayer("railway");
        if (fallback is not null)
        {
            DrawLineGroup(canvas, fallback, railway, clip, tileScale, static (p, i) => p.Features[i].GeometryType == VectorGeometryType.LineString);
        }
    }

    public void RenderSymbols(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport)
        => _inner.RenderSymbols(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport);

    public void RenderLineLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport)
        => _inner.RenderLineLabels(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport);

    public void RenderLabels(SKCanvas canvas, VectorTile tile, SKMatrix tileToCanvasMatrix, int zoom, LabelCollisionGrid collisionGrid, SKRect viewport)
        => _inner.RenderLabels(canvas, tile, tileToCanvasMatrix, zoom, collisionGrid, viewport);

    public void Dispose()
    {
        _inner.Dispose();
        _water.Dispose();
        _park.Dispose();
        _buildingFill.Dispose();
        _buildingStroke.Dispose();
        _railway.Dispose();
    }

    private static bool IsRail(Prepared p, int i)
        => p.Features[i].GeometryType == VectorGeometryType.LineString && (p.Class[i] ?? string.Empty) is "rail" or "transit" or "subway" or "tram";

    private static Prepared Prepare(VectorTileLayer layer) => Layers.GetValue(layer, static l => new Prepared(l));

    private void RenderLandcover(SKCanvas canvas, VectorTileLayer? layer, bool landuse, SKRect clip, float tileScale)
    {
        if (layer is null)
        {
            return;
        }

        var prepared = Prepare(layer);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        for (var i = 0; i < prepared.Features.Length; i++)
        {
            if (prepared.Features[i].GeometryType != VectorGeometryType.Polygon)
            {
                continue;
            }

            if (prepared.Color[i] is not { } color)
            {
                continue;
            }

            if (!Visible(prepared, i, clip, 1f / tileScale, tileScale, _options.MinPolygonPx))
            {
                continue;
            }

            paint.Color = color;
            DrawPolygon(canvas, prepared, i, paint, null);
        }
    }

    private void RenderPolygonLayer(SKCanvas canvas, VectorTileLayer? layer, SKPaint fill, SKPaint? stroke, SKRect clip, float tileScale, float minPx)
    {
        if (layer is null)
        {
            return;
        }

        var prepared = Prepare(layer);
        for (var i = 0; i < prepared.Features.Length; i++)
        {
            if (prepared.Features[i].GeometryType != VectorGeometryType.Polygon)
            {
                continue;
            }

            if (!Visible(prepared, i, clip, 1f / tileScale, tileScale, minPx))
            {
                continue;
            }

            DrawPolygon(canvas, prepared, i, fill, stroke);
        }
    }

    private bool Visible(Prepared p, int i, SKRect clip, float margin, float tileScale, float minPx)
    {
        if (_options.Cull && (p.R[i] + margin < clip.Left || p.L[i] - margin > clip.Right || p.B[i] + margin < clip.Top || p.T[i] - margin > clip.Bottom))
        {
            return false;
        }

        return minPx <= 0f || Math.Max(p.R[i] - p.L[i], p.B[i] - p.T[i]) * tileScale >= minPx;
    }

    private void DrawPolygon(SKCanvas canvas, Prepared p, int i, SKPaint fill, SKPaint? stroke)
    {
        if (_options.Prep)
        {
            var path = p.PathAt(i, polygon: true);
            canvas.DrawPath(path, fill);
            if (stroke is not null)
            {
                canvas.DrawPath(path, stroke);
            }

            return;
        }

        using var built = Prepared.BuildPolygon(p.Features[i]);
        canvas.DrawPath(built, fill);
        if (stroke is not null)
        {
            canvas.DrawPath(built, stroke);
        }
    }

    private void DrawFeatureLine(SKCanvas canvas, Prepared p, int i, SKPaint paint)
    {
        if (_options.Prep)
        {
            canvas.DrawPath(p.PathAt(i, polygon: false), paint);
            return;
        }

        using var built = Prepared.BuildLines(p.Features[i]);
        canvas.DrawPath(built, paint);
    }

    private void DrawLineGroup(SKCanvas canvas, VectorTileLayer? layer, SKPaint paint, SKRect clip, float tileScale, Func<Prepared, int, bool> select)
    {
        if (layer is null)
        {
            return;
        }

        var prepared = Prepare(layer);
        var margin = (paint.StrokeWidth / 2f) + (1f / tileScale);
        if (_options.BatchRoads)
        {
            using var builder = new SKPathBuilder();
            var any = false;
            for (var i = 0; i < prepared.Features.Length; i++)
            {
                if (!select(prepared, i) || !Visible(prepared, i, clip, margin, 1f, 0f))
                {
                    continue;
                }

                prepared.Append(builder, i, _options.Prep);
                any = true;
            }

            if (any)
            {
                using var merged = builder.Detach();
                canvas.DrawPath(merged, paint);
            }

            return;
        }

        for (var i = 0; i < prepared.Features.Length; i++)
        {
            if (!select(prepared, i) || !Visible(prepared, i, clip, margin, 1f, 0f))
            {
                continue;
            }

            DrawFeatureLine(canvas, prepared, i, paint);
        }
    }

    private void RenderRoads(SKCanvas canvas, VectorTileLayer layer, float tileScale, int zoom, SKRect clip)
    {
        var rlap = Mark();
        var prepared = Prepare(layer);
        var roads = new List<(int Index, int Order, SKColor Casing, SKColor Fill, float CasingWidth, float FillWidth, float Margin)>();
        var styles = new (int Order, SKColor Casing, SKColor Fill, float CasingWidth, float FillWidth, float Margin, bool Skip)[9];
        string[] codeClass = { string.Empty, "motorway", "trunk", "primary", "secondary", "tertiary", "residential", "service", "path" };
        for (var code = 1; code <= 7; code++)
        {
            var (order, casing, fill, casingWidth, fillWidth) = GetRoadStyle(codeClass[code], zoom);
            var thin = _options.MinRoadOrder > 0 && zoom < _options.ThinBelowZoom && order < _options.MinRoadOrder;
            styles[code] = (order, casing, fill, casingWidth, fillWidth, (Math.Max(casingWidth, fillWidth) / tileScale / 2f) + (1f / tileScale), thin);
        }

        for (var i = 0; i < prepared.Features.Length; i++)
        {
            var code = prepared.RoadCode[i];
            if (code is 0 or 8 || prepared.Features[i].GeometryType != VectorGeometryType.LineString)
            {
                continue;
            }

            var s = styles[code];
            if (s.Skip || !Visible(prepared, i, clip, s.Margin, 1f, 0f))
            {
                continue;
            }

            roads.Add((i, s.Order, s.Casing, s.Fill, s.CasingWidth, s.FillWidth, s.Margin));
        }

        Lap("roads.select", ref rlap);
        if (ProfileEnabled)
        {
            Profile.AddOrUpdate("roads.count", roads.Count, (_, v) => v + roads.Count);
            long pts = 0;
            foreach (var r in roads)
            {
                foreach (var part in prepared.Features[r.Index].GeometryParts)
                {
                    pts += part.Count;
                }
            }

            Profile.AddOrUpdate("roads.points", pts, (_, v) => v + pts);
        }

        if (_options.BatchRoads)
        {
            // 同じ順位の道路は同じ見た目なので、順位ごとに 1 つのパスにまとめる（下位から上位へ）
            var groups = roads.GroupBy(r => r.Order).OrderBy(g => g.Key).ToList();
            var paths = new List<(SKPath Path, (int Index, int Order, SKColor Casing, SKColor Fill, float CasingWidth, float FillWidth, float Margin) Style)>();
            try
            {
                foreach (var group in groups)
                {
                    using var builder = new SKPathBuilder();
                    foreach (var road in group)
                    {
                        AppendRoad(builder, prepared, road.Index, clip, road.Margin);
                    }

                    paths.Add((builder.Detach(), group.First()));
                }

                foreach (var (path, style) in paths)
                {
                    if (style.CasingWidth > 0f)
                    {
                        using var paint = StrokePaint(style.Casing, style.CasingWidth / tileScale);
                        canvas.DrawPath(path, paint);
                    }
                }

                foreach (var (path, style) in paths)
                {
                    using var paint = StrokePaint(style.Fill, style.FillWidth / tileScale);
                    canvas.DrawPath(path, paint);
                }
            }
            finally
            {
                foreach (var (path, _) in paths)
                {
                    path.Dispose();
                }
            }

            return;
        }

        roads.Sort((a, b) => a.Order.CompareTo(b.Order));
        Lap("roads.sort", ref rlap);
        foreach (var road in roads)
        {
            if (road.CasingWidth > 0f)
            {
                using var paint = StrokePaint(road.Casing, road.CasingWidth / tileScale);
                DrawRoad(canvas, prepared, road.Index, clip, road.Margin, paint);
            }
        }

        Lap("roads.casing", ref rlap);

        foreach (var road in roads)
        {
            using var paint = StrokePaint(road.Fill, road.FillWidth / tileScale);
            DrawRoad(canvas, prepared, road.Index, clip, road.Margin, paint);
        }

        Lap("roads.fill", ref rlap);
    }

    /// <summary>
    /// 破線の小道を、表示範囲（＋線幅）と交わる区間だけに切り詰めて描く。破線の位相は、元のパスの始点からの距離で合わせる（見た目は同じ）。
    /// Skia は破線をパスの全長に対して生成するため、深いズームでは、画面外の区間の処理が支配的になる。
    /// </summary>
    private void DrawDashedGroup(SKCanvas canvas, VectorTileLayer? layer, SKPaint paint, float on, float off, SKRect clip, float tileScale)
    {
        if (layer is null)
        {
            return;
        }

        var prepared = Prepare(layer);
        var margin = (paint.StrokeWidth / 2f) + (1f / tileScale);
        var area = new SKRect(clip.Left - margin, clip.Top - margin, clip.Right + margin, clip.Bottom + margin);
        var period = on + off;
        for (var i = 0; i < prepared.Features.Length; i++)
        {
            if (prepared.Features[i].GeometryType != VectorGeometryType.LineString || prepared.RoadCode[i] != 8 || !Visible(prepared, i, clip, margin, 1f, 0f))
            {
                continue;
            }

            if (!NeedsClip(prepared, i, clip, margin))
            {
                canvas.DrawPath(prepared.PathAt(i, polygon: false), paint);
                continue;
            }

            var feature = prepared.Features[i];
            for (var p = 0; p < feature.GeometryParts.Count; p++)
            {
                var line = feature.GeometryParts[p];
                double distance = 0;
                SKPathBuilder? builder = null;
                double runPhase = 0;
                for (var k = 0; k < line.Count - 1; k++)
                {
                    var a = line[k];
                    var b = line[k + 1];
                    var intersects = Math.Max(a.X, b.X) >= area.Left && Math.Min(a.X, b.X) <= area.Right && Math.Max(a.Y, b.Y) >= area.Top && Math.Min(a.Y, b.Y) <= area.Bottom;
                    if (intersects)
                    {
                        if (builder is null)
                        {
                            builder = new SKPathBuilder();
                            builder.MoveTo(a.X, a.Y);
                            runPhase = distance;
                        }

                        builder.LineTo(b.X, b.Y);
                    }
                    else if (builder is not null)
                    {
                        FlushDashedRun(canvas, builder, paint, on, off, runPhase % period);
                        builder = null;
                    }

                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    distance += Math.Sqrt((dx * (double)dx) + (dy * (double)dy));
                }

                if (builder is not null)
                {
                    FlushDashedRun(canvas, builder, paint, on, off, runPhase % period);
                }
            }
        }
    }

    private static void FlushDashedRun(SKCanvas canvas, SKPathBuilder builder, SKPaint template, float on, float off, double phase)
    {
        using (builder)
        using (var path = builder.Detach())
        using (var effect = SKPathEffect.CreateDash([on, off], (float)phase))
        using (var paint = template.Clone())
        {
            paint.PathEffect = effect;
            canvas.DrawPath(path, paint);
        }
    }

    private bool NeedsClip(Prepared p, int i, SKRect clip, float margin)
        => _options.ClipVertices && (p.L[i] - margin < clip.Left || p.R[i] + margin > clip.Right || p.T[i] - margin < clip.Top || p.B[i] + margin > clip.Bottom);

    private void DrawRoad(SKCanvas canvas, Prepared p, int i, SKRect clip, float margin, SKPaint paint)
    {
        if (!NeedsClip(p, i, clip, margin))
        {
            DrawFeatureLine(canvas, p, i, paint);
            return;
        }

        using var builder = new SKPathBuilder();
        Prepared.AddClippedLines(builder, p.Features[i], new SKRect(clip.Left - margin, clip.Top - margin, clip.Right + margin, clip.Bottom + margin));
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private void AppendRoad(SKPathBuilder builder, Prepared p, int i, SKRect clip, float margin)
    {
        if (NeedsClip(p, i, clip, margin))
        {
            Prepared.AddClippedLines(builder, p.Features[i], new SKRect(clip.Left - margin, clip.Top - margin, clip.Right + margin, clip.Bottom + margin));
            return;
        }

        p.Append(builder, i, _options.Prep);
    }

    private static SKPaint StrokePaint(SKColor color, float width) => new()
    {
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
        IsAntialias = true,
    };

    private static (int Order, SKColor Casing, SKColor Fill, float CasingWidth, float FillWidth) GetRoadStyle(string roadClass, int zoom)
    {
        var scale = ZoomScale(zoom);
        var (order, width, fill, casing) = roadClass switch
        {
            "motorway" => (90, 9.5f, "#9ad8a8", "#4f9a62"),
            "trunk" => (80, 8.5f, "#ffb27a", "#d9803f"),
            "primary" => (70, 7.5f, "#ffe27a", "#cfa21f"),
            "secondary" => (60, 7f, "#ffe27a", "#cfa21f"),
            "tertiary" => (50, 6f, "#fff3c4", "#d8c27a"),
            "minor" or "residential" or "unclassified" or "living_street" => (40, 4.5f, "#ffffff", "#cfc9bf"),
            "service" or "track" => (30, 3.5f, "#ffffff", "#d5cfc5"),
            _ => (0, 0f, "#00000000", "#00000000"),
        };
        return (order, ParseCached(casing), ParseCached(fill), width > 0 ? (width * scale) + 2f : 0f, width * scale);
    }

    private static readonly Dictionary<string, SKColor> ColorCache = new();

    private static SKColor ParseCached(string hex)
    {
        lock (ColorCache)
        {
            if (!ColorCache.TryGetValue(hex, out var color))
            {
                color = SKColor.Parse(hex);
                ColorCache[hex] = color;
            }

            return color;
        }
    }

    private static bool IsPath(string? roadClass) => roadClass is "path" or "pedestrian" or "footway" or "cycleway" or "steps";

    private static float ZoomScale(int zoom) => (float)Math.Pow(2, (zoom - 16) * 0.75);

    /// <summary>レイヤーの地物ごとの、境界・属性・パス（遅延生成）。タイルのデコード済みモデルと同じ寿命。</summary>
    private sealed class Prepared
    {
        public readonly VectorTileFeature[] Features;
        public readonly float[] L;
        public readonly float[] T;
        public readonly float[] R;
        public readonly float[] B;
        public readonly string?[] Class;
        public readonly long[] Admin;
        public readonly byte[] RoadCode;
        public readonly SKColor?[] Color;
        private readonly SKPath?[] _paths;

        public Prepared(VectorTileLayer layer)
        {
            Features = layer.Features.ToArray();
            var n = Features.Length;
            L = new float[n];
            T = new float[n];
            R = new float[n];
            B = new float[n];
            Class = new string?[n];
            Admin = new long[n];
            RoadCode = new byte[n];
            Color = new SKColor?[n];
            _paths = new SKPath?[n];
            for (var i = 0; i < n; i++)
            {
                var f = Features[i];
                float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
                for (var p = 0; p < f.GeometryParts.Count; p++)
                {
                    var part = f.GeometryParts[p];
                    for (var k = 0; k < part.Count; k++)
                    {
                        var pt = part[k];
                        l = Math.Min(l, pt.X);
                        t = Math.Min(t, pt.Y);
                        r = Math.Max(r, pt.X);
                        b = Math.Max(b, pt.Y);
                    }
                }

                L[i] = l;
                T[i] = t;
                R[i] = r;
                B[i] = b;
                Class[i] = f.GetString("class");
                Admin[i] = f.GetInt64("admin_level") ?? long.MaxValue;
                RoadCode[i] = RoadCodeOf(Class[i]);
                if (LandcoverColors.TryGetValue(layer.Name + ":" + Class[i], out var color))
                {
                    Color[i] = color;
                }
            }
        }

        private static byte RoadCodeOf(string? roadClass) => roadClass switch
        {
            "motorway" => 1,
            "trunk" => 2,
            "primary" => 3,
            "secondary" => 4,
            "tertiary" => 5,
            "minor" or "residential" or "unclassified" or "living_street" => 6,
            "service" or "track" => 7,
            "path" or "pedestrian" or "footway" or "cycleway" or "steps" => 8,
            _ => 0,
        };

        public SKPath PathAt(int i, bool polygon)
        {
            var cached = Volatile.Read(ref _paths[i]);
            if (cached is not null)
            {
                return cached;
            }

            var built = polygon ? BuildPolygon(Features[i]) : BuildLines(Features[i]);
            var previous = Interlocked.CompareExchange(ref _paths[i], built, null);
            if (previous is not null)
            {
                built.Dispose();
                return previous;
            }

            return built;
        }

        public void Append(SKPathBuilder builder, int i, bool usePrebuilt)
        {
            if (usePrebuilt)
            {
                builder.AddPath(PathAt(i, polygon: false), SKPathAddMode.Append);
                return;
            }

            AddLines(builder, Features[i]);
        }

        public static void AddClippedLines(SKPathBuilder builder, VectorTileFeature feature, SKRect area)
        {
            for (var p = 0; p < feature.GeometryParts.Count; p++)
            {
                var line = feature.GeometryParts[p];
                if (line.Count < 2)
                {
                    continue;
                }

                var open = false;
                for (var i = 0; i < line.Count - 1; i++)
                {
                    var a = line[i];
                    var b = line[i + 1];
                    var intersects = Math.Max(a.X, b.X) >= area.Left && Math.Min(a.X, b.X) <= area.Right && Math.Max(a.Y, b.Y) >= area.Top && Math.Min(a.Y, b.Y) <= area.Bottom;
                    if (!intersects)
                    {
                        open = false;
                        continue;
                    }

                    if (!open)
                    {
                        builder.MoveTo(a.X, a.Y);
                        open = true;
                    }

                    builder.LineTo(b.X, b.Y);
                }
            }
        }

        public static SKPath BuildPolygon(VectorTileFeature feature)
        {
            using var builder = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
            for (var r = 0; r < feature.GeometryParts.Count; r++)
            {
                var ring = feature.GeometryParts[r];
                if (ring.Count < 3)
                {
                    continue;
                }

                builder.MoveTo(ring[0].X, ring[0].Y);
                for (var p = 1; p < ring.Count; p++)
                {
                    builder.LineTo(ring[p].X, ring[p].Y);
                }

                builder.Close();
            }

            return builder.Detach();
        }

        public static SKPath BuildLines(VectorTileFeature feature)
        {
            using var builder = new SKPathBuilder();
            AddLines(builder, feature);
            return builder.Detach();
        }

        public static void AddLines(SKPathBuilder builder, VectorTileFeature feature)
        {
            for (var p = 0; p < feature.GeometryParts.Count; p++)
            {
                var line = feature.GeometryParts[p];
                if (line.Count < 2)
                {
                    continue;
                }

                builder.MoveTo(line[0].X, line[0].Y);
                for (var i = 1; i < line.Count; i++)
                {
                    builder.LineTo(line[i].X, line[i].Y);
                }
            }
        }
    }
}
