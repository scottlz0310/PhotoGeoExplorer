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
using Mapsui.Manipulations;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.E2E;
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

        // 画素密度も伝える（伝えないと、文字・線幅が物理ピクセルで固定のため、密度が高いほど画面上で小さくなる）
        Assert.Equal(density, view.PixelRatio);
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
        Assert.InRange(MapAttribution.GetLinks(MapTileSourceType.OpenFreeMapVector).Count, 1, MapAttribution.MaxLinkCount);
    }

    [Fact]
    public async Task PixelDensityChangeRendersTheCurrentViewportAgainAtTheNewDensityAsync()
    {
        using var background = CreateBackground(out var renderer);
        var density = 1.0;
        background.Attach(CreateMap(width: 400, height: 300), () => density);
        var first = await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);

        density = 2.0;
        background.OnPixelDensityChanged();

        var second = await renderer.NextRequestAsync().ConfigureAwait(true);
        Assert.Equal(400, first.PixelWidth);
        Assert.Equal(800, second.PixelWidth);
        Assert.Equal(first.Zoom + 1, second.Zoom, 9);
    }

    [Fact]
    public async Task AttachGivesMapsuiTheZoomLevelsSoThatZoomingToALevelResolvesTheViewAsync()
    {
        // 実機の確認で見つかった不具合: ベクターのレイヤーにはズームの段階が無く、写真を選んでも ZoomToLevel が呼ばれず
        // 視点の解像度が 0 のままで、何も描かれなかった（MapMarkerPresenter は Resolutions が空だと ZoomToLevel を呼ばない）
        using var background = CreateBackground(out var renderer);
        var map = new Mapsui.Map();
        map.Navigator.SetSize(400, 300);
        Assert.Empty(map.Navigator.Resolutions);

        background.Attach(map, () => 1.0);

        Assert.Equal(VectorMapBackground.MaxZoomLevel + 1, map.Navigator.Resolutions.Count);
        map.Navigator.CenterOn(new MPoint(15860088, 5381690));
        map.Navigator.ZoomToLevel(18);
        Assert.Equal(WebMercator.ZoomZeroResolution / Math.Pow(2, 18), map.Navigator.Viewport.Resolution, 9);
        var request = await renderer.NextRequestAsync().ConfigureAwait(true);
        Assert.Equal(18, request.Zoom, 6);
    }

    [Theory]
    // ピンチ・ホイールで拡大し続けても、縮小し続けても、上下限（最小＝最後の段階の半分、最大＝最初の段階）で止まる。
    // 実機で、ベクターを選ぶと際限なく拡大・縮小できた。描画の前（レイヤーが空）でも、描画の後でも同じ
    [InlineData(2.0, WebMercator.ZoomZeroResolution / 1048576, false)]
    [InlineData(0.5, WebMercator.ZoomZeroResolution, false)]
    [InlineData(2.0, WebMercator.ZoomZeroResolution / 1048576, true)]
    [InlineData(0.5, WebMercator.ZoomZeroResolution, true)]
    public async Task ZoomByGestureStopsAtTheZoomLimitsWhetherOrNotAnImageHasBeenRenderedAsync(double scaleFactor, double expectedResolution, bool afterRender)
    {
        using var background = CreateBackground(out var renderer);
        var map = CreateMap(width: 400, height: 300);
        map.Layers.Add(background.Layers[0]);
        background.Attach(map, () => 1.0);
        if (afterRender)
        {
            await renderer.NextRequestAsync().ConfigureAwait(true);
            await WaitUntilAsync(() => FeatureCount(background) == 1 && !background.IsBusy).ConfigureAwait(true);
        }

        var center = new ScreenPosition(200, 150);
        for (var i = 0; i < 60; i++)
        {
            map.Navigator.Manipulate(new Manipulation(center, center, scaleFactor, 0, 0));
        }

        Assert.Equal(expectedResolution, map.Navigator.Viewport.Resolution, 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PanByGestureMovesTheViewEvenIfMapsuiLimitedThePanBoundsToASinglePointAsync(bool afterRender)
    {
        // 実機で見つかった不具合: 実行中に背景を切り替えた後、Mapsui のパンの範囲が視点の中心の 1 点（Min = Max）になり、
        // パンできなくなった。範囲を明示すれば、レイヤーから決まる範囲に左右されない
        using var background = CreateBackground(out var renderer);
        var map = CreateMap(width: 400, height: 300);
        map.Layers.Add(background.Layers[0]);
        var viewport = map.Navigator.Viewport;
        map.Navigator.OverridePanBounds = new MRect(viewport.CenterX, viewport.CenterY, viewport.CenterX, viewport.CenterY);
        background.Attach(map, () => 1.0);
        if (afterRender)
        {
            await renderer.NextRequestAsync().ConfigureAwait(true);
            await WaitUntilAsync(() => FeatureCount(background) == 1 && !background.IsBusy).ConfigureAwait(true);
        }

        var before = map.Navigator.Viewport.CenterX;

        // 右から左へ 100 px ドラッグ
        map.Navigator.Manipulate(new Manipulation(new ScreenPosition(100, 150), new ScreenPosition(200, 150), 1.0, 0, 0));

        Assert.NotEqual(before, map.Navigator.Viewport.CenterX);
    }

    [Fact]
    public void PanBoundsCoverTheWholeWorldSoThatThePanIsNotLimitedByTheLayers()
    {
        using var background = CreateBackground(out _);
        var map = CreateMap(width: 400, height: 300);

        background.Attach(map, () => 1.0);

        var bounds = Assert.IsType<MRect>(map.Navigator.OverridePanBounds);
        Assert.Equal(-WebMercator.HalfWorldExtent, bounds.MinX, 6);
        Assert.Equal(-WebMercator.HalfWorldExtent, bounds.MinY, 6);
        Assert.Equal(WebMercator.HalfWorldExtent, bounds.MaxX, 6);
        Assert.Equal(WebMercator.HalfWorldExtent, bounds.MaxY, 6);
    }

    [Fact]
    public void DisposeRestoresThePanBoundsOnlyIfItSetThemAndKeepsOnesThatSomeoneElseSet()
    {
        var first = CreateBackground(out _);
        var map = CreateMap(width: 400, height: 300);
        first.Attach(map, () => 1.0);
        Assert.NotNull(map.Navigator.OverridePanBounds);
        first.Dispose();
        Assert.Null(map.Navigator.OverridePanBounds);

        // 背景を入れ替えた直後（新しい背景が Attach 済み）に、古い背景を破棄しても、新しい背景の設定を消さない
        var old = CreateBackground(out _);
        old.Attach(map, () => 1.0);
        using var replacement = CreateBackground(out _);
        replacement.Attach(map, () => 1.0);
        var replacementBounds = map.Navigator.OverridePanBounds;
        old.Dispose();
        Assert.Same(replacementBounds, map.Navigator.OverridePanBounds);

        // 他の呼び出し側が設定した値は消さない
        var others = new MRect(-1, -1, 1, 1);
        map.Navigator.OverridePanBounds = others;
        replacement.Dispose();
        Assert.Same(others, map.Navigator.OverridePanBounds);
    }

    [Fact]
    public void AttachSetsTheZoomLimitsAndDisposeRestoresThem()
    {
        var background = CreateBackground(out _);
        var map = CreateMap(width: 400, height: 300);
        Assert.Null(map.Navigator.OverrideZoomBounds);

        background.Attach(map, () => 1.0);
        Assert.NotNull(map.Navigator.OverrideZoomBounds);

        background.Dispose();
        Assert.Null(map.Navigator.OverrideZoomBounds);
    }

    [Fact]
    public void DisposeKeepsZoomLimitsThatSomeoneElseSet()
    {
        var background = CreateBackground(out _);
        var map = CreateMap(width: 400, height: 300);
        background.Attach(map, () => 1.0);
        var others = new MMinMax(1, 1000);
        map.Navigator.OverrideZoomBounds = others;

        background.Dispose();

        Assert.Same(others, map.Navigator.OverrideZoomBounds);
    }

    [Fact]
    public void DisposeRestoresTheZoomLevelsSoTheNextBackgroundCanUseItsOwn()
    {
        var background = CreateBackground(out _);
        var map = CreateMap(width: 400, height: 300);
        background.Attach(map, () => 1.0);
        Assert.NotEmpty(map.Navigator.Resolutions);

        background.Dispose();

        Assert.Empty(map.Navigator.Resolutions);
    }

    [Fact]
    public void DisposeKeepsZoomLevelsThatSomeoneElseSet()
    {
        var background = CreateBackground(out _);
        var map = CreateMap(width: 400, height: 300);
        background.Attach(map, () => 1.0);
        double[] others = [100, 50, 25];
        map.Navigator.OverrideResolutions = others;

        background.Dispose();

        Assert.Same(others, map.Navigator.OverrideResolutions);
    }

    [Fact]
    public void PixelDensityChangeBeforeAttachDoesNothing()
    {
        using var background = CreateBackground(out var renderer);

        background.OnPixelDensityChanged();

        Assert.Equal(0, renderer.CallCount);
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
    public async Task ImageWithMissingTilesIsStillShownAndLaterRendersContinueAsync()
    {
        using var background = CreateBackground(out var renderer);
        renderer.UnavailableTileCount = 3;
        var map = CreateMap(width: 400, height: 300);
        background.Attach(map, () => 1.0);

        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => FeatureCount(background) == 1 && !background.IsBusy).ConfigureAwait(true);

        // 欠けた状態から完全な状態へ戻っても描画は続く
        renderer.UnavailableTileCount = 0;
        map.Navigator.ZoomTo(map.Navigator.Viewport.Resolution / 2);
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);
        Assert.Equal(1, FeatureCount(background));
    }

    [Theory]
    // 描画と同じ変換（緯度経度・連続ズーム）で中心を渡す。画素密度が高いほどズームは大きい
    [InlineData(0, 0, WebMercator.ZoomZeroResolution, 1.0, 0, 0, 0)]
    [InlineData(0, 0, WebMercator.ZoomZeroResolution / 32768, 1.5, 0, 0, 15.584962500721156)]
    [InlineData(0, 0, WebMercator.ZoomZeroResolution * 2 / 32768, 2.0, 0, 0, 15.0)]
    public void PrefetchRequestUsesTheCenterAndTheContinuousZoomOfTheView(
        double centerX,
        double centerY,
        double resolution,
        double density,
        double expectedLatitude,
        double expectedLongitude,
        double expectedZoom)
    {
        Assert.True(ViewExtentRequest.TryCreate(centerX, centerY, resolution, 800, 600, density, "test", out var request));

        var prefetch = VectorMapBackground.CreatePrefetchRequest(request);

        Assert.Equal(expectedLatitude, prefetch.CenterLatitude, 9);
        Assert.Equal(expectedLongitude, prefetch.CenterLongitude, 9);
        Assert.Equal(expectedZoom, prefetch.Zoom, 9);
    }

    [Fact]
    public async Task AViewThatStaysStillIsPrefetchedAfterTheIdleDelayAsync()
    {
        var clock = new ManualTimeProvider();
        var prefetcher = new FakePrefetcher();
        using var background = CreateBackground(out var renderer, prefetcher, clock);
        background.Attach(CreateMap(width: 400, height: 300), () => 1.0);
        var rendered = await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);

        clock.Advance(ViewExtentPrefetchScheduler.DefaultIdleDelay);

        var prefetch = Assert.Single(prefetcher.Requests);
        Assert.Equal(rendered.CenterLatitude, prefetch.CenterLatitude, 9);
        Assert.Equal(rendered.CenterLongitude, prefetch.CenterLongitude, 9);
        Assert.Equal(rendered.Zoom, prefetch.Zoom, 9);
    }

    [Fact]
    public async Task NothingIsPrefetchedWithoutAPrefetcherOrAfterDisposeAsync()
    {
        // 事前デコードを持たない背景（ラスタ以外の最小構成）でも描画は続く
        using (var background = CreateBackground(out var renderer))
        {
            background.Attach(CreateMap(width: 400, height: 300), () => 1.0);
            await renderer.NextRequestAsync().ConfigureAwait(true);
        }

        var clock = new ManualTimeProvider();
        var prefetcher = new FakePrefetcher();
        var disposed = CreateBackground(out var disposedRenderer, prefetcher, clock);
        disposed.Attach(CreateMap(width: 400, height: 300), () => 1.0);
        await disposedRenderer.NextRequestAsync().ConfigureAwait(true);

        disposed.Dispose();
        clock.Advance(ViewExtentPrefetchScheduler.DefaultIdleDelay);

        Assert.Empty(prefetcher.Requests);
    }

    [Fact]
    public void ConstructionRejectsMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new VectorMapBackground(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => new VectorMapBackground(new FakeRenderer(), null!));
    }

    [Fact]
    public void AttachRejectsMissingArguments()
    {
        using var background = CreateBackground(out _);

        Assert.Throws<ArgumentNullException>(() => background.Attach(null!, () => 1.0));
        Assert.Throws<ArgumentNullException>(() => background.Attach(CreateMap(width: 0, height: 0), null!));
    }

    [Fact]
    public async Task BusyChangedIsRaisedWhileRenderingAsync()
    {
        using var background = CreateBackground(out var renderer);
        var raised = 0;
        background.BusyChanged += (_, _) => Interlocked.Increment(ref raised);

        background.Attach(CreateMap(width: 400, height: 300), () => 1.0);
        await renderer.NextRequestAsync().ConfigureAwait(true);

        // BusyChanged は UI 以外のスレッドから、状態を変えたロックの外で発火する。ハンドラの中で IsBusy を読むと、
        // すでに次の状態へ変わっていることがある（「忙しくなった」通知のハンドラが、終了後に false を読む）ので、
        // 値ではなく、忙しくなった・終わったの 2 回の遷移が通知されたことを確かめる
        await WaitUntilAsync(() => Volatile.Read(ref raised) == 2 && !background.IsBusy && FeatureCount(background) == 1)
            .ConfigureAwait(true);
        Assert.Equal(2, Volatile.Read(ref raised));
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

    [Fact]
    public async Task StartupApplierDoesNotEmitCompletionOrAdvanceSeqWhenVectorRenderFailsAsync()
    {
        using var background = CreateBackground(out var renderer);
        using var map = CreateMap(width: 800, height: 600);
        var controller = new MapViewController();
        var frameRenderedCount = 0;

        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) =>
        {
            Interlocked.Increment(ref frameRenderedCount);
            controller.NotifyFrameRendered();
        };

        // 起動直後の描画は失敗させる
        renderer.FailNextCall = true;
        controller.Attach(map, MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);

        Assert.Equal(0, Volatile.Read(ref frameRenderedCount));
        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);

        // 起動時指定（案 A）の移動後の描画も失敗させる
        var infoLogs = new List<string>();
        var errorLogs = new List<string>();
        renderer.FailNextCall = true;

        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add, errorLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 17.5)).ConfigureAwait(true);
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);

        // 描画失敗で非ビジーに戻っても、FrameRendered・RenderedSeq・完了 JSON ログのいずれも発生しない
        Assert.Equal(0, Volatile.Read(ref frameRenderedCount));
        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);
        Assert.DoesNotContain(infoLogs, line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
        Assert.Empty(errorLogs);
    }

    [Fact]
    public async Task StartupApplierEmitsCompletionOnceOnlyAfterTargetViewportFrameIsShownAsync()
    {
        using var background = CreateBackground(out var renderer);
        using var map = CreateMap(width: 800, height: 600);
        var controller = new MapViewController();

        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) => controller.NotifyFrameRendered();

        // 初期描画を完了させる（RenderedSeq = 1）
        controller.Attach(map, MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy && controller.CurrentStatus.RenderedSeq == 1).ConfigureAwait(true);

        // 案 A で別位置へ移動し、新ビューポートの描画が完了した時点で RenderedSeq = 2 と完了 JSON が 1 回出力される
        var infoLogs = new List<string>();
        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(43.768436, 142.362436, 17.5)).ConfigureAwait(true);
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy && controller.CurrentStatus.RenderedSeq == 2).ConfigureAwait(true);

        var statusLines = infoLogs.FindAll(line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
        Assert.Single(statusLines);
        Assert.Contains("\"renderedSeq\":2", statusLines[0], StringComparison.Ordinal);
        Assert.Contains("\"styleZoom\":17", statusLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleOverlappingFrameDoesNotAdvanceRenderedSeqWhenLatestViewportRenderFailsAsync()
    {
        using var background = CreateBackground(out var renderer);
        using var map = CreateMap(width: 800, height: 600);
        var controller = new MapViewController();

        background.BusyChanged += (_, _) => controller.UpdateBusy(background.IsBusy);
        background.FrameRendered += (_, _) => controller.NotifyFrameRendered();

        // 最初の描画をブロックして実行中に保つ
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.BlockNextCall = firstGate.Task;

        controller.Attach(map, MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector, background.AttributionText, 1.0, background.IsBusy);
        background.Attach(map, () => 1.0);
        await renderer.NextRequestAsync().ConfigureAwait(true);

        // 範囲が重なる新しい視点へ移動し、その新視点の描画は失敗させる
        renderer.FailNextCall = true;
        var infoLogs = new List<string>();
        using var applier = new E2EMapStartupApplier(controller, infoLogs.Add);
        await applier.ApplyAsync(new MapViewTarget(0.001, 0.001, 15.0)).ConfigureAwait(true);

        // 古いフレームを完了させてから新視点の失敗を待つ
        firstGate.SetResult();
        await renderer.NextRequestAsync().ConfigureAwait(true);
        await WaitUntilAsync(() => !background.IsBusy).ConfigureAwait(true);

        // 古い過渡フレームは最新要求と一致しないため採番も完了ログも行われない
        Assert.Equal(0, controller.CurrentStatus.RenderedSeq);
        Assert.DoesNotContain(infoLogs, line => line.StartsWith(E2EMapStartupApplier.StatusLogPrefix, StringComparison.Ordinal));
    }

    private static VectorMapBackground CreateBackground(
        out FakeRenderer renderer,
        IVectorMapPrefetcher? prefetcher = null,
        TimeProvider? timeProvider = null)
    {
        renderer = new FakeRenderer();
        return new VectorMapBackground(
            renderer,
            action =>
            {
                action();
                return true;
            },
            prefetcher,
            timeProvider);
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

    private sealed class FakePrefetcher : IVectorMapPrefetcher
    {
        private readonly ConcurrentQueue<VectorPrefetchRequest> _requests = new();

        public IReadOnlyCollection<VectorPrefetchRequest> Requests => _requests;

        public Task PrefetchAsync(VectorPrefetchRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Enqueue(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRenderer : IVectorMapRenderer, IDisposable
    {
        private readonly BlockingCollection<VectorViewRequest> _requests = [];
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public bool IsDisposed { get; private set; }

        public bool FailNextCall { get; set; }

        public Task? BlockNextCall { get; set; }

        public int UnavailableTileCount { get; set; }

        public async Task<VectorViewResult> RenderViewAsync(VectorViewRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            var shouldFail = FailNextCall;
            if (shouldFail)
            {
                FailNextCall = false;
            }

            var gate = BlockNextCall;
            if (gate is not null)
            {
                BlockNextCall = null;
            }

            _requests.Add(request, cancellationToken);
            if (gate is not null)
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (shouldFail)
            {
                throw new InvalidOperationException("描画の失敗（テスト）");
            }

            using var bitmap = new SKBitmap(request.PixelWidth, request.PixelHeight);
            return new VectorViewResult(
                SKImage.FromBitmap(bitmap),
                requestedTileCount: 4,
                unavailableTileCount: UnavailableTileCount);
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
