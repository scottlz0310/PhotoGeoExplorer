using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// HTTP を差し替えて、地域データのダウンロードの成功・各種の失敗・中止を確かめる。実ネットワークには接続しない。
/// どの結果でも一時ファイルが残らないこと、成功したときだけ取り込みが呼ばれることを共通で検証する
/// </summary>
public sealed class AreaDataDownloadServiceTests : IDisposable
{
    private static readonly byte[] ArchiveBytes = Enumerable.Range(0, 5000).Select(index => (byte)(index % 251)).ToArray();

    private readonly List<IDisposable> _disposables = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_AreaDataDownloadTests_" + Guid.NewGuid().ToString("N"));

    public AreaDataDownloadServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        Directory.Delete(_directory, recursive: true);
    }

    public static TheoryData<string, string> ExpectedUris => new()
    {
        { "01", "https://www.e-stat.go.jp/gis/statmap-search/data?dlserveyId=A002005212020&code=01&coordSys=1&format=shape&downloadType=5&datum=2011" },
        { "13", "https://www.e-stat.go.jp/gis/statmap-search/data?dlserveyId=A002005212020&code=13&coordSys=1&format=shape&downloadType=5&datum=2011" },
        { "47", "https://www.e-stat.go.jp/gis/statmap-search/data?dlserveyId=A002005212020&code=47&coordSys=1&format=shape&downloadType=5&datum=2011" }
    };

    public static TheoryData<HttpStatusCode> ErrorStatusCodes => new()
    {
        HttpStatusCode.NotFound,
        HttpStatusCode.Forbidden,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.ServiceUnavailable
    };

    [Theory]
    [MemberData(nameof(ExpectedUris))]
    public void BuildUriProducesTheEStatDownloadAddress(string prefectureCode, string expected)
    {
        Assert.Equal(expected, AreaDataDownloadService.BuildUri(AreaDataDownloadService.DefaultBaseUri, prefectureCode).AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("00")]
    [InlineData("48")]
    [InlineData("0a")]
    [InlineData("011")]
    public async Task DownloadAsyncRejectsAnInvalidPrefectureCodeWithoutCommunicating(string prefectureCode)
    {
        var handler = Stub((_, _) => throw new InvalidOperationException("must not communicate"));
        var service = CreateService(handler, new FakeAreaDataStore());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.DownloadAsync(prefectureCode, null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("application/zip")]
    [InlineData("application/x-zip-compressed")]
    [InlineData("APPLICATION/ZIP")]
    public async Task DownloadAsyncSavesTheArchiveAndImportsItThenRemovesTheTemporaryFile(string mediaType)
    {
        var store = new FakeAreaDataStore();
        byte[]? importedBytes = null;
        store.OnImport = path => importedBytes = File.ReadAllBytes(path);
        var handler = Stub((_, _) => Task.FromResult(ArchiveResponse(mediaType)));
        var reported = new List<long>();
        var service = CreateService(handler, store);

        var info = await service.DownloadAsync("31", new SynchronousProgress<long>(reported.Add), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(store.ImportResult, info);
        Assert.Equal(ArchiveBytes, importedBytes);
        Assert.Single(store.ImportedArchives);
        Assert.Equal("code=31", handler.LastRequestUri!.Query.Split('&').First(part => part.StartsWith("code=", StringComparison.Ordinal)));
        Assert.Equal(ArchiveBytes.Length, reported[^1]);
        Assert.True(reported.SequenceEqual(reported.Order()));
        AssertNoTemporaryFiles();
    }

    [Theory]
    [MemberData(nameof(ErrorStatusCodes))]
    public async Task DownloadAsyncReportsAnErrorStatusAndDoesNotImport(HttpStatusCode statusCode)
    {
        var store = new FakeAreaDataStore();
        var handler = Stub((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        var service = CreateService(handler, store);

        var exception = await Assert.ThrowsAsync<AreaDataDownloadException>(
            () => service.DownloadAsync("31", null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal((AreaDataDownloadError.HttpStatus, statusCode), (exception.Error, exception.StatusCode));
        Assert.Empty(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    public async Task DownloadAsyncStopsWhenTheResponseIsNotAnArchive(string mediaType)
    {
        var store = new FakeAreaDataStore();
        var handler = Stub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>login</html>", System.Text.Encoding.UTF8, MediaTypeHeaderValue.Parse(mediaType))
        }));
        var service = CreateService(handler, store);

        var exception = await Assert.ThrowsAsync<AreaDataDownloadException>(
            () => service.DownloadAsync("31", null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadError.NotArchive, exception.Error);
        Assert.Empty(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DownloadAsyncStopsWhenTheResponseHasNoContentType()
    {
        var store = new FakeAreaDataStore();
        var handler = Stub((_, _) =>
        {
            var content = new ByteArrayContent(ArchiveBytes);
            content.Headers.ContentType = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var service = CreateService(handler, store);

        var exception = await Assert.ThrowsAsync<AreaDataDownloadException>(
            () => service.DownloadAsync("31", null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadError.NotArchive, exception.Error);
        Assert.Empty(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DownloadAsyncRejectsADeclaredLengthOverTheLimitBeforeReadingTheBody()
    {
        var store = new FakeAreaDataStore();
        var handler = Stub((_, _) => Task.FromResult(ArchiveResponse("application/zip")));
        var service = CreateService(handler, store, maxDownloadBytes: ArchiveBytes.Length - 1);

        var exception = await Assert.ThrowsAsync<AreaDataDownloadException>(
            () => service.DownloadAsync("31", null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadError.TooLarge, exception.Error);
        Assert.Empty(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DownloadAsyncStopsWhenTheBodyExceedsTheLimitWithoutADeclaredLength()
    {
        var store = new FakeAreaDataStore();
        var handler = Stub((_, _) =>
        {
            var content = new StreamContent(new NonSeekableStream(ArchiveBytes));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var service = CreateService(handler, store, maxDownloadBytes: 1000);

        var exception = await Assert.ThrowsAsync<AreaDataDownloadException>(
            () => service.DownloadAsync("31", null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadError.TooLarge, exception.Error);
        Assert.Empty(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DownloadAsyncAcceptsABodyExactlyAtTheLimit()
    {
        var store = new FakeAreaDataStore();
        var handler = Stub((_, _) =>
        {
            var content = new StreamContent(new NonSeekableStream(ArchiveBytes));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var service = CreateService(handler, store, maxDownloadBytes: ArchiveBytes.Length);

        await service.DownloadAsync("31", null, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DownloadAsyncReportsAConnectionFailureAsANetworkError()
    {
        var store = new FakeAreaDataStore();
        var handler = Stub((_, _) => throw new HttpRequestException("connection refused"));
        var service = CreateService(handler, store);

        var exception = await Assert.ThrowsAsync<AreaDataDownloadException>(
            () => service.DownloadAsync("31", null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadError.Network, exception.Error);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Empty(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DownloadAsyncReportsATimeoutAsANetworkErrorNotACancellation()
    {
        var store = new FakeAreaDataStore();
        var handler = Stub((_, _) => throw new TaskCanceledException("timeout"));
        var service = CreateService(handler, store);

        var exception = await Assert.ThrowsAsync<AreaDataDownloadException>(
            () => service.DownloadAsync("31", null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadError.Network, exception.Error);
        Assert.Empty(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DownloadAsyncPropagatesACancellationDuringTheBodyAndLeavesNoFile()
    {
        var store = new FakeAreaDataStore();
        using var cancellation = new CancellationTokenSource();
        var firstChunkRead = new TaskCompletionSource();
        var handler = Stub((_, _) =>
        {
            var content = new StreamContent(new BlockingStream(ArchiveBytes, firstChunkRead));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var service = CreateService(handler, store);

        var download = service.DownloadAsync("31", null, cancellation.Token);
        await firstChunkRead.Task.ConfigureAwait(true);
        await cancellation.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download).ConfigureAwait(true);
        Assert.Empty(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task DownloadAsyncStartsTheImportOnTheCallersSynchronizationContext()
    {
        // 取り込みの完了通知（DataChanged）は、取り込みを呼んだ側のコンテキストで発火する。UI スレッドで呼べば、住所の更新も UI スレッドで走る
        var store = new FakeAreaDataStore();
        SynchronizationContext? importContext = null;
        store.OnImport = _ => importContext = SynchronizationContext.Current;
        var handler = Stub((_, _) => Task.FromResult(ArchiveResponse("application/zip")));
        var service = CreateService(handler, store);
        var callerContext = new PostingSynchronizationContext();

        await Task.Run(async () =>
        {
            SynchronizationContext.SetSynchronizationContext(callerContext);
            await service.DownloadAsync("31", null, CancellationToken.None).ConfigureAwait(true);
        }).ConfigureAwait(true);

        Assert.Same(callerContext, importContext);
    }

    [Fact]
    public async Task DownloadAsyncPropagatesAnImportFailureAndRemovesTheTemporaryFile()
    {
        var store = new FakeAreaDataStore { Failure = new AreaDataFormatException(AreaDataFormatError.InvalidArchive, "not an area data archive") };
        var handler = Stub((_, _) => Task.FromResult(ArchiveResponse("application/zip")));
        var service = CreateService(handler, store);

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => service.DownloadAsync("31", null, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.InvalidArchive, exception.Error);
        Assert.Single(store.ImportedArchives);
        AssertNoTemporaryFiles();
    }

    private static HttpResponseMessage ArchiveResponse(string mediaType)
    {
        var content = new ByteArrayContent(ArchiveBytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private StubHandler Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        _disposables.Add(handler);
        return handler;
    }

    private AreaDataDownloadService CreateService(StubHandler handler, FakeAreaDataStore store, long maxDownloadBytes = AreaDataDownloadService.DefaultMaxDownloadBytes)
    {
        var client = new HttpClient(handler, disposeHandler: false);
        _disposables.Add(client);
        return new(client, store, Path.Combine(_directory, "tmp"), maxDownloadBytes);
    }

    private void AssertNoTemporaryFiles()
    {
        var temporary = Path.Combine(_directory, "tmp");
        Assert.True(!Directory.Exists(temporary) || Directory.GetFileSystemEntries(temporary).Length == 0);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return respond(request, cancellationToken);
        }
    }

    /// <summary>登録した処理を同期的に呼ぶ（Progress&lt;T&gt; は呼び出しを同期コンテキストへ送るため、テストでは使わない）</summary>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>投稿された処理を、自分を現在のコンテキストにしてスレッドプールで実行する（UI スレッドの代わり）</summary>
    private sealed class PostingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SetSynchronizationContext(this);
                d(state);
            });
        }
    }

    /// <summary>Content-Length を持たない（読み取り専用・シークなし）ストリーム</summary>
    private class NonSeekableStream(byte[] bytes) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>最初の塊を返したあと、キャンセルされるまで待つストリーム</summary>
    private sealed class BlockingStream(byte[] bytes, TaskCompletionSource firstChunkRead) : NonSeekableStream(bytes)
    {
        private bool _firstChunkDelivered;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_firstChunkDelivered)
            {
                _firstChunkDelivered = true;
                var count = Read(buffer.Span[..1000]);
                firstChunkRead.TrySetResult();
                return count;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }
}
