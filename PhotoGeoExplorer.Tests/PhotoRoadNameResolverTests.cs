using System.Net.Http;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaTree;
using PhotoGeoExplorer.Services.Export;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class PhotoRoadNameResolverTests : IDisposable
{
    private static readonly DateTimeOffset TakenAt = new(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));
    private static readonly PhotoCoordinate Coordinate = new(35.0036, 135.7688);
    private static readonly RoadNameCandidate[] Candidates =
    [
        new("四条通", "186", 18),
        new("河原町通", "32", 48)
    ];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pge-road-lookup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task AConfirmedRoadNameIsReturnedWithoutFetchingCandidates()
    {
        var store = new FakeStore();
        store.Confirm("/p/a.jpg", 1000, TakenAt, new ConfirmedRoadName("丸山通", null));
        var suggester = new FakeSuggester(Candidates);
        var lookup = Create(store, suggester, suggest: true);

        var road = await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new RoadNameInfo(RoadNameStatus.Confirmed, "丸山通", null), road);
        Assert.Equal(0, suggester.CallCount);
        Assert.False(lookup.HasFailures);
    }

    [Fact]
    public async Task AnUnconfirmedPhotoGetsTheFirstCandidateWhenCandidatesAreRequested()
    {
        var suggester = new FakeSuggester(Candidates);
        var lookup = Create(new FakeStore(), suggester, suggest: true);

        var road = await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new RoadNameInfo(RoadNameStatus.Candidate, "四条通", "186"), road);
        Assert.Equal((Coordinate.Latitude, Coordinate.Longitude), suggester.LastCoordinate);
    }

    [Fact]
    public async Task AnUnconfirmedPhotoHasNoRoadNameWhenCandidatesAreNotRequested()
    {
        var suggester = new FakeSuggester(Candidates);
        var lookup = Create(new FakeStore(), suggester, suggest: false);

        var road = await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        // 通信しない。確定した値だけを出す
        Assert.Null(road);
        Assert.Equal(0, suggester.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnconfirmedPhotoWithoutAnyCandidateHasNoRoadName(bool hasSuggester)
    {
        var lookup = Create(new FakeStore(), hasSuggester ? new FakeSuggester([]) : null, suggest: true);

        Assert.Null(await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true));
        Assert.False(lookup.HasFailures);
    }

    [Theory]
    [InlineData(1001L, true)]
    [InlineData(1000L, false)]
    public async Task AConfirmationIsNotUsedForAPhotoWhoseSizeOrTakenAtDiffers(long confirmedSize, bool sameTakenAt)
    {
        var store = new FakeStore();
        store.Confirm("/p/a.jpg", confirmedSize, sameTakenAt ? TakenAt : TakenAt.AddSeconds(1), new ConfirmedRoadName("丸山通", null));
        var lookup = Create(store, new FakeSuggester(Candidates), suggest: true);

        var road = await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RoadNameStatus.Candidate, road?.Status);
    }

    [Fact]
    public async Task APhotoWhoseSizeCannotBeReadIsNotLookedUpInTheStore()
    {
        var store = new FakeStore();
        store.Confirm("/p/a.jpg", 1000, TakenAt, new ConfirmedRoadName("丸山通", null));
        var lookup = new PhotoRoadNameResolver(store, new FakeSuggester(Candidates), _ => null).CreateLookup(suggestCandidates: true);

        var road = await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, store.FindCount);
        Assert.Equal(RoadNameStatus.Candidate, road?.Status);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task AFailureToFetchCandidatesLeavesTheRoadNameEmptyAndStopsFetchingForTheRest(Type exceptionType)
    {
        var suggester = new FakeSuggester(Candidates) { Failure = (Exception)Activator.CreateInstance(exceptionType)! };
        var lookup = Create(new FakeStore(), suggester, suggest: true);

        var first = await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);
        var second = await lookup.ResolveAsync("/p/b.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        // 通信できない状態は以降の写真でも続くことが多いので、その回の残りでは求めない
        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, suggester.CallCount);
        Assert.True(lookup.HasFailures);
    }

    [Fact]
    public async Task AConfirmedRoadNameIsStillReturnedAfterFetchingCandidatesFailed()
    {
        var store = new FakeStore();
        store.Confirm("/p/b.jpg", 1000, TakenAt, new ConfirmedRoadName("丸山通", null));
        var lookup = Create(store, new FakeSuggester(Candidates) { Failure = new HttpRequestException("offline") }, suggest: true);

        await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);
        var road = await lookup.ResolveAsync("/p/b.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RoadNameStatus.Confirmed, road?.Status);
    }

    [Fact]
    public async Task CancellationIsNotTreatedAsAFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);
        var suggester = new FakeSuggester(Candidates) { Failure = new OperationCanceledException(cts.Token) };
        var lookup = Create(new FakeStore(), suggester, suggest: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, cts.Token)).ConfigureAwait(true);

        Assert.False(lookup.HasFailures);
    }

    [Fact]
    public async Task AFailureToReadTheConfirmationsIsCountedAndTheCandidateIsStillUsed()
    {
        var store = new FakeStore { Failure = new IOException("disk") };
        var lookup = Create(store, new FakeSuggester(Candidates), suggest: true);

        var road = await lookup.ResolveAsync("/p/a.jpg", TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RoadNameStatus.Candidate, road?.Status);
        Assert.True(lookup.HasFailures);
    }

    [Fact]
    public async Task TheFileSizeIsReadFromTheFileByDefault()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "a.jpg");
        await File.WriteAllBytesAsync(path, new byte[1234]).ConfigureAwait(true);
        var store = new FakeStore();
        store.Confirm(path, 1234, TakenAt, new ConfirmedRoadName("丸山通", null));
        var lookup = new PhotoRoadNameResolver(store, null).CreateLookup(suggestCandidates: false);

        var found = await lookup.ResolveAsync(path, TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);
        var missing = await lookup.ResolveAsync(Path.Combine(_directory, "none.jpg"), TakenAt, Coordinate, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(RoadNameStatus.Confirmed, found?.Status);
        Assert.Null(missing);
    }

    [Fact]
    public async Task MembersRejectInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new PhotoRoadNameResolver(null!, null));
        var lookup = Create(new FakeStore(), null, suggest: false);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => lookup.ResolveAsync(" ", TakenAt, Coordinate, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TheLoaderAttachesTheRoadNameOnlyWhenALookupIsGivenAndThePhotoHasALocation()
    {
        var suggester = new FakeSuggester(Candidates);
        var lookup = Create(new FakeStore(), suggester, suggest: true);
        var loader = new PhotoGeoRecordLoader(
            (path, _) => Task.FromResult<PhotoMetadata?>(path.Contains("gps", StringComparison.Ordinal)
                ? new PhotoMetadata(TakenAt, null, null, 35.0036, 135.7688, hasGpsData: true)
                : new PhotoMetadata(TakenAt, null, null, null, null)),
            new NoAreaResolver());

        var records = await loader.LoadAsync(["/p/gps.jpg", "/p/none.jpg"], null, CancellationToken.None, lookup).ConfigureAwait(true);
        var withoutLookup = await loader.LoadAsync(["/p/gps.jpg"], null, CancellationToken.None).ConfigureAwait(true);

        // 位置情報のある写真だけが道路名を持つ（字地名の有無に関わらず）。lookup を渡さなければ、道路名は取らない
        Assert.Equal(RoadNameStatus.Candidate, records[0].Road?.Status);
        Assert.Equal(PhotoAreaKind.NoAreaData, records[0].Kind);
        Assert.Null(records[1].Road);
        Assert.Null(Assert.Single(withoutLookup).Road);
        Assert.Equal(1, suggester.CallCount);
    }

    private static RoadNameLookup Create(FakeStore store, FakeSuggester? suggester, bool suggest)
        => new PhotoRoadNameResolver(store, suggester, _ => 1000L).CreateLookup(suggest);

    private sealed class NoAreaResolver : IAreaNameResolver
    {
        public event EventHandler? ResultsInvalidated
        {
            add { }
            remove { }
        }

        public Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
            => Task.FromResult<AreaName?>(null);
    }

    private sealed class FakeStore : IRoadNameConfirmationStore
    {
        private readonly Dictionary<string, (long Size, DateTimeOffset? TakenAt, ConfirmedRoadName Road)> _items = new();
        private int _findCount;

        public int FindCount => Volatile.Read(ref _findCount);

        public Exception? Failure { get; init; }

        public void Confirm(string path, long size, DateTimeOffset? takenAt, ConfirmedRoadName road) => _items[path] = (size, takenAt, road);

        public Task<ConfirmedRoadName?> FindAsync(PhotoIdentity photo, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _findCount);
            if (Failure is not null)
            {
                return Task.FromException<ConfirmedRoadName?>(Failure);
            }

            return Task.FromResult(
                _items.TryGetValue(photo.FilePath, out var item) && item.Size == photo.FileSize && item.TakenAt == photo.TakenAt ? item.Road : null);
        }

        public Task SaveAsync(PhotoIdentity photo, ConfirmedRoadName road, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveAsync(string filePath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSuggester(IReadOnlyList<RoadNameCandidate> result) : IRoadNameSuggester
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public (double, double)? LastCoordinate { get; private set; }

        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<RoadNameCandidate>> SuggestAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            LastCoordinate = (latitude, longitude);
            return Failure is not null ? Task.FromException<IReadOnlyList<RoadNameCandidate>>(Failure) : Task.FromResult(result);
        }
    }
}
