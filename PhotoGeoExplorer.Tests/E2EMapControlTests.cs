using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.E2E;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class E2EMapControlTests
{
    [Fact]
    public async Task ProtocolHandlesStatusAndViewCommands()
    {
        var controller = new FakeMapViewController(CreateStatus(43.768436, 142.362436, 16.0, isBusy: false, renderedSeq: 1));
        var infoLogs = new List<string>();
        var errorLogs = new List<string>();
        var protocol = new E2EMapControlProtocol(controller, infoLogs.Add, errorLogs.Add);

        var statusJson = await protocol.HandleLineAsync("""{"command":"status"}""");
        using (var statusDoc = JsonDocument.Parse(statusJson))
        {
            Assert.True(statusDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("status", statusDoc.RootElement.GetProperty("command").GetString());
            Assert.Equal(16.0, statusDoc.RootElement.GetProperty("status").GetProperty("zoom").GetDouble(), 6);
            Assert.Equal(16, statusDoc.RootElement.GetProperty("status").GetProperty("styleZoom").GetInt32());
        }

        var viewJson = await protocol.HandleLineAsync("""{"command":"view","latitude":43.455153,"longitude":142.599269,"zoom":17.5}""");
        using (var viewDoc = JsonDocument.Parse(viewJson))
        {
            Assert.True(viewDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("view", viewDoc.RootElement.GetProperty("command").GetString());
            var returnedStatus = viewDoc.RootElement.GetProperty("status");
            Assert.Equal(43.455153, returnedStatus.GetProperty("center").GetProperty("latitude").GetDouble(), 6);
            Assert.Equal(142.599269, returnedStatus.GetProperty("center").GetProperty("longitude").GetDouble(), 6);
            Assert.Equal(17.5, returnedStatus.GetProperty("zoom").GetDouble(), 6);
            Assert.Equal(17, returnedStatus.GetProperty("styleZoom").GetInt32());
        }

        Assert.Equal(2, infoLogs.Count);
        Assert.Empty(errorLogs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"command":123}""")]
    [InlineData("""{"command":"unknown"}""")]
    [InlineData("""{"command":"view","latitude":43.0,"longitude":142.0}""")]
    [InlineData("""{"command":"view","latitude":"43.0","longitude":142.0,"zoom":16}""")]
    [InlineData("""{"command":"view","latitude":91.0,"longitude":142.0,"zoom":16}""")]
    [InlineData("""{"command":"view","latitude":-91.0,"longitude":142.0,"zoom":16}""")]
    [InlineData("""{"command":"view","latitude":43.0,"longitude":181.0,"zoom":16}""")]
    [InlineData("""{"command":"view","latitude":43.0,"longitude":-181.0,"zoom":16}""")]
    [InlineData("""{"command":"view","latitude":43.0,"longitude":142.0,"zoom":-0.1}""")]
    [InlineData("""{"command":"view","latitude":43.0,"longitude":142.0,"zoom":22.1}""")]
    public async Task ProtocolRejectsMalformedAndOutOfRangeRequests(string requestLine)
    {
        var controller = new FakeMapViewController(CreateStatus(43.0, 142.0, 14.0, isBusy: false, renderedSeq: 0));
        var protocol = new E2EMapControlProtocol(controller);

        var response = await protocol.HandleLineAsync(requestLine);

        using var doc = JsonDocument.Parse(response);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString()));
        Assert.Equal(0, controller.SetViewCalls);
    }

    [Fact]
    public async Task ProtocolRejectsOversizedLine()
    {
        var controller = new FakeMapViewController(CreateStatus(43.0, 142.0, 14.0, isBusy: false, renderedSeq: 0));
        var protocol = new E2EMapControlProtocol(controller);
        var oversized = new string('a', E2EMapControlProtocol.MaxRequestLineLength + 1);

        var response = await protocol.HandleLineAsync(oversized);

        using var doc = JsonDocument.Parse(response);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("maximum length", doc.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NamedPipeServerIsConnectableAsSoonAsItIsConstructed()
    {
        var controller = new FakeMapViewController(CreateStatus(35.681236, 139.767125, 14.0, isBusy: false, renderedSeq: 1));
        var pipeName = $"PhotoGeoExplorer.E2E.Test.{Guid.NewGuid():N}";

        using var server = new E2EMapControlServer(controller, pipeName);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        // 待たずに接続できること。受け付けのループがスレッドプールで始まるのを待つ作りだと失敗する（#397）。
        client.Connect(0);

        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task NamedPipeServerAcceptsMultipleCommandsAndReconnections()
    {
        var controller = new FakeMapViewController(CreateStatus(35.681236, 139.767125, 14.0, isBusy: false, renderedSeq: 1));
        var pipeName = $"PhotoGeoExplorer.E2E.Test.{Guid.NewGuid():N}";
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        using var server = new E2EMapControlServer(controller, pipeName);

        // 1 回目の接続で複数コマンドを連続実行
        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(TestTasks.HangTimeout, CancellationToken.None);
            using var reader = new StreamReader(client, utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(client, utf8NoBom, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

            await writer.WriteLineAsync("""{"command":"status"}""");
            var firstResponse = await reader.ReadLineAsync();
            Assert.NotNull(firstResponse);
            using (var doc = JsonDocument.Parse(firstResponse))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(14.0, doc.RootElement.GetProperty("status").GetProperty("zoom").GetDouble(), 6);
            }

            await writer.WriteLineAsync("""{"command":"view","latitude":43.768436,"longitude":142.362436,"zoom":18.2}""");
            var secondResponse = await reader.ReadLineAsync();
            Assert.NotNull(secondResponse);
            using (var doc = JsonDocument.Parse(secondResponse))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(18.2, doc.RootElement.GetProperty("status").GetProperty("zoom").GetDouble(), 6);
                Assert.Equal(18, doc.RootElement.GetProperty("status").GetProperty("styleZoom").GetInt32());
            }
        }

        // 切断後の再接続で更新済み状態が読めることを確認
        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(TestTasks.HangTimeout, CancellationToken.None);
            using var reader = new StreamReader(client, utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(client, utf8NoBom, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

            await writer.WriteLineAsync("""{"command":"status"}""");
            var reconnectResponse = await reader.ReadLineAsync();
            Assert.NotNull(reconnectResponse);
            using var doc = JsonDocument.Parse(reconnectResponse);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(43.768436, doc.RootElement.GetProperty("status").GetProperty("center").GetProperty("latitude").GetDouble(), 6);
        }
    }

    [Fact]
    public async Task StartupApplierWaitsUntilIdleAndValidViewportThenLogsJsonOnce()
    {
        var controller = new FakeMapViewController(MapViewStatus.Empty) { StayBusyOnSetView = true };
        var infoLogs = new List<string>();
        var errorLogs = new List<string>();

        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add, errorLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 17.5));

        // Busy 中はまだ status JSON を出力しない
        Assert.DoesNotContain(infoLogs, line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));

        // Idle に遷移すると 1 回だけ JSON ログを出力する
        controller.CompleteRendering();
        controller.CompleteRendering();

        var statusLines = infoLogs.FindAll(line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
        Assert.Single(statusLines);
        Assert.Empty(errorLogs);

        var json = statusLines[0][E2EMapStartupApplier.StatusLogPrefix.Length..];
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("hasValidViewport").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("isBusy").GetBoolean());
        Assert.Equal(17.5, doc.RootElement.GetProperty("zoom").GetDouble(), 6);
        Assert.Equal(17, doc.RootElement.GetProperty("styleZoom").GetInt32());
    }

    [Fact]
    public async Task StartupApplierEmitsImmediatelyWhenAlreadyIdleAndValid()
    {
        var controller = new FakeMapViewController(CreateStatus(35.0, 139.0, 12.0, isBusy: false, renderedSeq: 1));
        var infoLogs = new List<string>();

        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 16.0));

        var statusLines = infoLogs.FindAll(line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
        Assert.Single(statusLines);
    }

    [Fact]
    public async Task StartupApplierDoesNotLogWhenBusyClearsWithoutFrameRender()
    {
        var controller = new FakeMapViewController(CreateStatus(35.0, 139.0, 12.0, isBusy: false, renderedSeq: 3))
        {
            StayBusyOnSetView = true,
            AdvanceSeqOnSetView = false
        };
        var infoLogs = new List<string>();

        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 16.0));

        // 描画失敗で RenderedSeq が進まずに IsBusy が false へ戻ったケース
        controller.FailRendering();

        Assert.DoesNotContain(infoLogs, line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartupApplierLogsErrorWhenSetViewFailsAndThrowsAfterDispose()
    {
        var controller = new FakeMapViewController(MapViewStatus.Empty) { FailSetView = true };
        var errorLogs = new List<string>();

        var applier = new E2EMapStartupApplier(controller, logError: errorLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 16.0));

        Assert.Single(errorLogs);

        applier.Dispose();
        applier.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => applier.ApplyAsync(new MapViewTarget(43.0, 142.0, 14.0)));
    }

    [Fact]
    public async Task NamedPipeServerRejectsOversizedLineAndAcceptsSubsequentConnections()
    {
        var controller = new FakeMapViewController(CreateStatus(35.681236, 139.767125, 14.0, isBusy: false, renderedSeq: 1));
        var pipeName = $"PhotoGeoExplorer.E2E.Test.{Guid.NewGuid():N}";
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Assert.Equal("PhotoGeoExplorer.E2E.42", E2EMapControlServer.BuildPipeName(42));

        using var server = new E2EMapControlServer(controller, pipeName);
        Assert.Equal(pipeName, server.PipeName);

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(TestTasks.HangTimeout, CancellationToken.None);
            using var reader = new StreamReader(client, utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(client, utf8NoBom, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

            await writer.WriteLineAsync(new string('x', E2EMapControlProtocol.MaxRequestLineLength + 10));
            var errorLine = await reader.ReadLineAsync();
            Assert.NotNull(errorLine);
            using var doc = JsonDocument.Parse(errorLine);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        }

        // 超過切断後も次の接続で通常コマンドが処理できる
        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(TestTasks.HangTimeout, CancellationToken.None);
            using var reader = new StreamReader(client, utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(client, utf8NoBom, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

            await writer.WriteLineAsync("""{"command":"status"}""");
            var okLine = await reader.ReadLineAsync();
            Assert.NotNull(okLine);
            using var doc = JsonDocument.Parse(okLine);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        }

        server.Dispose();
    }

    private static MapViewStatus CreateStatus(double latitude, double longitude, double zoom, bool isBusy, long renderedSeq)
        => MapViewStatus.Create(
            hasSize: true,
            centerX: WebMercator.FromLongitude(longitude),
            centerY: WebMercator.FromLatitude(latitude),
            resolution: WebMercator.ResolutionFromLogicalZoom(zoom),
            widthDip: 800,
            heightDip: 600,
            pixelDensity: 1.5,
            configuredTileSource: MapTileSourceType.OpenFreeMapVector,
            displayedTileSource: MapTileSourceType.OpenFreeMapVector,
            attributionText: "OpenFreeMap",
            isBusy: isBusy,
            renderedSeq: renderedSeq);

    private sealed class FakeMapViewController : IMapViewController
    {
        private MapViewStatus _status;

        internal FakeMapViewController(MapViewStatus initialStatus)
        {
            _status = initialStatus;
        }

        internal int SetViewCalls { get; private set; }

        internal bool StayBusyOnSetView { get; init; }

        internal bool AdvanceSeqOnSetView { get; init; } = true;

        internal bool FailSetView { get; init; }

        public MapViewStatus CurrentStatus => _status;

        public event EventHandler<MapViewStatus>? StatusChanged;

        public Task<MapViewCommandResult> SetViewAsync(MapViewTarget target, CancellationToken cancellationToken = default)
        {
            SetViewCalls++;
            if (FailSetView)
            {
                return Task.FromResult(new MapViewCommandResult(false, _status, "Simulated failure."));
            }

            var nextSeq = !StayBusyOnSetView && AdvanceSeqOnSetView ? _status.RenderedSeq + 1 : _status.RenderedSeq;
            _status = CreateStatus(
                target.Latitude,
                target.Longitude,
                target.Zoom,
                isBusy: StayBusyOnSetView,
                renderedSeq: nextSeq);
            StatusChanged?.Invoke(this, _status);
            return Task.FromResult(new MapViewCommandResult(true, _status));
        }

        internal void CompleteRendering()
        {
            _status = _status with { IsBusy = false, RenderedSeq = _status.RenderedSeq + 1 };
            StatusChanged?.Invoke(this, _status);
        }

        internal void FailRendering()
        {
            _status = _status with { IsBusy = false };
            StatusChanged?.Invoke(this, _status);
        }
    }
}
