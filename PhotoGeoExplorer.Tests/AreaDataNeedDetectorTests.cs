using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class AreaDataNeedDetectorTests
{
    public static TheoryData<string[], string[], string[]> Cases => new()
    {
        // 候補, 導入済み, 期待（取得が要る都道府県）
        { ["31"], [], ["31"] },
        { ["31"], ["31"], [] },
        { ["31"], ["13"], ["31"] },
        { [], [], [] },
        { [], ["31"], [] },
        // 県境付近: どれかが導入済みなら、導入済みのデータで判定できる可能性があるため取得は要らない
        { ["31", "32"], ["31"], [] },
        { ["31", "32"], ["32"], [] },
        { ["31", "32"], [], ["31", "32"] },
        { ["31", "32"], ["13"], ["31", "32"] }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FindMissingAsyncReturnsTheCandidatesOnlyWhenNoneIsInstalled(string[] candidates, string[] installed, string[] expected)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(installed);
        var store = new FakeAreaDataStore();
        foreach (var code in installed)
        {
            store.Add(new AreaDataPackInfo(code, "x", 2020, 1));
        }

        var detector = new AreaDataNeedDetector(new FixedLocator(candidates), store);

        var missing = await detector.FindMissingAsync(35.0, 135.0, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(expected, missing);
    }

    [Fact]
    public async Task FindMissingAsyncDoesNotReadTheInstalledListWhenThereAreNoCandidates()
    {
        var store = new FakeAreaDataStore();
        var detector = new AreaDataNeedDetector(new FixedLocator([]), store);

        await detector.FindMissingAsync(0, 0, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, store.ListCallCount);
    }

    [Fact]
    public async Task FindMissingAsyncPropagatesAStoreFailure()
    {
        var store = new FakeAreaDataStore { Failure = FakeAreaDataStore.NewIOException() };
        var detector = new AreaDataNeedDetector(new FixedLocator(["31"]), store);

        await Assert.ThrowsAsync<System.IO.IOException>(
            () => detector.FindMissingAsync(35.0, 135.0, CancellationToken.None)).ConfigureAwait(true);
    }

    private sealed class FixedLocator(IReadOnlyList<string> candidates) : IPrefectureLocator
    {
        public IReadOnlyList<string> FindCandidates(double latitude, double longitude) => candidates.ToList();
    }
}
