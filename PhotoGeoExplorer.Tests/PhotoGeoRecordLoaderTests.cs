using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaTree;
using PhotoGeoExplorer.Services.Export;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class PhotoGeoRecordLoaderTests
{
    private static readonly DateTimeOffset TakenAt = new(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));
    private static readonly AreaName Higashi = new("312010870", "鳥取県", "鳥取市", "東品治町", SnappedToNearest: false, CensusYear: 2020);

    [Fact]
    public async Task LoadAsyncReadsTheDateTheLocationAndTheArea()
    {
        var loader = new PhotoGeoRecordLoader(
            (_, _) => Task.FromResult<PhotoMetadata?>(new PhotoMetadata(TakenAt, null, null, 35.49, 134.22, hasGpsData: true)),
            new FakeResolver { Result = Higashi });

        var records = await loader.LoadAsync(["/p/a.jpg"], null, CancellationToken.None).ConfigureAwait(true);

        var record = Assert.Single(records);
        Assert.Equal(("/p/a.jpg", PhotoAreaKind.Resolved), (record.FilePath, record.Kind));
        Assert.Equal(TakenAt, record.TakenAt);
        Assert.Equal(new PhotoCoordinate(35.49, 134.22), record.Coordinate);
        Assert.Equal(Higashi, record.Area);
    }

    [Fact]
    public async Task LoadAsyncKeepsTheDateOfAPhotoWithoutAValidLocation()
    {
        var loader = new PhotoGeoRecordLoader(
            (_, _) => Task.FromResult<PhotoMetadata?>(new PhotoMetadata(TakenAt, null, null, 0, 0, hasGpsData: true)),
            new FakeResolver { Result = Higashi });

        var record = Assert.Single(await loader.LoadAsync(["/p/a.jpg"], null, CancellationToken.None).ConfigureAwait(true));

        // 測位に失敗した（0, 0）位置は、位置情報なし。字地名は引かない
        Assert.Equal((PhotoAreaKind.NoLocation, TakenAt), (record.Kind, record.TakenAt));
        Assert.Null(record.Coordinate);
        Assert.Null(record.Area);
    }

    [Fact]
    public async Task LoadAsyncTreatsAPhotoWhoseMetadataCannotBeReadAsHavingNoLocation()
    {
        var loader = new PhotoGeoRecordLoader((_, _) => Task.FromResult<PhotoMetadata?>(null), new FakeResolver { Result = Higashi });

        var record = Assert.Single(await loader.LoadAsync(["/p/a.jpg"], null, CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(PhotoAreaKind.NoLocation, record.Kind);
        Assert.Null(record.TakenAt);
    }

    [Fact]
    public async Task LoadAsyncMarksALocationWithoutAreaDataAsNoAreaData()
    {
        var loader = new PhotoGeoRecordLoader(
            (_, _) => Task.FromResult<PhotoMetadata?>(new PhotoMetadata(TakenAt, null, null, 35.68, 139.76, hasGpsData: true)),
            new FakeResolver { Result = null });

        var record = Assert.Single(await loader.LoadAsync(["/p/a.jpg"], null, CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(PhotoAreaKind.NoAreaData, record.Kind);
        Assert.Equal(new PhotoCoordinate(35.68, 139.76), record.Coordinate);
    }

    [Fact]
    public async Task LoadAsyncReturnsTheRecordsInTheOrderOfTheInput()
    {
        var delays = new Dictionary<string, int> { ["/p/0.jpg"] = 60, ["/p/1.jpg"] = 30, ["/p/2.jpg"] = 0 };
        var loader = new PhotoGeoRecordLoader(
            async (path, token) =>
            {
                await Task.Delay(delays[path], token).ConfigureAwait(false);
                return new PhotoMetadata(TakenAt, null, null, 35, 134, hasGpsData: true);
            },
            new FakeResolver { Result = Higashi });

        var records = await loader.LoadAsync(["/p/0.jpg", "/p/1.jpg", "/p/2.jpg"], null, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(["/p/0.jpg", "/p/1.jpg", "/p/2.jpg"], records.Select(record => record.FilePath));
    }

    [Fact]
    public async Task LoadAsyncReportsTheNumberOfReadPhotos()
    {
        var loader = new PhotoGeoRecordLoader((_, _) => Task.FromResult<PhotoMetadata?>(null), new FakeResolver(), maxDegreeOfParallelism: 1);
        var reported = new ConcurrentQueue<int>();

        await loader.LoadAsync(["/p/0.jpg", "/p/1.jpg", "/p/2.jpg"], new SynchronousProgress(reported.Enqueue), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal([1, 2, 3], reported.Order());
    }

    [Fact]
    public async Task LoadAsyncOfNoPhotosReturnsNothing()
    {
        var loader = new PhotoGeoRecordLoader((_, _) => Task.FromResult<PhotoMetadata?>(null), new FakeResolver());

        Assert.Empty(await loader.LoadAsync([], null, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task LoadAsyncStopsWhenCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        var loader = new PhotoGeoRecordLoader(
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return null;
            },
            new FakeResolver(),
            maxDegreeOfParallelism: 2);
        var run = loader.LoadAsync(["/p/0.jpg", "/p/1.jpg", "/p/2.jpg"], null, cancellation.Token);

        await cancellation.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run).ConfigureAwait(true);
    }

    [Fact]
    public async Task LoadAsyncPropagatesAFailureToReadTheAreaData()
    {
        var loader = new PhotoGeoRecordLoader(
            (_, _) => Task.FromResult<PhotoMetadata?>(new PhotoMetadata(TakenAt, null, null, 35, 134, hasGpsData: true)),
            new FakeResolver { Failure = new IOException("broken area data") });

        var exception = await Assert.ThrowsAsync<IOException>(() => loader.LoadAsync(["/p/a.jpg"], null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal("broken area data", exception.Message);
    }

    [Fact]
    public void ConstructorRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new PhotoGeoRecordLoader(null!, new FakeResolver()));
        Assert.Throws<ArgumentNullException>(() => new PhotoGeoRecordLoader((_, _) => Task.FromResult<PhotoMetadata?>(null), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhotoGeoRecordLoader((_, _) => Task.FromResult<PhotoMetadata?>(null), new FakeResolver(), 0));
    }

    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private sealed class FakeResolver : IAreaNameResolver
    {
        public event EventHandler? ResultsInvalidated
        {
            add { }
            remove { }
        }

        public AreaName? Result { get; init; }

        public IOException? Failure { get; init; }

        public Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
            => Failure is not null ? throw Failure : Task.FromResult(Result);
    }
}
