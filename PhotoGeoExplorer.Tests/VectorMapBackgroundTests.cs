// 生成した SKBitmap／SKImage の所有権は、描画結果・SharedResource を経て背景のレイヤーへ渡る
#pragma warning disable CA2000
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoMapStudio.Rendering;
using SkiaSharp;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class VectorMapBackgroundTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Theory]
    // 100%・ズーム 0 の世界全体
    [InlineData(0, 0, WebMercator.ZoomZeroResolution, 256, 256, 1.0, 0, 0, 0, 256, 256)]
    // 150%: 画像は論理寸法 × 密度の物理ピクセル（1366 × 1.5 = 2049、1220 × 1.5 = 1830）。ズームは log2(1.5) だけ大きい
    [InlineData(0, 0, WebMercator.ZoomZeroResolution / 32768, 1366, 1220, 1.5, 0, 0, 15.584962500721156, 2049, 1830)]
    // 200%: 物理ピクセルあたりの解像度が 100% のズーム 15 と同じなら、ズームも同じ
    [InlineData(0, 0, WebMercator.ZoomZeroResolution * 2 / 32768, 1366, 1220, 2.0, 0, 0, 15.0, 2732, 2440)]
    public void ViewRequestIsConvertedToLatitudeLongitudeZoomAndPhysicalSize(
        double centerX,
        double centerY,
        double resolution,
        double widthDip,
        double heightDip,
        double density,
        double expectedLatitude,
        double expectedLongitude,
        double expectedZoom,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.True(ViewExtentRequest.TryCreate(centerX, centerY, resolution, widthDip, heightDip, density, "test", out var request));

        var view = VectorMapBackground.CreateViewRequest(request, allowIncompleteMaps: true);

        Assert.Equal(expectedLatitude, view.CenterLatitude, 9);
        Assert.Equal(expectedLongitude, view.CenterLongitude, 9);
        Assert.Equal(expectedZoom, view.Zoom, 9);
        Assert.Equal(expectedWidth, view.PixelWidth);
        Assert.Equal(expectedHeight, view.PixelHeight);
    }

    [Fact]
    public void ViewRequestKeepsTheCenterOfAnOffsetViewport()
    {
        const double longitude = 139.767;
        const double latitude = 35.681;
        var x = WebMercator.EarthRadius * longitude * Math.PI / 180;
        var y = WebMercator.EarthRadius * Math.Log(Math.Tan((Math.PI / 4) + (latitude * Math.PI / 360)));
        Assert.True(ViewExtentRequest.TryCreate(x, y, 4.0, 800, 600, 1.0, "test", out var request));

        var view = VectorMapBackground.CreateViewRequest(request, allowIncompleteMaps: true);

        Assert.Equal(latitude, view.CenterLatitude, 9);
        Assert.Equal(longitude, view.CenterLongitude, 9);
    }

    [Theory]
    // 表示は欠けたタイルを許して描き、保存は許さない
    [InlineData(true)]
    [InlineData(false)]
    public void IncompleteMapsAreAllowedOnlyWhenRequested(bool allow)
    {
        Assert.True(ViewExtentRequest.TryCreate(0, 0, 1, 100, 80, 1.0, "test", out var request));

        var view = VectorMapBackground.CreateViewRequest(request, allowIncompleteMaps: allow);

        Assert.Equal(allow, view.AllowIncompleteMaps);
        Assert.True(view.ParallelLabels);
        Assert.InRange(view.BandCount, 1, 8);
    }

    [Fact]
    public void AttributionTextNamesOpenFreeMapOpenMapTilesAndOpenStreetMap()
    {
        using var background = CreateBackground(out _);

        Assert.Equal("OpenFreeMap © OpenMapTiles Data from OpenStreetMap", background.AttributionText);
        Assert.InRange(MapAttribution.VectorLinks.Count, 1, MapAttribution.MaxLinkCount);
    }

    [Fact]
    public async Task AttachRendersTheCurrentViewportAndShowsTheImageAsync()
    {
        using var background = CreateBackground(out var renderer);
        var map = CreateMap(width: 400, height: 300);

        background.Attach(map, () => 2.0);

        var request = await renderer.NextRequestAsync().ConfigureAwait(true);
        Assert.Equal(800, request.PixelWidth);
        Assert.Equal(600, request.PixelHeight);
        Assert.True(request.AllowIncompleteMaps);
        await WaitUntilAsync(() => FeatureCount(background) == 1).ConfigureAwait(true);
    }

    [Fact]
    public async Task ViewportChangeFromAnotherThreadRendersAgainAsync()
    {
        using var background = CreateBackground(out var renderer);
        var map = CreateMap(width: 400, height: 300);
        background.Attach(map, () => 1.0);
        var first = await renderer.NextRequestAsync().ConfigureAwait(true);

        // Mapsui はアニメーション中の視点変更を UI スレッド以外から通知する
        await Task.Run(() => map.Navigator.ZoomTo(map.Navigator.Viewport.Resolution / 2)).ConfigureAwait(true);

        var second = await renderer.NextRequestAsync().ConfigureAwait(true);
        Assert.Equal(first.Zoom + 1, second.Zoom, 9);
    }

    [Fact]
    public async Task ViewportWithoutSizeIsNotRenderedUntilItHasOneAsync()
    {
        using var background = CreateBackground(out var renderer);
        var map = CreateMap(width: 0, height: 0);

        background.Attach(map, () => 1.0);
        Assert.Equal(0, renderer.CallCount);

        map.Navigator.SetSize(400, 300);
        var request = await renderer.NextRequestAsync().ConfigureAwait(true);
        Assert.Equal(400, request.PixelWidth);
    }

    [Fact]
    public async Task RenderFailureDoesNotStopLaterRendersAsync()
    {
        using var background = CreateBackground(out var renderer);
        renderer.FailNextCall = true;
        var map = CreateMap(width: 400, height: 300);
        background.Attach(map, () => 1.0);
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);
        Assert.Equal(0, FeatureCount(background));

        map.Navigator.ZoomTo(map.Navigator.Viewport.Resolution / 2);

        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => FeatureCount(background) == 1).ConfigureAwait(true);
    }

    [Fact]
    public async Task BusyChangedIsRaisedWhileRenderingAsync()
    {
        using var background = CreateBackground(out var renderer);
        var raised = new ConcurrentQueue<bool>();
        background.BusyChanged += (_, _) => raised.Enqueue(background.IsBusy);

        background.Attach(CreateMap(width: 400, height: 300), () => 1.0);
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy && FeatureCount(background) == 1).ConfigureAwait(true);

        Assert.Contains(true, raised);
        Assert.False(raised.Last());
    }

    [Fact]
    public async Task ExportRendersTheGivenViewportStrictlyAtItsDensityAsync()
    {
        using var background = CreateBackground(out var renderer);

        using var export = await background
            .CreateExportLayersAsync(new Viewport(0, 0, 1, 0, 100, 80), 2f, CancellationToken.None)
            .ConfigureAwait(true);

        var request = await renderer.NextRequestAsync().ConfigureAwait(true);
        Assert.Equal(200, request.PixelWidth);
        Assert.Equal(160, request.PixelHeight);
        Assert.False(request.AllowIncompleteMaps);
        var layer = Assert.IsAssignableFrom<MemoryLayer>(Assert.Single(export.Layers));
        Assert.Single(layer.Features);
        // 表示中のレイヤーとは別（保存は表示中のレイヤーに触らない）
        Assert.DoesNotContain(layer, background.Layers);
    }

    [Fact]
    public async Task ExportRejectsAViewportThatCannotBeRenderedAsync()
    {
        using var background = CreateBackground(out var renderer);

        await Assert.ThrowsAsync<ArgumentException>(() => background.CreateExportLayersAsync(
            new Viewport(0, 0, 1, 0, 0, 0),
            1f,
            CancellationToken.None)).ConfigureAwait(true);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task ExportPropagatesRenderFailureAsync()
    {
        using var background = CreateBackground(out var renderer);
        renderer.FailNextCall = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => background.CreateExportLayersAsync(
            new Viewport(0, 0, 1, 0, 100, 80),
            1f,
            CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public void AttachTwiceIsRejected()
    {
        using var background = CreateBackground(out _);
        var map = CreateMap(width: 0, height: 0);
        background.Attach(map, () => 1.0);

        Assert.Throws<InvalidOperationException>(() => background.Attach(map, () => 1.0));
    }

    [Fact]
    public async Task DisposeStopsRenderingAndDisposesTheRendererAsync()
    {
        var background = CreateBackground(out var renderer);
        var map = CreateMap(width: 400, height: 300);
        background.Attach(map, () => 1.0);
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);
        var callsBefore = renderer.CallCount;

        background.Dispose();
        map.Navigator.ZoomTo(map.Navigator.Viewport.Resolution / 2);
        await Task.Delay(100).ConfigureAwait(true);

        Assert.True(renderer.IsDisposed);
        Assert.Equal(callsBefore, renderer.CallCount);
        Assert.Throws<ObjectDisposedException>(() => background.Attach(map, () => 1.0));
        background.Dispose();
    }

    private static VectorMapBackground CreateBackground(out FakeRenderer renderer)
    {
        renderer = new FakeRenderer();
        return new VectorMapBackground(renderer, action =>
        {
            action();
            return true;
        });
    }

    private static Mapsui.Map CreateMap(double width, double height)
    {
        var map = new Mapsui.Map();
        if (width > 0 && height > 0)
        {
            map.Navigator.SetSize(width, height);
        }

        map.Navigator.CenterOnAndZoomTo(new MPoint(0, 0), 4.0);
        return map;
    }

    private static int FeatureCount(VectorMapBackground background)
        => Assert.IsAssignableFrom<MemoryLayer>(Assert.Single(background.Layers)).Features.Count();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "条件が満たされないまま待ち時間を超えた。");
            await Task.Delay(10).ConfigureAwait(true);
        }
    }

    private sealed class FakeRenderer : IVectorMapRenderer, IDisposable
    {
        private readonly BlockingCollection<VectorViewRequest> _requests = [];
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public bool IsDisposed { get; private set; }

        public bool FailNextCall { get; set; }

        public Task<VectorViewResult> RenderViewAsync(VectorViewRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            _requests.Add(request, cancellationToken);
            if (FailNextCall)
            {
                FailNextCall = false;
                throw new InvalidOperationException("描画の失敗（テスト）");
            }

            using var bitmap = new SKBitmap(request.PixelWidth, request.PixelHeight);
            return Task.FromResult(new VectorViewResult(SKImage.FromBitmap(bitmap)));
        }

        public Task<VectorViewRequest> NextRequestAsync()
            => Task.Run(() => _requests.TryTake(out var request, Timeout)
                ? request
                : throw new TimeoutException("描画が要求されないまま待ち時間を超えた。"));

        public void Dispose()
        {
            IsDisposed = true;
            _requests.Dispose();
        }
    }
}
