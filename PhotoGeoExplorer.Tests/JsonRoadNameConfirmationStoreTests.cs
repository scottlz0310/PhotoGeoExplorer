using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.RoadNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class JsonRoadNameConfirmationStoreTests : IDisposable
{
    private static readonly DateTimeOffset TakenAt = new(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));
    private static readonly PhotoIdentity Photo = new(@"C:\photos\a.jpg", 1234, TakenAt);
    private static readonly ConfirmedRoadName Road = new("山の手通り", "203");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pge-road-confirmations-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "road-name-confirmations.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ASavedConfirmationSurvivesARestart()
    {
        await new JsonRoadNameConfirmationStore(StorePath).SaveAsync(Photo, Road, CancellationToken.None).ConfigureAwait(true);

        var restarted = new JsonRoadNameConfirmationStore(StorePath);

        Assert.Equal(Road, await restarted.FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task AnUnconfirmedPhotoHasNoConfirmation()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);

        Assert.Null(await store.FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(@"C:\photos\a.jpg", 1235L, true)]
    [InlineData(@"C:\photos\a.jpg", 1234L, false)]
    [InlineData(@"C:\photos\b.jpg", 1234L, true)]
    public async Task AConfirmationIsNotReturnedForAPhotoThatDiffersInPathSizeOrTakenAt(string path, long size, bool sameTakenAt)
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);
        await store.SaveAsync(Photo, Road, CancellationToken.None).ConfigureAwait(true);

        var found = await store.FindAsync(new PhotoIdentity(path, size, sameTakenAt ? TakenAt : TakenAt.AddSeconds(1)), CancellationToken.None).ConfigureAwait(true);

        Assert.Null(found);
    }

    [Fact]
    public async Task ThePathIsMatchedIgnoringCase()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);
        await store.SaveAsync(Photo, Road, CancellationToken.None).ConfigureAwait(true);

        var found = await store.FindAsync(Photo with { FilePath = @"c:\PHOTOS\A.JPG" }, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(Road, found);
    }

    [Fact]
    public async Task APhotoWithoutATakenAtIsMatchedByItsSizeAlone()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);
        var withoutDate = Photo with { TakenAt = null };
        await store.SaveAsync(withoutDate, Road, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(Road, await store.FindAsync(withoutDate, CancellationToken.None).ConfigureAwait(true));
        Assert.Null(await store.FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task SavingAgainReplacesTheConfirmation()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);
        await store.SaveAsync(Photo, Road, CancellationToken.None).ConfigureAwait(true);
        var other = new ConfirmedRoadName("丸山通", null);

        await store.SaveAsync(Photo, other, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(other, await new JsonRoadNameConfirmationStore(StorePath).FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task RemovingDeletesTheConfirmationAlsoAfterARestart()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);
        await store.SaveAsync(Photo, Road, CancellationToken.None).ConfigureAwait(true);

        await store.RemoveAsync(Photo.FilePath, CancellationToken.None).ConfigureAwait(true);

        Assert.Null(await store.FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
        Assert.Null(await new JsonRoadNameConfirmationStore(StorePath).FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task RemovingAnUnconfirmedPhotoDoesNothing()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);

        await store.RemoveAsync(Photo.FilePath, CancellationToken.None).ConfigureAwait(true);

        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task ARoadWithoutAnyNameOrRouteNumberCannotBeConfirmed()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(Photo, new ConfirmedRoadName(null, null), CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task AConfirmationOfARoadWithOnlyARouteNumberIsKept()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);
        var routeOnly = new ConfirmedRoadName(null, "53・373");
        await store.SaveAsync(Photo, routeOnly, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(routeOnly, await new JsonRoadNameConfirmationStore(StorePath).FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ConcurrentSavesAreAllKept()
    {
        var store = new JsonRoadNameConfirmationStore(StorePath);
        var photos = Enumerable.Range(0, 30).Select(index => new PhotoIdentity(@$"C:\photos\{index}.jpg", index, TakenAt)).ToArray();

        await Task.WhenAll(photos.Select(photo => store.SaveAsync(photo, new ConfirmedRoadName("道" + photo.FileSize, null), CancellationToken.None))).ConfigureAwait(true);

        var restarted = new JsonRoadNameConfirmationStore(StorePath);
        foreach (var photo in photos)
        {
            Assert.Equal(new ConfirmedRoadName("道" + photo.FileSize, null), await restarted.FindAsync(photo, CancellationToken.None).ConfigureAwait(true));
        }
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("{\"version\": 2, \"items\": []}")]
    [InlineData("null")]
    public async Task AnUnreadableFileIsMovedAsideAndTheStoreStartsEmpty(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(StorePath, content).ConfigureAwait(true);
        var store = new JsonRoadNameConfirmationStore(StorePath);

        Assert.Null(await store.FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
        await store.SaveAsync(Photo, Road, CancellationToken.None).ConfigureAwait(true);

        // 読めなかった内容は、次の保存で上書きされずに残る
        var broken = Assert.Single(Directory.GetFiles(_directory, "road-name-confirmations.json.broken-*"));
        Assert.Equal(content, await File.ReadAllTextAsync(broken).ConfigureAwait(true));
        Assert.Equal(Road, await new JsonRoadNameConfirmationStore(StorePath).FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ASaveThatCannotBeWrittenLeavesTheStoreUnchanged()
    {
        // 保存先のパスがディレクトリなので、置き換えに失敗する
        Directory.CreateDirectory(StorePath);
        var store = new JsonRoadNameConfirmationStore(StorePath);

        await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync(Photo, Road, CancellationToken.None)).ConfigureAwait(true);

        Assert.Null(await store.FindAsync(Photo, CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void TheDefaultPathIsUnderTheApplicationDataFolder()
    {
        Assert.EndsWith(Path.Combine("PhotoGeoExplorer", "road-name-confirmations.json"), JsonRoadNameConfirmationStore.DefaultPath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConstructionRejectsABlankPath(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => new JsonRoadNameConfirmationStore(path));
    }
}
