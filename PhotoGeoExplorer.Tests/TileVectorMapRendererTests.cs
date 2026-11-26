using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoMapStudio.Rendering;
using PhotoMapStudio.Tiles;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class TileVectorMapRendererTests
{
    private const string UserAgent = "PhotoGeoExplorer.Tests/1.0 (test)";

    private static readonly VectorViewRequest TokyoRequest = new(35.681, 139.767, 15, 256, 256);

    private static readonly VectorPrefetchRequest TokyoPrefetchRequest = new(35.681, 139.767, 15);

    [Theory]
    [InlineData("", UserAgent)]
    [InlineData(" ", UserAgent)]
    [InlineData("cache", "")]
    [InlineData("cache", " ")]
    public void ConstructionRejectsABlankCacheDirectoryOrUserAgent(string cacheDirectory, string userAgent)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TileVectorMapRenderer(cacheDirectory, userAgent));
    }

    [Fact]
    public async Task ARenderFailureFromTheDeliverySourceIsNotHiddenAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            using var handler = new FixedStatusHandler(HttpStatusCode.ServiceUnavailable);
            using var renderer = new TileVectorMapRenderer(directory, UserAgent, handler);

            await Assert.ThrowsAnyAsync<Exception>(
                () => renderer.RenderViewAsync(TokyoRequest, CancellationToken.None)).ConfigureAwait(true);

            Assert.True(handler.RequestCount > 0);
        }).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(51.5074, -0.1278, 12, false)]
    [InlineData(51.5074, -0.1278, 12, true)]
    [InlineData(35.681, 139.767, 3, false)]
    [InlineData(35.681, 139.767, 3, true)]
    public async Task GsiDrawsABlankImageWhereTheArchiveHasNoTilesAsync(
        double latitude, double longitude, int zoom, bool allowIncompleteMaps)
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            using var handler = new StubHttpMessageHandler(EmptyGsiArchiveRange);
            using var renderer = new TileVectorMapRenderer(directory, UserAgent, handler, MapTileSourceType.GsiVector);
            var request = new VectorViewRequest(latitude, longitude, zoom, 96, 64)
            {
                AllowIncompleteMaps = allowIncompleteMaps
            };

            using var result = await renderer.RenderViewAsync(request).ConfigureAwait(true);

            Assert.True(result.IsComplete);
            using var style = new GsiVectorStyle();
            using var bitmap = SkiaSharp.SKBitmap.FromImage(result.Image);
            Assert.Equal(96, bitmap.Width);
            Assert.Equal(64, bitmap.Height);
            Assert.All(bitmap.Pixels, pixel => Assert.Equal(style.BackgroundColor, pixel));
        }).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GsiDoesNotTreatAnArchiveHttpErrorAsMissingCoverageAsync(HttpStatusCode status)
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            using var handler = new FixedStatusHandler(status);
            using var renderer = new TileVectorMapRenderer(directory, UserAgent, handler, MapTileSourceType.GsiVector);

            var error = await Assert.ThrowsAsync<TileFetchException>(
                () => renderer.RenderViewAsync(TokyoRequest)).ConfigureAwait(true);

            Assert.Equal(status, error.StatusCode);
            Assert.Equal(Uri.UriSchemeHttps, error.RequestUri?.Scheme);
        }).ConfigureAwait(true);
    }

    private static HttpResponseMessage EmptyGsiArchiveRange(HttpRequestMessage request)
    {
        const int headerSize = 127;
        // PMTiles v3 のヘッダーと空のルートディレクトリ（エントリー数 0 の 1 バイト）。
        var archive = new byte[headerSize + 1];
        "PMTiles"u8.CopyTo(archive);
        archive[7] = 3;
        BinaryPrimitives.WriteUInt64LittleEndian(archive.AsSpan(8), headerSize);
        BinaryPrimitives.WriteUInt64LittleEndian(archive.AsSpan(16), 1);
        archive[97] = 1;
        archive[98] = 1;
        archive[99] = 1;
        archive[100] = 4;
        archive[101] = 16;

        var range = Assert.Single(request.Headers.Range!.Ranges);
        var start = (int)range.From!.Value;
        var length = (int)(range.To!.Value - start + 1);
        return new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(archive, start, length)
        };
    }

    [Fact]
    public async Task RequestsIdentifyTheAppWithItsUserAgentAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            using var handler = new FixedStatusHandler(HttpStatusCode.ServiceUnavailable);
            using var renderer = new TileVectorMapRenderer(directory, UserAgent, handler);

            await Assert.ThrowsAnyAsync<Exception>(
                () => renderer.RenderViewAsync(TokyoRequest, CancellationToken.None)).ConfigureAwait(true);

            Assert.Equal(UserAgent, handler.LastUserAgent);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeDuringARenderWaitsForTheRenderToFinishBeforeReleasingResourcesAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            using var handler = new HeldResponseHandler();
            var renderer = new TileVectorMapRenderer(directory, UserAgent, handler);
            using var cts = new CancellationTokenSource();

            var render = renderer.RenderViewAsync(TokyoRequest, cts.Token);
            await handler.Started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            await cts.CancelAsync().ConfigureAwait(true);
            renderer.Dispose();
            handler.Release();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeCancelsARenderThatTheCallerDidNotCancelAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            using var handler = new HeldResponseHandler();
            var renderer = new TileVectorMapRenderer(directory, UserAgent, handler);

            var render = renderer.RenderViewAsync(TokyoRequest, CancellationToken.None);
            await handler.Started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            renderer.Dispose();
            handler.Release();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task RenderingAfterDisposeIsRejectedAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            var renderer = new TileVectorMapRenderer(directory, UserAgent);
            renderer.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => renderer.RenderViewAsync(TokyoRequest, CancellationToken.None)).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task APrefetchFailureFromTheDeliverySourceIsNotHiddenAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            using var handler = new FixedStatusHandler(HttpStatusCode.ServiceUnavailable);
            using var renderer = new TileVectorMapRenderer(directory, UserAgent, handler);

            await Assert.ThrowsAnyAsync<Exception>(
                () => renderer.PrefetchAsync(TokyoPrefetchRequest, CancellationToken.None)).ConfigureAwait(true);

            Assert.True(handler.RequestCount > 0);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeCancelsAPrefetchThatTheCallerDidNotCancelAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            using var handler = new HeldResponseHandler();
            var renderer = new TileVectorMapRenderer(directory, UserAgent, handler);

            var prefetch = renderer.PrefetchAsync(TokyoPrefetchRequest, CancellationToken.None);
            await handler.Started.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            renderer.Dispose();
            handler.Release();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prefetch).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task PrefetchAfterDisposeIsRejectedAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            var renderer = new TileVectorMapRenderer(directory, UserAgent);
            renderer.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => renderer.PrefetchAsync(TokyoPrefetchRequest, CancellationToken.None)).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeCanBeCalledTwiceAsync()
    {
        await WithTemporaryDirectoryAsync(directory =>
        {
            var renderer = new TileVectorMapRenderer(directory, UserAgent);

            renderer.Dispose();
            renderer.Dispose();
            return Task.CompletedTask;
        }).ConfigureAwait(true);
    }

    private static async Task WithTemporaryDirectoryAsync(Func<string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pge-vector-renderer-" + Guid.NewGuid().ToString("N"));
        try
        {
            await action(directory).ConfigureAwait(true);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public string? LastUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            LastUserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    // 応答を保留し、Release されるまでキャンセルにも反応しない（通信側のキャンセル完了が、破棄より後になる順序を作る）
    private sealed class HeldResponseHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
