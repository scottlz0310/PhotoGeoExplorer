using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Index.Strtree;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 1 都道府県分の小地域ポリゴン。座標は緯度経度（X = 経度、Y = 緯度）
/// </summary>
internal sealed class AreaDataset
{
    /// <summary>どのポリゴンにも入らない地点を、最近傍へ寄せる距離の上限</summary>
    internal const double SnapDistanceMeters = 30;

    private const double MetersPerDegree = 111_320;

    private readonly STRtree<AreaRegion> _index = new();

    public AreaDataset(string prefectureCode, int censusYear, IReadOnlyCollection<AreaRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        PrefectureCode = prefectureCode;
        CensusYear = censusYear;
        RegionCount = regions.Count;
        foreach (var region in regions)
        {
            _index.Insert(region.Geometry.EnvelopeInternal, region);
        }

        // 最初の検索で構築されると、複数スレッドからの同時検索で競合する
        _index.Build();
    }

    public string PrefectureCode { get; }

    public int CensusYear { get; }

    public int RegionCount { get; }

    /// <summary>
    /// 地点の小地域を求める。陸の町丁字への内包を最優先し、次に 30m 以内の陸、水面の順に選ぶ
    /// </summary>
    public AreaMatch? Find(double latitude, double longitude)
    {
        var latitudeMargin = SnapDistanceMeters / MetersPerDegree;
        var longitudeScale = Math.Cos(latitude * Math.PI / 180);
        var longitudeMargin = latitudeMargin / Math.Max(longitudeScale, 1e-6);
        var searchArea = new Envelope(
            longitude - longitudeMargin,
            longitude + longitudeMargin,
            latitude - latitudeMargin,
            latitude + latitudeMargin);
        var candidates = _index.Query(searchArea);
        if (candidates.Count == 0)
        {
            return null;
        }

        var point = candidates[0].Geometry.Factory.CreatePoint(new Coordinate(longitude, latitude));
        var containing = candidates.Where(region => region.Prepared.Covers(point)).ToList();
        var land = containing.Find(region => !region.IsWater);
        if (land is not null)
        {
            return new AreaMatch(land.ToAreaName(snappedToNearest: false, CensusYear), 0);
        }

        // 経度方向を縮めて、その地点での局所的なメートル換算で距離を測る
        var toLocalScale = AffineTransformation.ScaleInstance(longitudeScale, 1);
        var localPoint = toLocalScale.Transform(point);
        var nearby = candidates
            .Select(region => new AreaMatchCandidate(
                region,
                toLocalScale.Transform(region.Geometry).Distance(localPoint) * MetersPerDegree))
            .Where(candidate => candidate.DistanceMeters <= SnapDistanceMeters)
            .OrderBy(candidate => candidate.DistanceMeters)
            .ToList();
        var nearestLand = nearby.Find(candidate => !candidate.Region.IsWater);
        if (nearestLand is not null)
        {
            return ToSnappedMatch(nearestLand);
        }

        if (containing.Count > 0)
        {
            return new AreaMatch(containing[0].ToAreaName(snappedToNearest: false, CensusYear), 0);
        }

        return nearby.Count > 0 ? ToSnappedMatch(nearby[0]) : null;
    }

    private AreaMatch ToSnappedMatch(AreaMatchCandidate candidate)
        => new(candidate.Region.ToAreaName(snappedToNearest: true, CensusYear), candidate.DistanceMeters);

    private sealed record AreaMatchCandidate(AreaRegion Region, double DistanceMeters);
}
