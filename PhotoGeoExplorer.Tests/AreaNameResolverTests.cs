using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class AreaNameResolverTests
{
    private static readonly GeometryFactory Factory = new();

    [Fact]
    public async Task ResolveAsyncReturnsTheAreaContainingThePoint()
    {
        var source = new FakeAreaDataSource { ["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0) };
        var resolver = new AreaNameResolver(new FakePrefectureLocator("13"), source);

        var areaName = await resolver.ResolveAsync(35.5, 139.5, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(areaName);
        Assert.Equal(("東京都", "千代田区", "丸の内"), (areaName.Prefecture, areaName.City, areaName.Town));
        Assert.False(areaName.SnappedToNearest);
    }

    [Theory]
    // 国外: 候補の都道府県がない
    [InlineData("", false)]
    // データ未導入の都道府県
    [InlineData("13", false)]
    // 導入済みだが、どの小地域にも入らない
    [InlineData("13", true)]
    public async Task ResolveAsyncReturnsNullWhenNoAreaIsFound(string candidates, bool datasetInstalled)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var source = new FakeAreaDataSource();
        if (datasetInstalled)
        {
            source["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0);
        }

        var resolver = new AreaNameResolver(
            new FakePrefectureLocator(candidates.Split(',', StringSplitOptions.RemoveEmptyEntries)),
            source);

        Assert.Null(await resolver.ResolveAsync(35.5, 150.0, CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    // 県境付近: 候補の順序によらず、地点を内包する都道府県で確定する
    [InlineData("13,14", 139.5, "丸の内")]
    [InlineData("14,13", 139.5, "丸の内")]
    [InlineData("13,14", 140.5, "中区")]
    [InlineData("14,13", 140.5, "中区")]
    public async Task ResolveAsyncChoosesThePrefectureContainingThePointNearABorder(
        string candidates,
        double longitude,
        string expectedTown)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var source = new FakeAreaDataSource
        {
            ["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0),
            ["14"] = CreateDataset("14", "神奈川県", "横浜市", "中区", west: 140.0, east: 141.0)
        };
        var resolver = new AreaNameResolver(new FakePrefectureLocator(candidates.Split(',')), source);

        var areaName = await resolver.ResolveAsync(35.5, longitude, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(areaName);
        Assert.Equal(expectedTown, areaName.Town);
    }

    [Theory]
    [InlineData("13,14")]
    [InlineData("14,13")]
    public async Task ResolveAsyncChoosesTheNearestAreaWhenThePointIsInAGapBetweenPrefectures(string candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // 東京側の端から約 9m、神奈川側の端から約 18m の隙間（経度 0.0001 度 ≒ 9m）
        var source = new FakeAreaDataSource
        {
            ["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0),
            ["14"] = CreateDataset("14", "神奈川県", "横浜市", "中区", west: 140.0003, east: 141.0)
        };
        var resolver = new AreaNameResolver(new FakePrefectureLocator(candidates.Split(',')), source);

        var areaName = await resolver.ResolveAsync(35.5, 140.0001, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(areaName);
        Assert.Equal("丸の内", areaName.Town);
        Assert.True(areaName.SnappedToNearest);
    }

    [Theory]
    [InlineData("13,14")]
    [InlineData("14,13")]
    public async Task ResolveAsyncPrefersLandInANeighboringPrefectureOverWaterContainingThePoint(string candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // 東京側は水面。地点は水面の中で、神奈川側の陸まで約 14m（経度 0.00015 度）
        var source = new FakeAreaDataSource
        {
            ["13"] = CreateDataset("13", "東京都", "千代田区", "水面", west: 139.0, east: 140.0, isWater: true),
            ["14"] = CreateDataset("14", "神奈川県", "横浜市", "中区", west: 140.00015, east: 141.0)
        };
        var resolver = new AreaNameResolver(new FakePrefectureLocator(candidates.Split(',')), source);

        var areaName = await resolver.ResolveAsync(35.5, 140.0, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(areaName);
        Assert.Equal("中区", areaName.Town);
        Assert.True(areaName.SnappedToNearest);
    }

    [Theory]
    [InlineData("13,14")]
    [InlineData("14,13")]
    public async Task ResolveAsyncKeepsWaterWhenNoLandIsWithinTheSnapDistanceInAnyPrefecture(string candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // 神奈川側の陸まで約 45m（経度 0.0005 度）
        var source = new FakeAreaDataSource
        {
            ["13"] = CreateDataset("13", "東京都", "千代田区", "水面", west: 139.0, east: 140.0, isWater: true),
            ["14"] = CreateDataset("14", "神奈川県", "横浜市", "中区", west: 140.0005, east: 141.0)
        };
        var resolver = new AreaNameResolver(new FakePrefectureLocator(candidates.Split(',')), source);

        var areaName = await resolver.ResolveAsync(35.5, 140.0, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(areaName);
        Assert.Equal("水面", areaName.Town);
        Assert.False(areaName.SnappedToNearest);
    }

    [Fact]
    public async Task ResolveAsyncSharesAnInFlightLoadEvenWhenMorePrefecturesThanTheCapacityAreRequested()
    {
        var source = new FakeAreaDataSource { HoldLoads = true };
        source["01"] = CreateDataset("01", "A県", "A市", "A町", west: 139.0, east: 140.0);
        source["02"] = CreateDataset("02", "B県", "B市", "B町", west: 139.0, east: 140.0);
        var locator = new FakePrefectureLocator();
        var resolver = new AreaNameResolver(locator, source, cacheCapacity: 1);

        // 読み込みが終わらないうちに、容量（1）を超える A → B → A を要求する
        var pending = new List<Task<AreaName?>>();
        foreach (var code in new[] { "01", "02", "01" })
        {
            locator.Candidates = new[] { code };
            pending.Add(resolver.ResolveAsync(35.5, 139.5, CancellationToken.None));
        }

        Assert.Equal("01,02", string.Join(',', source.LoadedCodes));

        source.ReleaseLoads();
        var results = await Task.WhenAll(pending).ConfigureAwait(true);

        Assert.Equal("A町,B町,A町", string.Join(',', results.Select(result => result?.Town)));
        Assert.Equal("01,02", string.Join(',', source.LoadedCodes));
    }

    [Fact]
    public async Task ResolveAsyncDoesNotReloadPrefecturesWhenAlternatingAcrossABorder()
    {
        var source = new FakeAreaDataSource
        {
            ["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0),
            ["14"] = CreateDataset("14", "神奈川県", "横浜市", "中区", west: 140.0, east: 141.0)
        };
        var resolver = new AreaNameResolver(new FakePrefectureLocator("13", "14"), source);

        for (var i = 0; i < 5; i++)
        {
            await resolver.ResolveAsync(35.5, 139.5, CancellationToken.None).ConfigureAwait(true);
            await resolver.ResolveAsync(35.5, 140.5, CancellationToken.None).ConfigureAwait(true);
        }

        Assert.Equal("13,14", string.Join(',', source.LoadedCodes.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task ResolveAsyncEvictsTheLeastRecentlyUsedPrefectureBeyondTheCapacity()
    {
        var source = new FakeAreaDataSource
        {
            ["01"] = CreateDataset("01", "A県", "A市", "A町", west: 139.0, east: 140.0),
            ["02"] = CreateDataset("02", "B県", "B市", "B町", west: 139.0, east: 140.0),
            ["03"] = CreateDataset("03", "C県", "C市", "C町", west: 139.0, east: 140.0)
        };
        var locator = new FakePrefectureLocator();
        var resolver = new AreaNameResolver(locator, source, cacheCapacity: 2);

        foreach (var code in new[] { "01", "02", "01", "03", "01", "02" })
        {
            locator.Candidates = new[] { code };
            await resolver.ResolveAsync(35.5, 139.5, CancellationToken.None).ConfigureAwait(true);
        }

        // 01 は使われ続けるので保持される。03 を読んだ時点で 02 が追い出され、最後の 02 で読み直す
        Assert.Equal("01,02,03,02", string.Join(',', source.LoadedCodes));
    }

    [Fact]
    public async Task ResolveAsyncRetriesLoadingAfterAPrefectureIsInstalledLater()
    {
        var source = new FakeAreaDataSource();
        var resolver = new AreaNameResolver(new FakePrefectureLocator("13"), source);

        Assert.Null(await resolver.ResolveAsync(35.5, 139.5, CancellationToken.None).ConfigureAwait(true));
        source["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0);
        var areaName = await resolver.ResolveAsync(35.5, 139.5, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(areaName);
        Assert.Equal("丸の内", areaName.Town);
    }

    [Fact]
    public async Task ResolveAsyncPropagatesLoadFailuresAndRetriesOnTheNextCall()
    {
        var source = new FakeAreaDataSource { Failure = new IOException("broken data pack") };
        var resolver = new AreaNameResolver(new FakePrefectureLocator("13"), source);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => resolver.ResolveAsync(35.5, 139.5, CancellationToken.None)).ConfigureAwait(true);
        Assert.Equal("broken data pack", exception.Message);

        source.Failure = null;
        source["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0);
        Assert.NotNull(await resolver.ResolveAsync(35.5, 139.5, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ResolveAsyncSharesOneLoadBetweenConcurrentCallsAndCancellationDoesNotStopIt()
    {
        var source = new FakeAreaDataSource { HoldLoads = true };
        source["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0);
        var resolver = new AreaNameResolver(new FakePrefectureLocator("13"), source);
        using var cancellation = new CancellationTokenSource();

        var canceled = resolver.ResolveAsync(35.5, 139.5, cancellation.Token);
        var other = resolver.ResolveAsync(35.5, 139.5, CancellationToken.None);
        await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled).ConfigureAwait(true);

        source.ReleaseLoads();
        var areaName = await other.ConfigureAwait(true);

        Assert.NotNull(areaName);
        Assert.Single(source.LoadedCodes);
        Assert.False(source.LastLoadToken.CanBeCanceled);
    }

    [Fact]
    public async Task ResolveAsyncThrowsWhenAlreadyCanceled()
    {
        var source = new FakeAreaDataSource { ["13"] = CreateDataset("13", "東京都", "千代田区", "丸の内", west: 139.0, east: 140.0) };
        var resolver = new AreaNameResolver(new FakePrefectureLocator("13"), source);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveAsync(35.5, 139.5, cancellation.Token)).ConfigureAwait(true);
        Assert.Empty(source.LoadedCodes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConstructorRejectsANonPositiveCacheCapacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AreaNameResolver(new FakePrefectureLocator(), new FakeAreaDataSource(), capacity));
    }

    private static AreaDataset CreateDataset(
        string code,
        string prefecture,
        string city,
        string town,
        double west,
        double east,
        bool isWater = false)
    {
        var polygon = Factory.CreatePolygon(new[]
        {
            new Coordinate(west, 35.0),
            new Coordinate(east, 35.0),
            new Coordinate(east, 36.0),
            new Coordinate(west, 36.0),
            new Coordinate(west, 35.0)
        });
        return new AreaDataset(code, 2020, new[] { new AreaRegion($"{code}000000", prefecture, city, town, isWater, polygon) });
    }

    private sealed class FakePrefectureLocator : IPrefectureLocator
    {
        public FakePrefectureLocator(params string[] candidates) => Candidates = candidates;

        public IReadOnlyList<string> Candidates { get; set; }

        public IReadOnlyList<string> FindCandidates(double latitude, double longitude) => Candidates;
    }

    private sealed class FakeAreaDataSource : IAreaDataSource
    {
        private readonly Dictionary<string, AreaDataset> _datasets = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> LoadedCodes { get; } = new();

        public IOException? Failure { get; set; }

        public bool HoldLoads { get; init; }

        public CancellationToken LastLoadToken { get; private set; }

        public AreaDataset this[string prefectureCode]
        {
            set => _datasets[prefectureCode] = value;
        }

        public void ReleaseLoads() => _release.TrySetResult();

        public async Task<AreaDataset?> LoadAsync(string prefectureCode, CancellationToken cancellationToken)
        {
            LoadedCodes.Add(prefectureCode);
            LastLoadToken = cancellationToken;
            if (HoldLoads)
            {
                await _release.Task.ConfigureAwait(false);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return _datasets.GetValueOrDefault(prefectureCode);
        }
    }
}
