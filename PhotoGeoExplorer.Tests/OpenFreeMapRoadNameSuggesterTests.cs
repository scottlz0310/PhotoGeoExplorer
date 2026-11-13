using PhotoGeoExplorer.Services.RoadNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class OpenFreeMapRoadNameSuggesterTests
{
    [Fact]
    public void DefaultCacheDirectoryIsSeparateFromTheMapTileCache()
    {
        var directory = OpenFreeMapRoadNameSuggester.DefaultCacheDirectory;

        Assert.EndsWith(Path.Combine("PhotoGeoExplorer", "Cache", "RoadTiles"), directory, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "PhotoGeoExplorer/1.0 (contact)")]
    [InlineData(" ", "PhotoGeoExplorer/1.0 (contact)")]
    [InlineData("cache", "")]
    [InlineData("cache", " ")]
    public void ConstructionRejectsABlankCacheDirectoryOrUserAgent(string cacheDirectory, string userAgent)
    {
        Assert.ThrowsAny<ArgumentException>(() => new OpenFreeMapRoadNameSuggester(cacheDirectory, userAgent));
    }

    [Fact]
    public async Task ACoordinateThatCannotBeProjectedNeverReachesTheNetwork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pge-road-suggester-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var suggester = new OpenFreeMapRoadNameSuggester(directory, "PhotoGeoExplorer.Tests/1.0 (test)");

            var candidates = await suggester.SuggestAsync(90d, 0d, CancellationToken.None).ConfigureAwait(true);

            Assert.Empty(candidates);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DisposeDuringARequestWaitsForTheRequestToFinishBeforeReleasingResources()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pge-road-suggester-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var handler = new HeldResponseHandler();
            var suggester = new OpenFreeMapRoadNameSuggester(directory, "PhotoGeoExplorer.Tests/1.0 (test)", handler);
            using var cts = new CancellationTokenSource();

            var suggest = suggester.SuggestAsync(35.0036d, 135.7688d, cts.Token);
            await handler.Started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            await cts.CancelAsync().ConfigureAwait(true);
            suggester.Dispose();
            handler.Release();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => suggest).ConfigureAwait(true);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SuggestingAfterDisposeIsRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pge-road-suggester-" + Guid.NewGuid().ToString("N"));
        try
        {
            var suggester = new OpenFreeMapRoadNameSuggester(directory, "PhotoGeoExplorer.Tests/1.0 (test)");
            suggester.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => suggester.SuggestAsync(35.0036d, 135.7688d, CancellationToken.None)).ConfigureAwait(true);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void DisposeCanBeCalledTwice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pge-road-suggester-" + Guid.NewGuid().ToString("N"));
        try
        {
            var suggester = new OpenFreeMapRoadNameSuggester(directory, "PhotoGeoExplorer.Tests/1.0 (test)");

            suggester.Dispose();
            suggester.Dispose();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // 応答を保留し、Release されるまでキャンセルにも反応しない（通信側のキャンセル完了が、破棄より後になる順序を作る）
    private sealed class HeldResponseHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        }
    }
}
