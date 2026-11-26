using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

#pragma warning disable CA2000 // 画像を作る処理の破棄は ViewModel が行う。

namespace PhotoGeoExplorer.Tests;

public sealed class MapImageExportViewModelTests
{
    private const int CurrentView = 0;
    private const int Hd = 1;
    private const int Uxga = 2;
    private const int Custom = 3;

    [Theory]
    [InlineData(1.0, 3, 0, 800, 418)]
    [InlineData(1.25, 4, 1, 1000, 522)]
    [InlineData(1.5, 3, 1, 1200, 626)]
    [InlineData(2.0, 3, 2, 1600, 835)]
    [InlineData(1.75, 4, 2, 1400, 731)]
    public async Task InitialConditionsReproduceTheCurrentView(
        double density, int scaleCount, int scaleIndex, int width, int height)
    {
        var exporter = new FakeMapImageExporter();
        var source = Source(density);
        var viewModel = new MapImageExportViewModel(exporter, source);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Start();
            await viewModel.PreviewTask.ConfigureAwait(true);

            Assert.Equal(CurrentView, viewModel.SizeIndex);
            Assert.Equal(scaleCount, viewModel.TextScaleChoices.Count);
            Assert.Equal(scaleIndex, viewModel.TextScaleIndex);
            var request = Assert.Single(exporter.Rendered);
            Assert.Equal(
                new MapImageExportRequest(
                    source.Background, source.View.CenterX, source.View.CenterY,
                    WebMercator.LogicalZoomFromResolution(source.View.Resolution),
                    width, height, density, source.Markers),
                request);
        }
    }

    [Theory]
    [InlineData(Hd, 1280, 720)]
    [InlineData(Uxga, 1600, 1200)]
    public async Task APresetSizeKeepsItsPixelsWhenTheTextSizeChanges(int sizeIndex, int width, int height)
    {
        var viewModel = new MapImageExportViewModel(new FakeMapImageExporter(), Source(1.0), NoDelay);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.SizeIndex = sizeIndex;
            viewModel.TextScaleIndex = 2;

            Assert.False(viewModel.IsCustomSize);
            Assert.Equal(width, viewModel.Width);
            Assert.Equal(height, viewModel.Height);
        }
    }

    [Fact]
    public async Task TheCurrentViewSizeFollowsTheTextSizeSoTheSameAreaIsShown()
    {
        var exporter = new FakeMapImageExporter();
        var viewModel = new MapImageExportViewModel(exporter, Source(1.0), NoDelay);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.TextScaleIndex = 1;
            await viewModel.PreviewTask.ConfigureAwait(true);

            var request = exporter.Rendered[^1];
            Assert.Equal((1200, 626, 1.5), (request.PixelWidth, request.PixelHeight, request.PixelDensity));
        }
    }

    [Fact]
    public async Task OnlyACustomSizeAcceptsTypedValuesAndStartsFromTheShownSize()
    {
        var exporter = new FakeMapImageExporter();
        var viewModel = new MapImageExportViewModel(exporter, Source(1.0), NoDelay);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Width = 300;
            Assert.Equal(800, viewModel.Width);

            viewModel.SizeIndex = Hd;
            viewModel.SizeIndex = Custom;
            Assert.True(viewModel.IsCustomSize);
            Assert.Equal((1280d, 720d), (viewModel.Width, viewModel.Height));

            viewModel.Width = 300;
            viewModel.Height = 200.4;
            await viewModel.PreviewTask.ConfigureAwait(true);

            var request = exporter.Rendered[^1];
            Assert.Equal((300, 200), (request.PixelWidth, request.PixelHeight));
        }
    }

    [Theory]
    [InlineData(double.NaN, 600, 14.0, 12)]
    [InlineData(63, 600, 14.0, 12)]
    [InlineData(800, 4097, 14.0, 12)]
    [InlineData(1e300, 600, 14.0, 12)]
    [InlineData(800, 600, double.NaN, 12)]
    [InlineData(800, 600, 22.5, 12)]
    [InlineData(800, 600, 14.0, 257)]
    public async Task ConditionsThatCannotBeRenderedDisableSavingAndClearThePreview(
        double width, double height, double zoom, int tileCount)
    {
        var exporter = new FakeMapImageExporter();
        var viewModel = new MapImageExportViewModel(exporter, Source(1.0), NoDelay);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Start();
            await viewModel.PreviewTask.ConfigureAwait(true);
            viewModel.NotifyPreviewShown(viewModel.PreviewPng!);
            Assert.True(viewModel.CanSave);

            exporter.CountTiles = _ => tileCount;
            viewModel.SizeIndex = Custom;
            viewModel.Width = width;
            viewModel.Height = height;
            viewModel.Zoom = zoom;
            await viewModel.PreviewTask.ConfigureAwait(true);

            Assert.False(viewModel.CanSave);
            Assert.False(viewModel.IsRendering);
            Assert.Null(viewModel.PreviewPng);
            Assert.NotEmpty(viewModel.StatusText);
            Assert.All(exporter.Rendered, request => Assert.Equal(MapImageExportProblem.None, MapImageExportLimits.Validate(request)));
        }
    }

    [Theory]
    [InlineData(0.004, false)]
    [InlineData(-0.004, false)]
    [InlineData(0.1, true)]
    public async Task TheRoundedValueWrittenBackByTheInputDoesNotChangeTheZoom(double difference, bool changes)
    {
        var viewModel = new MapImageExportViewModel(new FakeMapImageExporter(), Source(1.0), NoDelay);
        await using (viewModel.ConfigureAwait(true))
        {
            var initial = viewModel.Zoom;

            viewModel.Zoom = initial + difference;

            Assert.Equal(changes ? initial + difference : initial, viewModel.Zoom);
        }
    }

    [Fact]
    public async Task QuickChangesWaitForTheDelayAndRenderOnlyTheLastCondition()
    {
        var exporter = new FakeMapImageExporter();
        var delays = new ControlledDelay();
        var viewModel = new MapImageExportViewModel(exporter, Source(1.0), delays.DelayAsync);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Zoom = 10;
            viewModel.Zoom = 11;
            viewModel.Zoom = 12;
            Assert.Empty(exporter.Rendered);
            Assert.True(viewModel.IsRendering);

            await delays.ReleaseLatestAsync().ConfigureAwait(true);
            await viewModel.PreviewTask.ConfigureAwait(true);

            Assert.Equal(12, Assert.Single(exporter.Rendered).LogicalZoom);
            Assert.All(delays.Requested, delay => Assert.Equal(MapImageExportViewModel.PreviewDelay, delay));
            Assert.False(viewModel.IsRendering);
        }
    }

    [Fact]
    public async Task TheNextPreviewStartsAfterTheCanceledOneHasFinished()
    {
        var firstRender = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstToken = CancellationToken.None;
        var exporter = new FakeMapImageExporter();
        exporter.Render = (_, token) =>
        {
            if (exporter.Rendered.Count > 1)
            {
                return Task.FromResult<Stream>(new MemoryStream([2]));
            }

            // 取り消しを受けても、後始末が終わるまで完了しない描画
            firstToken = token;
            return firstRender.Task;
        };
        var viewModel = new MapImageExportViewModel(exporter, Source(1.0), NoDelay);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Start();
            viewModel.Zoom = 10;

            Assert.True(firstToken.IsCancellationRequested);
            Assert.Single(exporter.Rendered);

            firstRender.SetResult(new MemoryStream([1]));
            await viewModel.PreviewTask.ConfigureAwait(true);

            Assert.Equal(2, exporter.Rendered.Count);
            Assert.Equal([2], viewModel.PreviewPng!);
        }
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task AFailedPreviewDisablesSavingAndCanBeRetried(Type failure)
    {
        var exporter = new FakeMapImageExporter
        {
            Render = (_, _) => Task.FromException<Stream>((Exception)Activator.CreateInstance(failure)!)
        };
        var viewModel = new MapImageExportViewModel(exporter, Source(1.0), NoDelay);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Start();
            await viewModel.PreviewTask.ConfigureAwait(true);

            Assert.False(viewModel.CanSave);
            Assert.False(viewModel.IsRendering);
            Assert.Null(viewModel.PreviewPng);
            Assert.Equal(Visibility.Visible, viewModel.RetryVisibility);
            Assert.Contains("PreviewFailed", viewModel.StatusText, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(viewModel.OpenImageToSave);

            exporter.Render = null;
            viewModel.Retry();
            Assert.Equal(Visibility.Collapsed, viewModel.RetryVisibility);
            await viewModel.PreviewTask.ConfigureAwait(true);
            viewModel.NotifyPreviewShown(viewModel.PreviewPng!);

            using var stream = viewModel.OpenImageToSave();
            Assert.Equal(2, stream.ReadByte());
        }
    }

    [Fact]
    public async Task SavingIsEnabledOnlyWhileThePreviewOfTheCurrentConditionsIsShown()
    {
        var exporter = new FakeMapImageExporter();
        var delays = new ControlledDelay();
        var viewModel = new MapImageExportViewModel(exporter, Source(1.0), delays.DelayAsync);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Start();
            await delays.ReleaseLatestAsync().ConfigureAwait(true);
            await viewModel.PreviewTask.ConfigureAwait(true);
            var first = viewModel.PreviewPng!;

            // 画像ができても、画面に出るまでは保存できない
            Assert.False(viewModel.CanSave);
            viewModel.NotifyPreviewShown(first);
            Assert.True(viewModel.CanSave);

            // 前の画像が画面に残っている間は、保存できない
            viewModel.Zoom = 10;
            Assert.False(viewModel.CanSave);
            Assert.Same(first, viewModel.PreviewPng);
            viewModel.NotifyPreviewShown(first);
            Assert.False(viewModel.CanSave);
            Assert.Throws<InvalidOperationException>(viewModel.OpenImageToSave);

            await delays.ReleaseLatestAsync().ConfigureAwait(true);
            await viewModel.PreviewTask.ConfigureAwait(true);
            viewModel.NotifyPreviewShown(first);
            Assert.False(viewModel.CanSave);
            viewModel.NotifyPreviewShown(viewModel.PreviewPng!);

            using var stream = viewModel.OpenImageToSave();
            Assert.Equal(2, stream.ReadByte());
            Assert.Equal(2, exporter.Rendered.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALateNotificationOfAnEarlierImageDoesNotEnableSavingAfterReturningToItsCondition(bool shownBefore)
    {
        var delays = new ControlledDelay();
        var viewModel = new MapImageExportViewModel(new FakeMapImageExporter(), Source(1.0), delays.DelayAsync);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Start();
            await delays.ReleaseLatestAsync().ConfigureAwait(true);
            await viewModel.PreviewTask.ConfigureAwait(true);
            var first = viewModel.PreviewPng!;
            if (shownBefore)
            {
                viewModel.NotifyPreviewShown(first);
            }

            viewModel.SizeIndex = Hd;
            viewModel.SizeIndex = CurrentView;

            // 画面はまだ最初の画像を復号していて、その完了が後から届く
            viewModel.NotifyPreviewShown(first);
            Assert.False(viewModel.CanSave);
            Assert.Throws<InvalidOperationException>(viewModel.OpenImageToSave);

            await delays.ReleaseLatestAsync().ConfigureAwait(true);
            await viewModel.PreviewTask.ConfigureAwait(true);

            // 新しい画像が画面に出るまでは保存できない
            viewModel.NotifyPreviewShown(first);
            Assert.False(viewModel.CanSave);
            Assert.Throws<InvalidOperationException>(viewModel.OpenImageToSave);

            viewModel.NotifyPreviewShown(viewModel.PreviewPng!);
            using var stream = viewModel.OpenImageToSave();
            Assert.Equal(2, stream.ReadByte());
        }
    }

    [Fact]
    public async Task APreviewThatCannotBeShownDisablesSaving()
    {
        var viewModel = new MapImageExportViewModel(new FakeMapImageExporter(), Source(1.0), NoDelay);
        await using (viewModel.ConfigureAwait(true))
        {
            viewModel.Start();
            await viewModel.PreviewTask.ConfigureAwait(true);

            viewModel.NotifyPreviewDisplayFailed(viewModel.PreviewPng!);

            Assert.False(viewModel.CanSave);
            Assert.Null(viewModel.PreviewPng);
            Assert.Equal(Visibility.Visible, viewModel.RetryVisibility);
        }
    }

    [Fact]
    public async Task ClosingCancelsThePreviewAndReleasesTheExporterAfterItFinishes()
    {
        var render = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = CancellationToken.None;
        var exporter = new FakeMapImageExporter
        {
            Render = (_, cancellationToken) =>
            {
                token = cancellationToken;
                return render.Task;
            }
        };
        var viewModel = new MapImageExportViewModel(exporter, Source(1.0), NoDelay);
        viewModel.Start();

        var closing = viewModel.DisposeAsync().AsTask();

        Assert.True(token.IsCancellationRequested);
        Assert.False(closing.IsCompleted);
        Assert.Equal(0, exporter.DisposeCount);

        render.SetResult(new MemoryStream([1]));
        await closing.ConfigureAwait(true);
        await viewModel.DisposeAsync().ConfigureAwait(true);
        viewModel.Zoom = 3;

        Assert.Equal(1, exporter.DisposeCount);
        Assert.Single(exporter.Rendered);
        Assert.Null(viewModel.PreviewPng);
    }

    private static Task NoDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static MapImageExportSource Source(double density)
        => new(
            new MapBackgroundRequest(MapTileSourceType.OpenStreetMap, "PhotoGeoExplorer.Tests"),
            new MapViewState(
                HasSize: true,
                CenterX: 15000000,
                CenterY: 4000000,
                Resolution: WebMercator.ResolutionFromLogicalZoom(14.3333),
                WidthDip: 800,
                HeightDip: 417.6),
            density,
            []);

    private sealed class ControlledDelay
    {
        private TaskCompletionSource _latest = new();

        public List<TimeSpan> Requested { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            // 最初の描画（待ち時間なし）は、テストで操作しない
            Requested.Add(delay);
            _latest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = _latest;
            cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
            return source.Task;
        }

        public async Task ReleaseLatestAsync()
        {
            // 取り消された前の描画が終わって、最新の待機が始まるまで譲る
            while (_latest.Task.IsCompleted)
            {
                await Task.Yield();
            }

            _latest.SetResult();
        }
    }
}
