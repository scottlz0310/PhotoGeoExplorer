using System.Collections.Frozen;
using PhotoGeoExplorer.Models;
using PhotoMapStudio.Tiles.Vector;

namespace PhotoGeoExplorer.Services.RoadNames;

/// <summary>
/// OpenFreeMap（OpenMapTiles スキーマ）の <c>transportation_name</c> レイヤーから、座標の最寄りの道路名の候補を求める
/// </summary>
/// <remarks>
/// 候補は確定値ではない。種別（国道・都道府県道など）は推定せず、データにある名称と路線番号だけを返す
/// （OpenFreeMap の日本の道路は、経路網が地域区分を持たず、道路種別からの推定は外れることがある）。
/// 国道は、名称が「国道53号」の形で入っているので、そのまま名称として返る
/// </remarks>
internal sealed class RoadNameSuggester : IRoadNameSuggester
{
    /// <summary>OpenFreeMap のベクタータイルの最大ズーム</summary>
    internal const int TileZoom = 14;

    /// <summary>この距離（メートル）以内の道路だけを候補にする</summary>
    internal const double SearchRadiusMeters = 50d;

    internal const int MaxCandidates = 3;

    private const string LayerName = "transportation_name";

    // 歩行者用の経路（path。駅のホームなど）や農林道（track）は、「道路名」として出すと不自然なので候補にしない
    private static readonly FrozenSet<string> RoadClasses = new[]
    {
        "motorway", "trunk", "primary", "secondary", "tertiary", "minor", "service",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly char[] ValueSeparators = [';'];

    private readonly IRoadTileSource _tiles;

    public RoadNameSuggester(IRoadTileSource tiles)
    {
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    public async Task<IReadOnlyList<RoadNameCandidate>> SuggestAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        // 撮影位置は EXIF から読むため、メルカトルで表せない値（極付近・非数）が来ることがある
        if (!RoadTileGeometry.IsRepresentable(latitude, longitude))
        {
            return [];
        }

        var (tileX, tileY) = RoadTileGeometry.ToTileCoordinates(latitude, longitude, TileZoom);
        var tileWidthMeters = RoadTileGeometry.TileWidthMeters(latitude, TileZoom);
        var nearest = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (var (x, y) in SelectTiles(tileX, tileY, tileWidthMeters))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tile = await _tiles.GetTileAsync(x, y, cancellationToken).ConfigureAwait(false);
            if (tile?.GetLayer(LayerName) is not { } layer)
            {
                continue;
            }

            // 地点が、このタイルの外にあることもある（隣接タイルの道路を調べるとき）。座標はタイルの単位で、タイルの外へはみ出してよい
            var extent = (double)layer.Extent;
            var metersPerUnit = tileWidthMeters / extent;
            var pointX = (tileX - x) * extent;
            var pointY = (tileY - y) * extent;

            foreach (var feature in layer.Features)
            {
                Collect(feature, pointX, pointY, metersPerUnit, nearest);
            }
        }

        return
        [
            .. nearest.Values
                .OrderBy(candidate => candidate.DistanceMeters)
                .ThenBy(candidate => candidate.Name ?? candidate.Ref, StringComparer.Ordinal)
                .Take(MaxCandidates)
                .Select(candidate => new RoadNameCandidate(candidate.Name, candidate.Ref, candidate.DistanceMeters)),
        ];
    }

    /// <summary>
    /// 地点を含むタイルと、地点が探索半径以内で縁に近い側の隣接タイル（斜めを含む）。地点を含むタイルが先頭
    /// </summary>
    private static List<(int X, int Y)> SelectTiles(double tileX, double tileY, double tileWidthMeters)
    {
        var x = (int)Math.Floor(tileX);
        var y = (int)Math.Floor(tileY);
        var margin = SearchRadiusMeters / tileWidthMeters;
        var inTileX = tileX - x;
        var inTileY = tileY - y;

        var columns = new List<int> { 0 };
        if (inTileX < margin)
        {
            columns.Add(-1);
        }
        else if (inTileX > 1d - margin)
        {
            columns.Add(1);
        }

        var rows = new List<int> { 0 };
        if (inTileY < margin)
        {
            rows.Add(-1);
        }
        else if (inTileY > 1d - margin)
        {
            rows.Add(1);
        }

        var tileCount = 1 << TileZoom;
        var tiles = new List<(int X, int Y)>();
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var candidateX = x + column;
                var candidateY = y + row;
                if (candidateX >= 0 && candidateX < tileCount && candidateY >= 0 && candidateY < tileCount)
                {
                    tiles.Add((candidateX, candidateY));
                }
            }
        }

        return tiles;
    }

    private static void Collect(
        VectorTileFeature feature,
        double pointX,
        double pointY,
        double metersPerUnit,
        Dictionary<string, Accumulator> nearest)
    {
        if (feature.GeometryType != VectorGeometryType.LineString
            || feature.GetString("class") is not { } roadClass
            || !RoadClasses.Contains(roadClass))
        {
            return;
        }

        var names = SplitValues(feature.GetString("name:ja") ?? feature.GetString("name"));
        var routes = SplitValues(feature.GetString("ref"));
        if (names.Length == 0 && routes.Length == 0)
        {
            return;
        }

        var distance = DistanceMeters(feature, pointX, pointY) * metersPerUnit;
        if (distance > SearchRadiusMeters)
        {
            return;
        }

        if (names.Length == 0)
        {
            // 名称のない路線は、番号を表示側が「路線番号 N」と整える
            var route = JoinRoutes(routes);
            Add(nearest, $"\0ref:{route}", name: null, route, distance);
            return;
        }

        for (var i = 0; i < names.Length; i++)
        {
            // 重なった複数の道路は「名称;名称」「番号;番号」と並ぶ。数が合うときだけ、同じ位置どうしを対応させる
            var route = routes.Length == names.Length
                ? routes[i]
                : (names.Length == 1 ? JoinRoutes(routes) : null);
            Add(nearest, names[i], names[i], route, distance);
        }
    }

    private static void Add(Dictionary<string, Accumulator> nearest, string key, string? name, string? route, double distance)
    {
        if (!nearest.TryGetValue(key, out var existing))
        {
            nearest[key] = new Accumulator(name, route, distance);
            return;
        }

        // 同じ名称の道路は、いちばん近い地物の距離でまとめる。番号は、どれかにあればそれを使う
        nearest[key] = existing with
        {
            Ref = existing.Ref ?? route,
            DistanceMeters = Math.Min(existing.DistanceMeters, distance),
        };
    }

    private static double DistanceMeters(VectorTileFeature feature, double pointX, double pointY)
    {
        var best = double.MaxValue;
        foreach (var part in feature.GeometryParts)
        {
            for (var i = 0; i + 1 < part.Count; i++)
            {
                best = Math.Min(
                    best,
                    RoadTileGeometry.DistanceToSegment(pointX, pointY, part[i].X, part[i].Y, part[i + 1].X, part[i + 1].Y));
            }
        }

        return best;
    }

    private static string[] SplitValues(string? value)
        => value is null
            ? []
            : value.Split(ValueSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // 複数の路線番号は「・」でつなぐ（表示側が「路線番号 53・373」と整える）
    private static string? JoinRoutes(string[] routes) => routes.Length == 0 ? null : string.Join('・', routes);

    private sealed record Accumulator(string? Name, string? Ref, double DistanceMeters);
}
