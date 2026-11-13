using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.RoadNames;
using PhotoMapStudio.Tiles.Vector;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class RoadNameSuggesterTests
{
    // 京都市内のタイル（z14）。地点を、このタイルの中心（4096 単位の座標で 2048, 2048）に置く
    private const int TileX = 14370;
    private const int TileY = 6489;
    private const double Extent = 4096d;

    [Theory]
    [InlineData(0d, true)]
    [InlineData(30d, true)]
    [InlineData(49d, true)]
    [InlineData(51d, false)]
    [InlineData(60d, false)]
    public async Task OnlyRoadsWithinTheSearchRadiusAreCandidates(double offsetMeters, bool expected)
    {
        var tiles = new FakeRoadTileSource { [(TileX, TileY)] = Tile(Road("四条通", "primary", HorizontalLine(offsetMeters))) };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        Assert.Equal(expected, candidates.Count == 1);
        if (expected)
        {
            Assert.Equal("四条通", candidates[0].Name);
            Assert.InRange(candidates[0].DistanceMeters, offsetMeters - 0.5, offsetMeters + 0.5);
        }
    }

    [Fact]
    public async Task RoadsAreGroupedByNameWithTheShortestDistanceAndOrderedNearestFirst()
    {
        var tiles = new FakeRoadTileSource
        {
            [(TileX, TileY)] = Tile(
                Road("河原町通", "primary", HorizontalLine(40)),
                Road("四条通", "primary", HorizontalLine(20)),
                Road("四条通", "primary", HorizontalLine(10)),
                Road("寺町通", "minor", HorizontalLine(30))),
        };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        string[] expected = ["四条通", "寺町通", "河原町通"];
        Assert.Equal(expected, candidates.Select(candidate => candidate.Name));
        Assert.InRange(candidates[0].DistanceMeters, 9.5, 10.5);
    }

    [Fact]
    public async Task AtMostThreeCandidatesAreReturned()
    {
        var tiles = new FakeRoadTileSource
        {
            [(TileX, TileY)] = Tile(
                Enumerable.Range(1, 5).Select(index => Road($"道{index}", "minor", HorizontalLine(index * 5))).ToArray()),
        };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        string[] expected = ["道1", "道2", "道3"];
        Assert.Equal(expected, candidates.Select(candidate => candidate.Name));
    }

    [Theory]
    [InlineData("丸の内仲通り", "Marunouchi Naka-dori", "丸の内仲通り")]
    [InlineData(null, "Marunouchi Naka-dori", "Marunouchi Naka-dori")]
    [InlineData("丸の内仲通り", null, "丸の内仲通り")]
    public async Task TheJapaneseNameIsPreferredToTheDefaultName(string? nameJa, string? name, string expected)
    {
        var tiles = new FakeRoadTileSource { [(TileX, TileY)] = Tile(Road(name, "tertiary", HorizontalLine(10), nameJa: nameJa)) };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        Assert.Equal(expected, Assert.Single(candidates).Name);
    }

    [Theory]
    [InlineData("国道53号", "53;373", "国道53号", "53・373")]
    [InlineData("山の手通り", "203", "山の手通り", "203")]
    [InlineData("丸山通", null, "丸山通", null)]
    [InlineData(null, "203", null, "203")]
    [InlineData(null, "203;1172", null, "203・1172")]
    public async Task TheNameAndTheRouteNumbersAreReturnedWithoutInferringTheRoadType(
        string? name,
        string? route,
        string? expectedName,
        string? expectedRoute)
    {
        var tiles = new FakeRoadTileSource { [(TileX, TileY)] = Tile(Road(name, "secondary", HorizontalLine(10), route: route)) };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        var candidate = Assert.Single(candidates);
        Assert.Equal(expectedName, candidate.Name);
        Assert.Equal(expectedRoute, candidate.Ref);
    }

    [Fact]
    public async Task OverlappingRoadsAreSplitAndTheirRouteNumbersArePairedByPosition()
    {
        var tiles = new FakeRoadTileSource
        {
            [(TileX, TileY)] = Tile(Road("山の手通り;栗谷小西谷1号線", "minor", HorizontalLine(10), route: "203;1172")),
        };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        Assert.Equal(
            new[] { ("山の手通り", "203"), ("栗谷小西谷1号線", "1172") },
            candidates.Select(candidate => (candidate.Name!, candidate.Ref!)));
    }

    [Fact]
    public async Task RouteNumbersAreNotPairedWhenTheNumberOfNamesAndRoutesDiffer()
    {
        var tiles = new FakeRoadTileSource
        {
            [(TileX, TileY)] = Tile(Road("山の手通り;栗谷小西谷1号線", "minor", HorizontalLine(10), route: "203")),
        };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Null(candidate.Ref));
    }

    [Theory]
    [InlineData("motorway", true)]
    [InlineData("trunk", true)]
    [InlineData("primary", true)]
    [InlineData("secondary", true)]
    [InlineData("tertiary", true)]
    [InlineData("minor", true)]
    [InlineData("service", true)]
    [InlineData("path", false)]
    [InlineData("track", false)]
    [InlineData("motorway_construction", false)]
    [InlineData("ferry", false)]
    public async Task OnlyRoadClassesForVehiclesAndPeopleAreCandidates(string roadClass, bool expected)
    {
        var tiles = new FakeRoadTileSource { [(TileX, TileY)] = Tile(Road("名称", roadClass, HorizontalLine(10))) };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        Assert.Equal(expected, candidates.Count == 1);
    }

    [Fact]
    public async Task FeaturesWithoutANameOrARouteNumberAreIgnored()
    {
        var tiles = new FakeRoadTileSource { [(TileX, TileY)] = Tile(Road(null, "minor", HorizontalLine(10))) };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        Assert.Empty(candidates);
    }

    [Theory]
    [InlineData(VectorGeometryType.Point)]
    [InlineData(VectorGeometryType.Polygon)]
    [InlineData(VectorGeometryType.Unknown)]
    public async Task OnlyLinesAreCandidates(VectorGeometryType geometryType)
    {
        var tiles = new FakeRoadTileSource
        {
            [(TileX, TileY)] = Tile(Road("名称", "minor", HorizontalLine(10), geometryType: geometryType)),
        };

        var candidates = await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task ATileWithoutTheRoadLayerContributesNothing()
    {
        var tiles = new FakeRoadTileSource
        {
            [(TileX, TileY)] = new VectorTile { Layers = [new VectorTileLayer { Name = "water" }] },
        };

        Assert.Empty(await Suggest(tiles, 0.5, 0.5).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(0.5, 0.5, new[] { 0 })]
    [InlineData(0.99, 0.5, new[] { 0, 1 })]
    [InlineData(0.01, 0.5, new[] { 0, -1 })]
    public async Task NeighboringTilesAreReadOnlyWhenThePointIsNearTheirEdge(double inTileX, double inTileY, int[] columns)
    {
        var tiles = new FakeRoadTileSource();

        await Suggest(tiles, inTileX, inTileY).ConfigureAwait(true);

        Assert.Equal(
            columns.Select(column => (TileX + column, TileY)).Order().ToArray(),
            tiles.Requests.Order().ToArray());
    }

    [Fact]
    public async Task ThePointTileIsReadFirstAndACornerReadsFourTiles()
    {
        var tiles = new FakeRoadTileSource();

        await Suggest(tiles, 0.99, 0.99).ConfigureAwait(true);

        Assert.Equal((TileX, TileY), tiles.Requests[0]);
        Assert.Equal(
            new[] { (TileX, TileY), (TileX + 1, TileY), (TileX, TileY + 1), (TileX + 1, TileY + 1) }.Order().ToArray(),
            tiles.Requests.Order().ToArray());
    }

    [Fact]
    public async Task ARoadInTheNeighboringTileIsFoundFromThePointOutsideThatTile()
    {
        const double inTileX = 0.99;
        var tiles = new FakeRoadTileSource
        {
            // 隣（東）のタイルの西の縁に沿った縦線。地点からは、タイルの外へ約 0.01 タイル分
            [(TileX + 1, TileY)] = Tile(Road("東の通り", "minor", new[] { (0, 0), (0, 4096) })),
        };

        var candidates = await Suggest(tiles, inTileX, 0.5).ConfigureAwait(true);

        var candidate = Assert.Single(candidates);
        Assert.Equal("東の通り", candidate.Name);
        var expected = (1d - inTileX) * Extent * MetersPerUnit();
        Assert.InRange(candidate.DistanceMeters, expected - 0.5, expected + 0.5);
    }

    [Fact]
    public async Task AMissingTileIsSkippedAndTheOthersAreStillUsed()
    {
        var tiles = new FakeRoadTileSource
        {
            [(TileX, TileY)] = null,
            [(TileX + 1, TileY)] = Tile(Road("東の通り", "minor", new[] { (0, 0), (0, 4096) })),
        };

        var candidates = await Suggest(tiles, 0.99, 0.5).ConfigureAwait(true);

        Assert.Equal("東の通り", Assert.Single(candidates).Name);
    }

    [Theory]
    [InlineData(90d, 135d)]
    [InlineData(-89d, 135d)]
    [InlineData(double.NaN, 135d)]
    [InlineData(35d, double.PositiveInfinity)]
    [InlineData(35d, 181d)]
    public async Task ACoordinateThatCannotBeProjectedHasNoCandidatesAndReadsNoTile(double latitude, double longitude)
    {
        var tiles = new FakeRoadTileSource();
        var suggester = new RoadNameSuggester(tiles);

        var candidates = await suggester.SuggestAsync(latitude, longitude, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(candidates);
        Assert.Empty(tiles.Requests);
    }

    [Fact]
    public async Task CancellationIsPropagatedFromTheTileSource()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);
        var tiles = new FakeRoadTileSource();
        var (latitude, longitude) = PointAt(0.5, 0.5);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new RoadNameSuggester(tiles).SuggestAsync(latitude, longitude, cancellation.Token)).ConfigureAwait(true);
    }

    [Fact]
    public async Task FailuresOfTheTileSourceAreNotHidden()
    {
        var tiles = new FakeRoadTileSource { Failure = new HttpRequestException("offline") };
        var (latitude, longitude) = PointAt(0.5, 0.5);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => new RoadNameSuggester(tiles).SuggestAsync(latitude, longitude, CancellationToken.None)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(35d)]
    [InlineData(43.3d)]
    public void MetersPerUnitAtTheTileMatchesTheKnownWebMercatorScale(double latitude)
    {
        // z14 のタイル幅（メートル）は、赤道での 2445 m ほどに、緯度の余弦を掛けたもの
        var expected = 2445.98490512564 * Math.Cos(latitude * Math.PI / 180d);

        Assert.Equal(expected, RoadTileGeometry.TileWidthMeters(latitude, 14), precision: 3);
    }

    private static Task<IReadOnlyList<RoadNameCandidate>> Suggest(FakeRoadTileSource tiles, double inTileX, double inTileY)
    {
        var (latitude, longitude) = PointAt(inTileX, inTileY);
        return new RoadNameSuggester(tiles).SuggestAsync(latitude, longitude, CancellationToken.None);
    }

    // タイル（TileX, TileY）の中の位置（0〜1）を、緯度経度にする
    private static (double Latitude, double Longitude) PointAt(double inTileX, double inTileY)
    {
        var tiles = Math.Pow(2, 14);
        var longitude = ((TileX + inTileX) / tiles * 360d) - 180d;
        var latitude = Math.Atan(Math.Sinh(Math.PI * (1d - (2d * (TileY + inTileY) / tiles)))) * 180d / Math.PI;
        return (latitude, longitude);
    }

    private static double MetersPerUnit()
        => RoadTileGeometry.TileWidthMeters(PointAt(0.5, 0.5).Latitude, 14) / Extent;

    // 地点（タイルの中心）から、南へ指定のメートルだけ離れた、タイルを横切る線
    private static (int X, int Y)[] HorizontalLine(double offsetMeters)
    {
        var y = (int)Math.Round((Extent / 2d) + (offsetMeters / MetersPerUnit()));
        return [(0, y), (4096, y)];
    }

    private static VectorTile Tile(params VectorTileFeature[] features)
        => new() { Layers = [new VectorTileLayer { Name = "transportation_name", Extent = 4096, Features = features }] };

    private static VectorTileFeature Road(
        string? name,
        string roadClass,
        (int X, int Y)[] line,
        string? nameJa = null,
        string? route = null,
        VectorGeometryType geometryType = VectorGeometryType.LineString)
    {
        var properties = new Dictionary<string, object> { ["class"] = roadClass };
        if (name is not null)
        {
            properties["name"] = name;
        }

        if (nameJa is not null)
        {
            properties["name:ja"] = nameJa;
        }

        if (route is not null)
        {
            properties["ref"] = route;
        }

        return new VectorTileFeature
        {
            GeometryType = geometryType,
            Properties = properties,
            GeometryParts = [line.Select(point => new VectorPoint(point.X, point.Y)).ToArray()],
        };
    }

    private sealed class FakeRoadTileSource : IRoadTileSource
    {
        private readonly Dictionary<(int X, int Y), VectorTile?> _tiles = new();

        public List<(int X, int Y)> Requests { get; } = new();

        public Exception? Failure { get; init; }

        public VectorTile? this[(int X, int Y) key]
        {
            set => _tiles[key] = value;
        }

        public Task<VectorTile?> GetTileAsync(int x, int y, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((x, y));
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(_tiles.GetValueOrDefault((x, y)));
        }
    }
}
