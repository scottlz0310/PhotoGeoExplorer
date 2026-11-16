using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services;
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

        IReadOnlyList<ILayer> IMapBackground.Layers => Layers;

        public string AttributionText => string.Empty;

        public bool IsBusy => _busy;

        public event EventHandler? BusyChanged;

        public void SetBusy(bool busy)
        {
            _busy = busy;
            BusyChanged?.Invoke(this, EventArgs.Empty);
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

        public (Mapsui.Map Map, IMapBackground Background, MemoryLayer MarkerLayer) InitializeMap(MapBackgroundRequest background)
            => throw new NotSupportedException();

        public IMapBackground CreateBackground(MapBackgroundRequest background)
        {
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
}
