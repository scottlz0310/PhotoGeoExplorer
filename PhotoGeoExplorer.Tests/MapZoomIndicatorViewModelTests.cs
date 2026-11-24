using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoGeoExplorer.State;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapZoomIndicatorViewModelTests
{
    private static readonly Dictionary<string, string> JapaneseResources = LoadResources("ja-JP");
    private static readonly Dictionary<string, string> EnglishResources = LoadResources("en-US");

    [Theory]
    [InlineData("ja-JP", 0.0, "z0.0", 0)]
    [InlineData("ja-JP", 14.0, "z14.0", 14)]
    [InlineData("ja-JP", 16.24, "z16.2", 16)]
    [InlineData("ja-JP", 18.585, "z18.6", 18)]
    [InlineData("en-US", 18.585, "z18.6", 18)]
    [InlineData("en-US", 19.0, "z19.0", 19)]
    public void FormatsLogicalZoomMatchingMapDefaultZoomScale(
        string locale,
        double logicalZoom,
        string expectedLabel,
        int expectedStyleZoom)
    {
        var resolution = WebMercator.ResolutionFromLogicalZoom(logicalZoom);
        var status = CreateStatus(resolution: resolution, pixelDensity: 1.5, tileSource: MapTileSourceType.OpenFreeMapVector);
        var controller = new FakeMapViewController(status);
        var settings = new MapHeaderDisplaySettings();
        var dispatcher = new TestUiDispatcher();

        using var viewModel = new MapZoomIndicatorViewModel(controller, settings, dispatcher, getLocalizedString: CreateLocalizer(locale));

        Assert.Equal(expectedLabel, viewModel.ZoomText);
        Assert.Equal(Visibility.Visible, viewModel.ZoomVisibility);
        Assert.NotNull(viewModel.ZoomToolTip);
        Assert.Contains($"z{expectedStyleZoom}", viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains("150%", viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains("1.50x", viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains("OpenFreeMap", viewModel.ZoomToolTip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0, "100%", "1.00x")]
    [InlineData(1.25, "125%", "1.25x")]
    [InlineData(1.5, "150%", "1.50x")]
    [InlineData(2.0, "200%", "2.00x")]
    public void ToolTipShowsPixelDensityAndStyleZoomIndependentlyOfDpi(
        double pixelDensity,
        string expectedPercent,
        string expectedRatio)
    {
        var resolution = WebMercator.ResolutionFromLogicalZoom(17.4);
        var status = CreateStatus(resolution: resolution, pixelDensity: pixelDensity, tileSource: MapTileSourceType.OpenFreeMapVector);
        var controller = new FakeMapViewController(status);
        var settings = new MapHeaderDisplaySettings();
        var dispatcher = new TestUiDispatcher();

        using var viewModel = new MapZoomIndicatorViewModel(controller, settings, dispatcher, getLocalizedString: CreateLocalizer("ja-JP"));

        Assert.Equal("z17.4", viewModel.ZoomText);
        Assert.NotNull(viewModel.ZoomToolTip);
        Assert.Contains("z17", viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains(expectedPercent, viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains(expectedRatio, viewModel.ZoomToolTip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ja-JP", MapTileSourceType.OpenStreetMap, MapTileSourceType.OpenStreetMap, "OpenStreetMap")]
    [InlineData("ja-JP", MapTileSourceType.EsriWorldImagery, MapTileSourceType.EsriWorldImagery, "Esri")]
    [InlineData("ja-JP", MapTileSourceType.MapboxSatellite, MapTileSourceType.MapboxSatellite, "Mapbox")]
    [InlineData("ja-JP", MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector, "OpenFreeMap")]
    [InlineData("en-US", MapTileSourceType.OpenFreeMapVector, MapTileSourceType.OpenFreeMapVector, "OpenFreeMap (Vector)")]
    internal void ToolTipShowsTileSourceName(
        string locale,
        MapTileSourceType configured,
        MapTileSourceType displayed,
        string expectedName)
    {
        var status = CreateStatus(
            resolution: WebMercator.ResolutionFromLogicalZoom(15.0),
            pixelDensity: 1.0,
            tileSource: displayed,
            configuredTileSource: configured);
        var controller = new FakeMapViewController(status);
        var settings = new MapHeaderDisplaySettings();

        using var viewModel = new MapZoomIndicatorViewModel(controller, settings, new TestUiDispatcher(), getLocalizedString: CreateLocalizer(locale));

        Assert.NotNull(viewModel.ZoomToolTip);
        Assert.Contains(expectedName, viewModel.ZoomToolTip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("en-US")]
    public void ToolTipShowsBothDisplayedAndConfiguredSourceWhenFallbackIsActive(string locale)
    {
        var status = CreateStatus(
            resolution: WebMercator.ResolutionFromLogicalZoom(15.0),
            pixelDensity: 1.0,
            tileSource: MapTileSourceType.OpenStreetMap,
            configuredTileSource: MapTileSourceType.MapboxSatellite);
        var controller = new FakeMapViewController(status);
        var settings = new MapHeaderDisplaySettings();

        using var viewModel = new MapZoomIndicatorViewModel(controller, settings, new TestUiDispatcher(), getLocalizedString: CreateLocalizer(locale));

        Assert.NotNull(viewModel.ZoomToolTip);
        Assert.Contains("OpenStreetMap", viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains("Mapbox", viewModel.ZoomToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void HidesZoomWhenViewportIsInvalidOrSettingIsDisabled()
    {
        var controller = new FakeMapViewController(MapViewStatus.Empty);
        var settings = new MapHeaderDisplaySettings();
        var dispatcher = new TestUiDispatcher();

        using var viewModel = new MapZoomIndicatorViewModel(controller, settings, dispatcher);

        Assert.Equal(string.Empty, viewModel.ZoomText);
        Assert.Equal(Visibility.Collapsed, viewModel.ZoomVisibility);
        Assert.Null(viewModel.ZoomToolTip);

        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(16.0), 1.0, MapTileSourceType.OpenStreetMap));
        Assert.Equal("z16.0", viewModel.ZoomText);
        Assert.Equal(Visibility.Visible, viewModel.ZoomVisibility);
        Assert.NotNull(viewModel.ZoomToolTip);

        // 設定で OFF にすると即座に非表示になり、ON に戻すと最新値が復帰する
        settings.ShowZoom = false;
        Assert.Equal(string.Empty, viewModel.ZoomText);
        Assert.Equal(Visibility.Collapsed, viewModel.ZoomVisibility);
        Assert.Null(viewModel.ZoomToolTip);

        settings.ShowZoom = true;
        Assert.Equal("z16.0", viewModel.ZoomText);
        Assert.Equal(Visibility.Visible, viewModel.ZoomVisibility);
        Assert.NotNull(viewModel.ZoomToolTip);
    }

    [Fact]
    public void ThrottlesRapidUpdatesAndAppliesLatestValueOnTrailingEdge()
    {
        var clock = new ManualTimeProvider();
        var controller = new FakeMapViewController(MapViewStatus.Empty);
        var settings = new MapHeaderDisplaySettings();
        var dispatcher = new TestUiDispatcher();
        var observedZoomTexts = new List<string>();

        using var viewModel = new MapZoomIndicatorViewModel(
            controller,
            settings,
            dispatcher,
            clock,
            TimeSpan.FromMilliseconds(50),
            CreateLocalizer("ja-JP"));
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapZoomIndicatorViewModel.ZoomText))
            {
                observedZoomTexts.Add(viewModel.ZoomText);
            }
        };

        // 最初の更新は即時反映
        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(14.0), 1.0, MapTileSourceType.OpenStreetMap));
        Assert.Equal(["z14.0"], observedZoomTexts);

        // 50ms 以内の高頻度連続更新は間引かれ、タイマーが 1 回だけ起動する
        clock.Advance(TimeSpan.FromMilliseconds(10));
        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(14.5), 1.0, MapTileSourceType.OpenStreetMap));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(15.2), 1.0, MapTileSourceType.OpenStreetMap));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(16.8), 1.0, MapTileSourceType.OpenStreetMap));

        Assert.Equal(["z14.0"], observedZoomTexts);
        Assert.NotNull(dispatcher.Timer);
        Assert.True(dispatcher.Timer.IsRunning);
        Assert.Equal(1, dispatcher.Timer.StartCount);

        // 間引き期間経過後の Tick で最終値（z16.8）が 1 回だけ反映される
        clock.Advance(TimeSpan.FromMilliseconds(25));
        dispatcher.Timer.RaiseTick();

        Assert.Equal(["z14.0", "z16.8"], observedZoomTexts);
        Assert.False(dispatcher.Timer.IsRunning);
    }

    [Fact]
    public async Task DispatchesBackgroundThreadNotificationsToUiDispatcherAndStopsAfterDisposeAsync()
    {
        var controller = new FakeMapViewController(MapViewStatus.Empty);
        var settings = new MapHeaderDisplaySettings();
        var dispatcher = new TestUiDispatcher(deferEnqueue: true);

        var viewModel = new MapZoomIndicatorViewModel(controller, settings, dispatcher, getLocalizedString: CreateLocalizer("ja-JP"));

        // 別スレッドからの通知は UI ディスパッチャーへ積まれるだけで直接プロパティを変更しない
        await Task.Run(() =>
        {
            controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(15.3), 1.0, MapTileSourceType.OpenStreetMap));
            controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(17.9), 1.0, MapTileSourceType.OpenStreetMap));
        }).ConfigureAwait(true);

        Assert.Equal(string.Empty, viewModel.ZoomText);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.FlushQueuedActions();
        Assert.Equal("z17.9", viewModel.ZoomText);

        // ズーム・密度・背景が変わらず IsBusy や RenderedSeq だけ変化した場合は PropertyChanged を発火しない
        var changedCount = 0;
        viewModel.PropertyChanged += (_, _) => changedCount++;
        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(17.9), 1.0, MapTileSourceType.OpenStreetMap, isBusy: true, renderedSeq: 5));
        dispatcher.FlushQueuedActions();
        Assert.Equal(0, changedCount);

        // Dispose 後は更新を受け付けない
        viewModel.Dispose();
        viewModel.Dispose();
        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(12.0), 1.0, MapTileSourceType.OpenStreetMap));
        settings.ShowZoom = false;
        dispatcher.FlushQueuedActions();
        Assert.Equal("z17.9", viewModel.ZoomText);
    }

    [Fact]
    public void EnablingShowZoomInterleavedWithQueuedStatusUpdatesKeepsLatestZoomAndToolTip()
    {
        var controller = new FakeMapViewController(MapViewStatus.Empty);
        var settings = new MapHeaderDisplaySettings { ShowZoom = false };
        var dispatcher = new TestUiDispatcher(deferEnqueue: true);

        using var viewModel = new MapZoomIndicatorViewModel(
            controller,
            settings,
            dispatcher,
            getLocalizedString: CreateLocalizer("ja-JP"));

        // 表示オフの状態から z14.0 通知 → 表示オン → z17.0 通知（DPI・背景も更新）の順にキューへ積む
        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(14.0), 1.0, MapTileSourceType.OpenStreetMap));
        settings.ShowZoom = true;
        controller.Publish(CreateStatus(WebMercator.ResolutionFromLogicalZoom(17.0), 1.5, MapTileSourceType.OpenFreeMapVector));

        dispatcher.FlushQueuedActions();

        Assert.Equal(17.0, controller.CurrentStatus.Zoom!.Value, precision: 6);
        Assert.Equal("z17.0", viewModel.ZoomText);
        Assert.Equal(Visibility.Visible, viewModel.ZoomVisibility);
        Assert.NotNull(viewModel.ZoomToolTip);
        Assert.Contains("17.0", viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains("z17", viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains("150%", viewModel.ZoomToolTip, StringComparison.Ordinal);
        Assert.Contains("OpenFreeMap", viewModel.ZoomToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsInvalidArguments()
    {
        var controller = new FakeMapViewController(MapViewStatus.Empty);
        var settings = new MapHeaderDisplaySettings();
        var dispatcher = new TestUiDispatcher();

        Assert.Throws<ArgumentNullException>(() => new MapZoomIndicatorViewModel(null!, settings, dispatcher));
        Assert.Throws<ArgumentNullException>(() => new MapZoomIndicatorViewModel(controller, null!, dispatcher));
        Assert.Throws<ArgumentNullException>(() => new MapZoomIndicatorViewModel(controller, settings, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapZoomIndicatorViewModel(controller, settings, dispatcher, throttleInterval: TimeSpan.FromMilliseconds(-1)));
    }

    private static Func<string, string> CreateLocalizer(string locale)
    {
        var table = locale == "ja-JP" ? JapaneseResources : EnglishResources;
        return key => table.TryGetValue(key, out var value) ? value : key;
    }

    private static Dictionary<string, string> LoadResources(string locale)
    {
        var path = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer", "Strings", locale, "Resources.resw");
        var document = XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static MapViewStatus CreateStatus(
        double resolution,
        double pixelDensity,
        MapTileSourceType tileSource,
        MapTileSourceType? configuredTileSource = null,
        bool isBusy = false,
        long renderedSeq = 1)
        => MapViewStatus.Create(
            hasSize: true,
            centerX: WebMercator.FromLongitude(142.362436),
            centerY: WebMercator.FromLatitude(43.768436),
            resolution: resolution,
            widthDip: 800,
            heightDip: 600,
            pixelDensity: pixelDensity,
            configuredTileSource: configuredTileSource ?? tileSource,
            displayedTileSource: tileSource,
            attributionText: "Test",
            isBusy: isBusy,
            renderedSeq: renderedSeq);

    private sealed class FakeMapViewController(MapViewStatus initialStatus) : IMapViewController
    {
        public MapViewStatus CurrentStatus { get; private set; } = initialStatus;

        public event EventHandler<MapViewStatus>? StatusChanged;

        public void Publish(MapViewStatus status)
        {
            CurrentStatus = status;
            StatusChanged?.Invoke(this, status);
        }

        public Task<MapViewCommandResult> SetViewAsync(MapViewTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult(new MapViewCommandResult(true, CurrentStatus));
    }

    private sealed class TestUiDispatcher(bool deferEnqueue = false) : IUiDispatcher
    {
        private readonly Queue<Action> _queue = new();

        public bool IsAvailable => true;

        public FakeTimer? Timer { get; private set; }

        public int PendingCount
        {
            get
            {
                lock (_queue)
                {
                    return _queue.Count;
                }
            }
        }

        public Task RunAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => asyncFunc();

        public bool TryEnqueue(Action action)
        {
            if (deferEnqueue)
            {
                lock (_queue)
                {
                    _queue.Enqueue(action);
                }

                return true;
            }

            action();
            return true;
        }

        public void FlushQueuedActions()
        {
            while (true)
            {
                Action? next;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }

                    next = _queue.Dequeue();
                }

                next();
            }
        }

        public IUiDispatcherTimer? CreateTimer()
        {
            Timer ??= new FakeTimer();
            return Timer;
        }
    }

    private sealed class FakeTimer : IUiDispatcherTimer
    {
        public TimeSpan Interval { get; set; }

        public bool IsRunning { get; private set; }

        public int StartCount { get; private set; }

        public event EventHandler? Tick;

        public void Start()
        {
            IsRunning = true;
            StartCount++;
        }

        public void Stop() => IsRunning = false;

        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
