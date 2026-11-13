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
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class PhotoAreaClassifierTests
{
    private static readonly AreaName Tottori = new("312010870", "鳥取県", "鳥取市", "東品治町", SnappedToNearest: false, CensusYear: 2020);

    [Fact]
    public async Task ClassifyAsyncSeparatesPhotosWithoutLocationWithoutAreaDataAndResolved()
    {
        var reader = new FakeCoordinateReader
        {
            ["/p/tottori.jpg"] = new PhotoCoordinate(35.49, 134.22),
            ["/p/tokyo.jpg"] = new PhotoCoordinate(35.68, 139.76)
        };
        var resolver = new FakeResolver { [new PhotoCoordinate(35.49, 134.22)] = Tottori };
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, resolver);

        var entries = await ClassifyAllAsync(
            classifier,
            Photo("/p/tottori.jpg"),
            Photo("/p/tokyo.jpg"),
            Photo("/p/nogps.jpg")).ConfigureAwait(true);

        var byPath = entries.ToDictionary(entry => entry.FilePath);
        Assert.Equal((PhotoAreaKind.Resolved, Tottori), (byPath["/p/tottori.jpg"].Kind, byPath["/p/tottori.jpg"].Area));
        Assert.Equal(PhotoAreaKind.NoAreaData, byPath["/p/tokyo.jpg"].Kind);
        Assert.Equal(PhotoAreaKind.NoLocation, byPath["/p/nogps.jpg"].Kind);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task ClassifyAsyncDoesNotReadAFileKnownToHaveNoLocation()
    {
        var reader = new FakeCoordinateReader();
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, new FakeResolver());

        var entries = await ClassifyAllAsync(classifier, Photo("/p/a.jpg", hasLocation: false)).ConfigureAwait(true);

        Assert.Equal(PhotoAreaKind.NoLocation, Assert.Single(entries).Kind);
        Assert.Empty(reader.ReadPaths);
    }

    [Fact]
    public async Task ClassifyAsyncReadsAFileOnlyOnceWhileItsVersionIsUnchanged()
    {
        var reader = new FakeCoordinateReader { ["/p/a.jpg"] = new PhotoCoordinate(35.49, 134.22) };
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, new FakeResolver());

        await ClassifyAllAsync(classifier, Photo("/p/a.jpg", "v1")).ConfigureAwait(true);
        await ClassifyAllAsync(classifier, Photo("/p/a.jpg", "v1")).ConfigureAwait(true);
        Assert.Single(reader.ReadPaths);

        // ファイルが書き換わったら読み直す（位置情報の編集など）
        await ClassifyAllAsync(classifier, Photo("/p/a.jpg", "v2")).ConfigureAwait(true);
        Assert.Equal(2, reader.ReadPaths.Count);
    }

    [Fact]
    public async Task ClassifyAsyncKeepsTheResultOfAFileWithoutLocationToo()
    {
        var reader = new FakeCoordinateReader();
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, new FakeResolver());

        await ClassifyAllAsync(classifier, Photo("/p/a.jpg")).ConfigureAwait(true);
        await ClassifyAllAsync(classifier, Photo("/p/a.jpg")).ConfigureAwait(true);

        Assert.Single(reader.ReadPaths);
    }

    [Fact]
    public async Task ClassifyAsyncResolvesTheSameCoordinateOnlyOnce()
    {
        var coordinate = new PhotoCoordinate(35.49, 134.22);
        var reader = new FakeCoordinateReader { ["/p/a.jpg"] = coordinate, ["/p/b.jpg"] = coordinate };
        var resolver = new FakeResolver { [coordinate] = Tottori };
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, resolver, maxDegreeOfParallelism: 1);

        await ClassifyAllAsync(classifier, Photo("/p/a.jpg"), Photo("/p/b.jpg")).ConfigureAwait(true);

        Assert.Single(resolver.Calls);
    }

    [Fact]
    public async Task ClassifyAsyncResolvesAgainAfterTheAreaDataChanges()
    {
        var coordinate = new PhotoCoordinate(35.49, 134.22);
        var reader = new FakeCoordinateReader { ["/p/a.jpg"] = coordinate };
        var resolver = new FakeResolver();
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, resolver);

        var before = await ClassifyAllAsync(classifier, Photo("/p/a.jpg")).ConfigureAwait(true);
        Assert.Equal(PhotoAreaKind.NoAreaData, Assert.Single(before).Kind);

        // 地域データが取り込まれた。位置情報の読み取り結果は使い回し、字地名だけを求め直す
        resolver[coordinate] = Tottori;
        resolver.RaiseResultsInvalidated();
        var after = await ClassifyAllAsync(classifier, Photo("/p/a.jpg")).ConfigureAwait(true);

        Assert.Equal(PhotoAreaKind.Resolved, Assert.Single(after).Kind);
        Assert.Single(reader.ReadPaths);
    }

    [Fact]
    public async Task ClassifyAsyncDoesNotKeepAResultThatWasStartedBeforeTheAreaDataChanged()
    {
        var coordinate = new PhotoCoordinate(35.49, 134.22);
        var reader = new FakeCoordinateReader { ["/p/a.jpg"] = coordinate };
        var resolver = new FakeResolver { HoldResolves = true, [coordinate] = Tottori };
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, resolver);
        var stale = ClassifyAllAsync(classifier, Photo("/p/a.jpg"));
        await resolver.WaitForResolveAsync().ConfigureAwait(true);

        resolver.RaiseResultsInvalidated();
        resolver.ReleaseResolves();
        await stale.ConfigureAwait(true);
        await ClassifyAllAsync(classifier, Photo("/p/a.jpg")).ConfigureAwait(true);

        // 変更前に始まった判定の結果は保持されず、次の判定で求め直す
        Assert.Equal(2, resolver.Calls.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ClassifyAsyncNeverRunsMoreThanTheConfiguredNumberInParallel(int limit)
    {
        var reader = new FakeCoordinateReader { HoldReads = true };
        for (var i = 0; i < 12; i++)
        {
            reader[$"/p/{i}.jpg"] = new PhotoCoordinate(35 + i, 134);
        }

        var classifier = new PhotoAreaClassifier(reader.ReadAsync, new FakeResolver(), maxDegreeOfParallelism: limit);
        var run = ClassifyAllAsync(classifier, Enumerable.Range(0, 12).Select(i => Photo($"/p/{i}.jpg")).ToArray());
        await reader.WaitForConcurrentReadsAsync(limit).ConfigureAwait(true);
        await Task.Delay(100).ConfigureAwait(true);

        // 上限に達しても、それ以上は始まらない
        Assert.Equal(limit, reader.PeakConcurrency);
        reader.ReleaseReads();
        await run.ConfigureAwait(true);
        Assert.Equal(limit, reader.PeakConcurrency);
    }

    [Fact]
    public async Task ClassifyAsyncReportsProgressInBatchesAndOnceMoreAtTheEnd()
    {
        var reader = new FakeCoordinateReader();
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, new FakeResolver(), maxDegreeOfParallelism: 1, progressBatchSize: 3);
        var reports = new ConcurrentQueue<AreaClassificationProgress>();
        var photos = Enumerable.Range(0, 7).Select(i => Photo($"/p/{i}.jpg", hasLocation: false)).ToArray();

        await classifier.ClassifyAsync(photos, new SynchronousProgress(reports.Enqueue), CancellationToken.None).ConfigureAwait(true);

        var list = reports.ToList();
        Assert.Equal([3, 6, 7], list.Select(report => report.Completed));
        Assert.All(list, report => Assert.Equal(7, report.Total));
        Assert.Equal([3, 3, 1], list.Select(report => report.NewEntries.Count));
        Assert.Equal(7, list.SelectMany(report => report.NewEntries).Select(entry => entry.FilePath).Distinct().Count());
    }

    [Fact]
    public async Task ClassifyAsyncReportsOnceForAnEmptyList()
    {
        var classifier = new PhotoAreaClassifier(new FakeCoordinateReader().ReadAsync, new FakeResolver());
        var reports = new List<AreaClassificationProgress>();

        await classifier.ClassifyAsync([], new SynchronousProgress(reports.Add), CancellationToken.None).ConfigureAwait(true);

        var report = Assert.Single(reports);
        Assert.Equal((0, 0), (report.Completed, report.Total));
        Assert.Empty(report.NewEntries);
    }

    [Fact]
    public async Task ClassifyAsyncStopsWhenCanceled()
    {
        var reader = new FakeCoordinateReader { HoldReads = true };
        for (var i = 0; i < 20; i++)
        {
            reader[$"/p/{i}.jpg"] = new PhotoCoordinate(35, 134 + i);
        }

        var classifier = new PhotoAreaClassifier(reader.ReadAsync, new FakeResolver(), maxDegreeOfParallelism: 2);
        using var cancellation = new CancellationTokenSource();
        var run = classifier.ClassifyAsync(
            Enumerable.Range(0, 20).Select(i => Photo($"/p/{i}.jpg")).ToArray(),
            progress: null,
            cancellation.Token);
        await reader.WaitForConcurrentReadsAsync(2).ConfigureAwait(true);

        await cancellation.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run).ConfigureAwait(true);
        Assert.True(reader.ReadPaths.Count < 20);
    }

    [Fact]
    public async Task ClassifyAsyncPropagatesAFailureToReadTheAreaData()
    {
        var reader = new FakeCoordinateReader { ["/p/a.jpg"] = new PhotoCoordinate(35.49, 134.22) };
        var resolver = new FakeResolver { Failure = new IOException("broken area data") };
        var classifier = new PhotoAreaClassifier(reader.ReadAsync, resolver);

        var exception = await Assert.ThrowsAsync<IOException>(() => ClassifyAllAsync(classifier, Photo("/p/a.jpg"))).ConfigureAwait(true);

        Assert.Equal("broken area data", exception.Message);
    }

    [Fact]
    public void ConstructorRejectsInvalidArguments()
    {
        var reader = new FakeCoordinateReader();
        Assert.Throws<ArgumentNullException>(() => new PhotoAreaClassifier(null!, new FakeResolver()));
        Assert.Throws<ArgumentNullException>(() => new PhotoAreaClassifier(reader.ReadAsync, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhotoAreaClassifier(reader.ReadAsync, new FakeResolver(), maxDegreeOfParallelism: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhotoAreaClassifier(reader.ReadAsync, new FakeResolver(), progressBatchSize: 0));
    }

    private static PhotoRef Photo(string path, string version = "v1", bool? hasLocation = null) => new(path, version, hasLocation);

    private static async Task<IReadOnlyList<PhotoAreaEntry>> ClassifyAllAsync(PhotoAreaClassifier classifier, params PhotoRef[] photos)
    {
        var entries = new ConcurrentBag<PhotoAreaEntry>();
        await classifier.ClassifyAsync(
            photos,
            new SynchronousProgress(report =>
            {
                foreach (var entry in report.NewEntries)
                {
                    entries.Add(entry);
                }
            }),
            CancellationToken.None).ConfigureAwait(false);
        return entries.ToList();
    }

    // Progress<T> は同期コンテキストへ投げるので、テストでは呼び出しをそのまま実行する
    private sealed class SynchronousProgress(Action<AreaClassificationProgress> report) : IProgress<AreaClassificationProgress>
    {
        public void Report(AreaClassificationProgress value) => report(value);
    }

    private sealed class FakeCoordinateReader
    {
        private readonly Dictionary<string, PhotoCoordinate> _coordinates = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private int _concurrency;
        private int _peakConcurrency;

        public ConcurrentQueue<string> ReadPaths { get; } = new();

        public bool HoldReads { get; init; }

        public int PeakConcurrency
        {
            get
            {
                lock (_gate)
                {
                    return _peakConcurrency;
                }
            }
        }

        public PhotoCoordinate this[string path]
        {
            set => _coordinates[path] = value;
        }

        public void ReleaseReads() => _release.TrySetResult();

        public async Task WaitForConcurrentReadsAsync(int count)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (PeakConcurrency < count)
            {
                Assert.True(Environment.TickCount64 < deadline, $"{count} 件の同時読み取りが始まりませんでした。");
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        public async Task<PhotoCoordinate?> ReadAsync(string path, CancellationToken cancellationToken)
        {
            ReadPaths.Enqueue(path);
            lock (_gate)
            {
                _concurrency++;
                _peakConcurrency = Math.Max(_peakConcurrency, _concurrency);
            }

            try
            {
                if (HoldReads)
                {
                    await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                return _coordinates.TryGetValue(path, out var coordinate) ? coordinate : null;
            }
            finally
            {
                lock (_gate)
                {
                    _concurrency--;
                }
            }
        }
    }

    private sealed class FakeResolver : IAreaNameResolver
    {
        private readonly Dictionary<PhotoCoordinate, AreaName> _areas = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler? ResultsInvalidated;

        public ConcurrentQueue<PhotoCoordinate> Calls { get; } = new();

        public bool HoldResolves { get; init; }

        public IOException? Failure { get; init; }

        public AreaName this[PhotoCoordinate coordinate]
        {
            set => _areas[coordinate] = value;
        }

        public void RaiseResultsInvalidated() => ResultsInvalidated?.Invoke(this, EventArgs.Empty);

        public void ReleaseResolves() => _release.TrySetResult();

        public Task WaitForResolveAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public async Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            var coordinate = new PhotoCoordinate(latitude, longitude);
            Calls.Enqueue(coordinate);
            _started.TrySetResult();
            if (HoldResolves)
            {
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return _areas.GetValueOrDefault(coordinate);
        }
    }
}
