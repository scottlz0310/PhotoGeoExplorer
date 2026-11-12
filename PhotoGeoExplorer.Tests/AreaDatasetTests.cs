using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class AreaDatasetTests
{
    // 緯度 35 度付近の合成データ。1 度あたりのメートル数は、判定と同じ換算を使う
    private const double BaseLatitude = 35.0;
    private const double BaseLongitude = 135.0;
    private const double MetersPerDegree = 111_320;
    private static readonly GeometryFactory Factory = new();

    // 西から順に、A 町（陸）｜ 幅 40m の隙間 ｜ B 町（陸）｜ 水面。各区画は東西 1000m、南北 1000m
    private static readonly AreaDataset Dataset = new(
        "99",
        2020,
        new[]
        {
            CreateRegion("99000001", "A町", isWater: false, westMeters: 0, eastMeters: 1000),
            CreateRegion("99000002", "B町", isWater: false, westMeters: 1040, eastMeters: 2040),
            CreateRegion("99000003", "水面", isWater: true, westMeters: 2040, eastMeters: 3040)
        });

    [Theory]
    // 内包
    [InlineData(500, 500, "A町", false)]
    [InlineData(1500, 500, "B町", false)]
    // 境界上（頂点・辺）は、その小地域として扱う
    [InlineData(0, 0, "A町", false)]
    [InlineData(1000, 500, "A町", false)]
    // 隙間: 30m 以内なら最近傍へ寄せる
    [InlineData(1010, 500, "A町", true)]
    [InlineData(1030, 500, "B町", true)]
    [InlineData(-29, 500, "A町", true)]
    [InlineData(500, 1029, "A町", true)]
    // 水面: 30m 以内に陸の町丁字があれば陸を優先し、なければ水面のまま
    [InlineData(2060, 500, "B町", true)]
    [InlineData(2500, 500, "水面", false)]
    // 陸と水面の境界上は陸
    [InlineData(2040, 500, "B町", false)]
    // 水面の外側 30m 以内で、陸が 30m 以内にない
    [InlineData(3060, 500, "水面", true)]
    public void FindReturnsTheExpectedArea(double eastMeters, double northMeters, string expectedTown, bool expectedSnapped)
    {
        var match = Dataset.Find(ToLatitude(northMeters), ToLongitude(eastMeters));

        Assert.NotNull(match);
        Assert.Equal(expectedTown, match.Name.Town);
        Assert.Equal(expectedSnapped, match.Name.SnappedToNearest);
        Assert.Equal(2020, match.Name.CensusYear);
        Assert.Equal("合成県", match.Name.Prefecture);
        Assert.Equal("合成市", match.Name.City);
    }

    [Theory]
    // どの小地域からも 30m を超えて離れている
    [InlineData(-40, 500)]
    [InlineData(500, 1040)]
    [InlineData(3080, 500)]
    [InlineData(500, -40)]
    // 斜め方向: 東西・南北とも 25m ずつ離れると、角からの距離は約 35m
    [InlineData(-25, -25)]
    // 遠く離れた地点
    [InlineData(500_000, 500_000)]
    public void FindReturnsNullWhenNoAreaIsWithinTheSnapDistance(double eastMeters, double northMeters)
    {
        Assert.Null(Dataset.Find(ToLatitude(northMeters), ToLongitude(eastMeters)));
    }

    [Theory]
    [InlineData(1010, 10)]
    [InlineData(1025, 15)]
    [InlineData(-20, 20)]
    public void FindReportsTheDistanceToTheSnappedAreaInMeters(double eastMeters, double expectedDistanceMeters)
    {
        var match = Dataset.Find(ToLatitude(500), ToLongitude(eastMeters));

        Assert.NotNull(match);
        Assert.Equal(expectedDistanceMeters, match.DistanceMeters, tolerance: 0.5);
    }

    [Fact]
    public void FindReportsZeroDistanceForAContainedPoint()
    {
        var match = Dataset.Find(ToLatitude(500), ToLongitude(500));

        Assert.NotNull(match);
        Assert.Equal(0, match.DistanceMeters);
    }

    [Fact]
    public void FindReturnsNullForAnEmptyDataset()
    {
        var dataset = new AreaDataset("99", 2020, Array.Empty<AreaRegion>());

        Assert.Null(dataset.Find(BaseLatitude, BaseLongitude));
    }

    [Fact]
    public void FindPrefersTheNearestLandWhenSeveralAreWithinTheSnapDistance()
    {
        // 幅 40m の隙間の中で、A 町まで 25m、B 町まで 15m
        var match = Dataset.Find(ToLatitude(500), ToLongitude(1025));

        Assert.NotNull(match);
        Assert.Equal("B町", match.Name.Town);
    }

    private static AreaRegion CreateRegion(string keyCode, string town, bool isWater, double westMeters, double eastMeters)
    {
        var polygon = Factory.CreatePolygon(new[]
        {
            new Coordinate(ToLongitude(westMeters), ToLatitude(0)),
            new Coordinate(ToLongitude(eastMeters), ToLatitude(0)),
            new Coordinate(ToLongitude(eastMeters), ToLatitude(1000)),
            new Coordinate(ToLongitude(westMeters), ToLatitude(1000)),
            new Coordinate(ToLongitude(westMeters), ToLatitude(0))
        });
        return new AreaRegion(keyCode, "合成県", "合成市", town, isWater, polygon);
    }

    private static double ToLatitude(double northMeters) => BaseLatitude + (northMeters / MetersPerDegree);

    // 経度 1 度の長さは緯度で変わる。区画の南端（BaseLatitude）での長さを基準にする
    private static double ToLongitude(double eastMeters)
        => BaseLongitude + (eastMeters / (MetersPerDegree * Math.Cos(BaseLatitude * Math.PI / 180)));
}
