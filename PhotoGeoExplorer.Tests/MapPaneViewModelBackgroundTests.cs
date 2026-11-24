using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Xunit;

#pragma warning disable CA2000 // 地図とレイヤーの所有権は viewModel に移り、viewModel.Cleanup で破棄される。

namespace PhotoGeoExplorer.Tests;

public sealed class MapPaneViewModelBackgroundTests
{
    [Fact]
    public void SwitchTileSourceReplacesBackgroundLayersBelowTheMarkerLayer()
    {
        var service = new FakeBackgroundMapPaneService();
        var viewModel = CreateInitializedViewModel(service, new RecordingImageService(), out var map, out var marker);

        try
        {
            viewModel.SwitchTileSource(MapTileSourceType.OpenStreetMap);
            var first = Assert.Single(service.Created);
            Assert.Equal<ILayer>([.. first.Layers, marker], map.Layers.ToArray());

            viewModel.SwitchTileSource(MapTileSourceType.EsriWorldImagery);
            var second = service.Created[1];

            Assert.Equal<ILayer>([.. second.Layers, marker], map.Layers.ToArray());
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(0, second.DisposeCount);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void SwitchTileSourceAttachesTheNewBackgroundToTheMapWithTheLatestPixelDensity()
    {
        var service = new FakeBackgroundMapPaneService();
        var viewModel = CreateInitializedViewModel(service, new RecordingImageService(), out var map, out _);

        try
        {
            viewModel.UpdatePixelDensity(1.5);
            viewModel.SwitchTileSource(MapTileSourceType.OpenFreeMapVector);

            var background = Assert.Single(service.Created);
            Assert.Equal(1, background.AttachCount);
            Assert.Same(map, background.AttachedMap);
            Assert.Equal(1.5, background.GetPixelDensity!());

            // 密度の取得関数は、あとから更新された値を返す（背景が毎回の描画要求で読む）
            viewModel.UpdatePixelDensity(2.0);
            Assert.Equal(2.0, background.GetPixelDensity());
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void SwitchTileSourceRequestsTheSelectedSourceAndProvidesAWayToReachTheUiThread()
    {
        var service = new FakeBackgroundMapPaneService();
        var viewModel = CreateInitializedViewModel(service, new RecordingImageService(), out _, out _);

        try
        {
            viewModel.SwitchTileSource(MapTileSourceType.OpenFreeMapVector);

            var request = Assert.Single(service.Requests);
            Assert.Equal(MapTileSourceType.OpenFreeMapVector, request.SourceType);
            Assert.Equal(MapTileSourceType.OpenFreeMapVector, viewModel.CurrentTileSource);
            // UI スレッドのディスパッチャーが無い環境では、渡せないことを false で返す（例外にしない）
            Assert.False(Assert.IsAssignableFrom<Func<Action, bool>>(request.TryPostToUi)(() => { }));
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void PixelDensityChangeIsForwardedOnlyToTheCurrentBackgroundAndOnlyWhenTheValueChanges()
    {
        var service = new FakeBackgroundMapPaneService();
        var viewModel = CreateInitializedViewModel(service, new RecordingImageService(), out _, out _);

        try
        {
            viewModel.SwitchTileSource(MapTileSourceType.OpenStreetMap);
            viewModel.SwitchTileSource(MapTileSourceType.OpenFreeMapVector);
            var replaced = service.Created[0];
            var current = service.Created[1];

            viewModel.UpdatePixelDensity(1.5);
            viewModel.UpdatePixelDensity(1.5);
            viewModel.UpdatePixelDensity(2.0);

            Assert.Equal(2, current.PixelDensityChangedCount);
            Assert.Equal(0, replaced.PixelDensityChangedCount);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void InvalidPixelDensityIsIgnored(double invalid)
    {
        var service = new FakeBackgroundMapPaneService();
        var viewModel = CreateInitializedViewModel(service, new RecordingImageService(), out _, out _);

        try
        {
            viewModel.SwitchTileSource(MapTileSourceType.OpenFreeMapVector);
            var background = Assert.Single(service.Created);

            viewModel.UpdatePixelDensity(invalid);

            Assert.Equal(0, background.PixelDensityChangedCount);
            // 既定の 1.0 のまま
            Assert.Equal(1.0, background.GetPixelDensity!());
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void PixelDensityUpdateBeforeAnyBackgroundExistsIsKeptForTheFirstBackground()
    {
        var service = new FakeBackgroundMapPaneService();
        var viewModel = CreateInitializedViewModel(service, new RecordingImageService(), out _, out _);

        try
        {
            viewModel.UpdatePixelDensity(1.75);
            viewModel.SwitchTileSource(MapTileSourceType.OpenFreeMapVector);

            Assert.Equal(1.75, Assert.Single(service.Created).GetPixelDensity!());
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void BackgroundBusyChangesAreForwardedOnlyFromTheCurrentBackground()
    {
        var service = new FakeBackgroundMapPaneService();
        var viewModel = CreateInitializedViewModel(service, new RecordingImageService(), out _, out _);
        var raised = 0;
        viewModel.BackgroundBusyChanged += (_, _) => raised++;

        try
        {
            viewModel.SwitchTileSource(MapTileSourceType.OpenStreetMap);
            var first = service.Created[0];
            viewModel.SwitchTileSource(MapTileSourceType.EsriWorldImagery);
            var second = service.Created[1];

            first.SetBusy(true);
            Assert.Equal(0, raised);
            Assert.False(viewModel.IsBackgroundBusy);

            second.SetBusy(true);
            Assert.Equal(1, raised);
            Assert.True(viewModel.IsBackgroundBusy);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void CleanupDisposesTheBackground()
    {
        var service = new FakeBackgroundMapPaneService();
        var viewModel = CreateInitializedViewModel(service, new RecordingImageService(), out _, out _);
        viewModel.SwitchTileSource(MapTileSourceType.OpenStreetMap);
        var background = Assert.Single(service.Created);

        viewModel.Cleanup();

        Assert.Equal(1, background.DisposeCount);
        Assert.False(viewModel.IsBackgroundBusy);
    }

    [Fact]
    public async Task CaptureMapImageRendersExportLayersInPlaceOfTheBackgroundLayers()
    {
        var service = new FakeBackgroundMapPaneService();
        var imageService = new RecordingImageService();
        var viewModel = CreateInitializedViewModel(service, imageService, out var map, out var marker);
        var viewport = new Viewport(10, 20, 2, 0, 640, 480);

        try
        {
            viewModel.SwitchTileSource(MapTileSourceType.OpenStreetMap);
            var background = Assert.Single(service.Created);

            using var stream = await viewModel.CaptureMapImageAsync(viewport, 1.5f, CancellationToken.None).ConfigureAwait(true);

            Assert.Same(map, imageService.Map);
            Assert.Equal<ILayer>([.. background.ExportLayers, marker], imageService.Layers!.ToArray());
            Assert.Equal(viewport, imageService.Viewport);
            Assert.Equal(1.5f, imageService.PixelDensity);
            Assert.Equal(viewport, background.ExportViewport);
            Assert.Equal(1.5f, background.ExportPixelDensity);
            Assert.Equal(1, background.ExportDisposeCount);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public async Task CaptureMapImageThrowsWhenTheMapIsNotInitialized()
    {
        var viewModel = new MapPaneViewModel(
            new FakeBackgroundMapPaneService(),
            new WorkspaceState(),
            new RecordingImageService(),
            static () => null,
            new InMemorySecretStore());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.CaptureMapImageAsync(new Viewport(0, 0, 1, 0, 100, 50), 1f, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ViewControllerTracksFrameRenderBusyTileSourceAndSetViewAsync()
    {
        var service = new FakeBackgroundMapPaneService();
        var dispatcher = new RecordingUiDispatcher();
        var viewModel = new MapPaneViewModel(
            service,
            new WorkspaceState(),
            new RecordingImageService(),
            static () => null,
            new InMemorySecretStore(),
            uiDispatcher: dispatcher);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        try
        {
            typeof(MapPaneViewModel).GetMethod("InitializeMapCore", flags)!.Invoke(viewModel, null);
            typeof(MapPaneViewModel).GetField("_isMapInitialized", flags)!.SetValue(viewModel, true);
            var map = Assert.IsType<Mapsui.Map>(viewModel.Map);
            map.Navigator.SetSize(800, 600);
            var initialBackground = Assert.Single(service.Created);

            // Busy → 非Busy だけでは RenderedSeq は増えない
            initialBackground.SetBusy(true);
            Assert.True(viewModel.ViewController.CurrentStatus.IsBusy);
            initialBackground.SetBusy(false);
            Assert.False(viewModel.ViewController.CurrentStatus.IsBusy);
            Assert.Equal(0, viewModel.ViewController.CurrentStatus.RenderedSeq);

            // FrameRendered は BeginMapRender → 描画完了経由で採番される
            var initialCompleted = viewModel.BeginMapRender(map.Navigator.Viewport, map.Layers.ToArray(), map.RenderService);
            Assert.NotNull(initialCompleted);
            Assert.Equal(0, viewModel.ViewController.CurrentStatus.RenderedSeq);
            initialCompleted!();
            Assert.Equal(1, viewModel.ViewController.CurrentStatus.RenderedSeq);

            // SetViewAsync は注入した IUiDispatcher 経由で UI スレッド実行される
            var moveResult = await viewModel.ViewController.SetViewAsync(new MapViewTarget(43.768436, 142.362436, 17.5)).ConfigureAwait(true);
            Assert.True(moveResult.Succeeded);
            Assert.Equal(17, moveResult.Status.StyleZoom);
            Assert.Equal("z17.5", viewModel.ZoomIndicator.ZoomText);
            Assert.Equal(1, dispatcher.RunAsyncCount);
            Assert.True(dispatcher.TryEnqueueCount >= 1);

            viewModel.UpdatePixelDensity(1.5);
            Assert.Equal(1.5, viewModel.ViewController.CurrentStatus.PixelDensity);
            Assert.Equal(1200, viewModel.ViewController.CurrentStatus.ViewportSize.PhysicalWidth);
            Assert.NotNull(viewModel.ZoomIndicator.ZoomToolTip);

            viewModel.SwitchTileSource(MapTileSourceType.OpenFreeMapVector);
            Assert.Equal("OpenFreeMapVector", viewModel.ViewController.CurrentStatus.TileSource.Displayed);
            Assert.Equal("z17.5", viewModel.ZoomIndicator.ZoomText);
            Assert.NotNull(viewModel.ZoomIndicator.ZoomToolTip);
            var nextBackground = service.Created[1];
            var nextCompleted = viewModel.BeginMapRender(map.Navigator.Viewport, map.Layers.ToArray(), map.RenderService);
            Assert.NotNull(nextCompleted);
            nextCompleted!();
            Assert.Equal(2, viewModel.ViewController.CurrentStatus.RenderedSeq);
            Assert.Equal(1, initialBackground.BeginMapRenderCount);
            Assert.Equal(1, nextBackground.BeginMapRenderCount);

            // 旧背景からの通知は無視される
            initialBackground.RaiseFrameRendered();
            Assert.Equal(2, viewModel.ViewController.CurrentStatus.RenderedSeq);
        }
        finally
        {
            viewModel.Cleanup();
        }

        Assert.False(viewModel.ViewController.CurrentStatus.HasValidViewport);
    }

    private static MapPaneViewModel CreateInitializedViewModel(
        FakeBackgroundMapPaneService service,
        RecordingImageService imageService,
        out Mapsui.Map map,
        out MemoryLayer marker)
    {
        var viewModel = new MapPaneViewModel(service, new WorkspaceState(), imageService, static () => null, new InMemorySecretStore());
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        map = new Mapsui.Map();
        marker = new MemoryLayer { Name = "PhotoMarkers", Features = Array.Empty<IFeature>() };
        map.Layers.Add(marker);
        typeof(MapPaneViewModel).GetField("_map", flags)!.SetValue(viewModel, map);
        typeof(MapPaneViewModel).GetField("_markerLayer", flags)!.SetValue(viewModel, marker);
        typeof(MapPaneViewModel).GetField("_isMapInitialized", flags)!.SetValue(viewModel, true);
        return viewModel;
    }

    private sealed class FakeBackground : IMapBackground
    {
        private bool _busy;

        public List<ILayer> Layers { get; } = [new MemoryLayer { Name = "background" }];

        public List<ILayer> ExportLayers { get; } = [new MemoryLayer { Name = "export" }];

        public int DisposeCount { get; private set; }

        public int ExportDisposeCount { get; set; }

        public Viewport ExportViewport { get; private set; }

        public float ExportPixelDensity { get; private set; }

        public Mapsui.Map? AttachedMap { get; private set; }

        public Func<double>? GetPixelDensity { get; private set; }

        public int AttachCount { get; private set; }

        public int PixelDensityChangedCount { get; private set; }

        public int BeginMapRenderCount { get; private set; }

        IReadOnlyList<ILayer> IMapBackground.Layers => Layers;

        public string AttributionText => string.Empty;

        public bool IsBusy => _busy;

        public event EventHandler? BusyChanged;

        public event EventHandler? FrameRendered;

        public void SetBusy(bool busy)
        {
            _busy = busy;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseFrameRendered() => FrameRendered?.Invoke(this, EventArgs.Empty);

        public void Attach(Mapsui.Map map, Func<double> getPixelDensity)
        {
            AttachCount++;
            AttachedMap = map;
            GetPixelDensity = getPixelDensity;
        }

        public void OnPixelDensityChanged() => PixelDensityChangedCount++;

        public Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService)
        {
            BeginMapRenderCount++;
            return RaiseFrameRendered;
        }

        public Task<IMapExportLayers> CreateExportLayersAsync(Viewport viewport, float pixelDensity, CancellationToken cancellationToken)
        {
            ExportViewport = viewport;
            ExportPixelDensity = pixelDensity;
            return Task.FromResult<IMapExportLayers>(new FakeExportLayers(this));
        }

        public void Dispose() => DisposeCount++;

        private sealed class FakeExportLayers(FakeBackground owner) : IMapExportLayers
        {
            public IReadOnlyList<ILayer> Layers => owner.ExportLayers;

            public void Dispose() => owner.ExportDisposeCount++;
        }
    }

    private sealed class FakeBackgroundMapPaneService : IMapPaneService
    {
        public List<FakeBackground> Created { get; } = [];

        public List<MapBackgroundRequest> Requests { get; } = [];

        public (Mapsui.Map Map, IMapBackground Background, MemoryLayer MarkerLayer) InitializeMap(MapBackgroundRequest background)
        {
            Requests.Add(background);
            var created = new FakeBackground();
            Created.Add(created);
            var map = new Mapsui.Map();
            foreach (var layer in created.Layers)
            {
                map.Layers.Add(layer);
            }

            var marker = new MemoryLayer { Name = "PhotoMarkers", Features = Array.Empty<IFeature>() };
            map.Layers.Add(marker);
            return (map, created, marker);
        }

        public IMapBackground CreateBackground(MapBackgroundRequest background)
        {
            Requests.Add(background);
            var created = new FakeBackground();
            Created.Add(created);
            return created;
        }

        public Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
            IReadOnlyList<PhotoListItem> items,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public string GetTileCacheRootDirectory() => Path.GetTempPath();

        public string GetPinImagePath(PhotoMetadata metadata) => throw new NotSupportedException();

        public bool FileExistsAtPath(string path) => false;
    }

    private sealed class RecordingImageService : IMapImageService
    {
        public Mapsui.Map? Map { get; private set; }

        public IReadOnlyList<ILayer>? Layers { get; private set; }

        public Viewport Viewport { get; private set; }

        public float PixelDensity { get; private set; }

        public string CreateDefaultFileName(DateTimeOffset timestamp) => "map.png";

        public Stream RenderPng(Mapsui.Map map, IReadOnlyList<ILayer> layers, Viewport viewport, float pixelDensity)
        {
            Map = map;
            Layers = layers;
            Viewport = viewport;
            PixelDensity = pixelDensity;
            return new MemoryStream();
        }

        public Task SavePngAsync(Stream pngStream, string filePath, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public bool IsAvailable => true;

        public int RunAsyncCount { get; private set; }

        public int TryEnqueueCount { get; private set; }

        public Task RunAsync(Action action)
        {
            RunAsyncCount++;
            action();
            return Task.CompletedTask;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => asyncFunc();

        public bool TryEnqueue(Action action)
        {
            TryEnqueueCount++;
            action();
            return true;
        }

        public IUiDispatcherTimer? CreateTimer() => null;
    }
}
