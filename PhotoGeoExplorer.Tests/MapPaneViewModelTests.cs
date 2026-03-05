using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// MapPaneViewModel のテスト
/// </summary>
public class MapPaneViewModelTests
{
    [Fact]
    public void ConstructorThrowsWhenServiceIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MapPaneViewModel(null!));
    }

    [Fact]
    public void ConstructorSetsTitle()
    {
        // Arrange & Act
        var viewModel = new MapPaneViewModel();

        // Assert
        Assert.Equal("Map", viewModel.Title);
    }

    [Fact]
    public void ConstructorThrowsWhenWorkspaceStateIsNull()
    {
        // Arrange
        var service = new MapPaneService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MapPaneViewModel(service, null!));
    }

    [Fact]
    public void InitialStateIsCorrect()
    {
        // Arrange & Act
        var viewModel = new MapPaneViewModel();

        // Assert
        Assert.False(viewModel.IsMapInitialized);
        Assert.Null(viewModel.Map);
        Assert.Equal(MapTileSourceType.OpenStreetMap, viewModel.CurrentTileSource);
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.StatusVisibility);
    }

    [Fact]
    public async Task InitializeAsyncCompletesWithoutError()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act & Assert (テスト環境では UI スレッドがないため、初期化はスキップされる)
        await viewModel.InitializeAsync().ConfigureAwait(true);
        Assert.True(viewModel.IsMapInitialized);
    }

    [Fact]
    public void MapDefaultZoomLevelNormalizesInvalidValues()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act
        viewModel.MapDefaultZoomLevel = 15; // 無効な値（8, 10, 12, 14, 16, 18 のみ有効）

        // Assert
        Assert.Equal(14, viewModel.MapDefaultZoomLevel); // デフォルト値に戻る
    }

    [Fact]
    public void MapDefaultZoomLevelAcceptsValidValues()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act & Assert
        viewModel.MapDefaultZoomLevel = 8;
        Assert.Equal(8, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 10;
        Assert.Equal(10, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 12;
        Assert.Equal(12, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 14;
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 16;
        Assert.Equal(16, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 18;
        Assert.Equal(18, viewModel.MapDefaultZoomLevel);
    }

    [Fact]
    public async Task UpdateMarkersFromSelectionAsyncThrowsWhenSelectedItemsIsNull()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await viewModel.UpdateMarkersFromSelectionAsync(null!).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public void CleanupDisposesResources()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act
        viewModel.Cleanup();

        // Assert
        Assert.Null(viewModel.Map);
    }

    [Fact]
    public void RequestPhotoFocusPublishesWorkspaceEvent()
    {
        // Arrange
        var workspaceState = new WorkspaceState();
        var viewModel = new MapPaneViewModel(new MapPaneService(), workspaceState);
        string? requestedPath = null;
        workspaceState.PhotoFocusRequested += (_, args) => requestedPath = args.FilePath;

        // Act
        viewModel.RequestPhotoFocus(@"C:\Photos\focus.jpg");

        // Assert
        Assert.Equal(@"C:\Photos\focus.jpg", requestedPath);
    }

    [Fact]
    public void RequestPhotoSelectionPublishesWorkspaceEvent()
    {
        // Arrange
        var workspaceState = new WorkspaceState();
        var viewModel = new MapPaneViewModel(new MapPaneService(), workspaceState);
        IReadOnlyList<string>? requestedPaths = null;
        workspaceState.PhotoSelectionRequested += (_, args) => requestedPaths = args.FilePaths;
        var selection = new List<string> { @"C:\Photos\a.jpg", @"C:\Photos\b.jpg" };

        // Act
        viewModel.RequestPhotoSelection(selection);

        // Assert
        Assert.NotNull(requestedPaths);
        Assert.Equal(2, requestedPaths!.Count);
    }

    [Fact]
    public void RequestNotificationPublishesWorkspaceEvent()
    {
        // Arrange
        var workspaceState = new WorkspaceState();
        var viewModel = new MapPaneViewModel(new MapPaneService(), workspaceState);
        string? requestedMessage = null;
        InfoBarSeverity? requestedSeverity = null;
        workspaceState.NotificationRequested += (_, args) =>
        {
            requestedMessage = args.Message;
            requestedSeverity = args.Severity;
        };

        // Act
        viewModel.RequestNotification("error", InfoBarSeverity.Error);

        // Assert
        Assert.Equal("error", requestedMessage);
        Assert.Equal(InfoBarSeverity.Error, requestedSeverity);
    }

    [Fact]
    public async Task WorkspaceSelectedPhotosChangeTriggersMarkerUpdate()
    {
        // Arrange
        var workspaceState = new WorkspaceState();
        var service = new WorkspaceSelectionMapPaneService();
        var viewModel = new MapPaneViewModel(service, workspaceState);
        SetMapInitializedForTest(viewModel);

        var photo = new PhotoListItem(
            new PhotoItem(@"C:\Photos\selected.jpg", 1, DateTimeOffset.UtcNow, isFolder: false),
            thumbnail: null);

        try
        {
            // Act
            workspaceState.SelectedPhotos = new[] { photo };

            var completed = await Task.WhenAny(service.LoadCalled, Task.Delay(1000)).ConfigureAwait(true);

            // Assert
            Assert.Same(service.LoadCalled, completed);
            Assert.Equal(1, service.LoadPhotoMetadataCallCount);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    private static void SetMapInitializedForTest(MapPaneViewModel viewModel)
    {
        var mapField = typeof(MapPaneViewModel).GetField("_map", BindingFlags.Instance | BindingFlags.NonPublic);
        var markerLayerField = typeof(MapPaneViewModel).GetField("_markerLayer", BindingFlags.Instance | BindingFlags.NonPublic);
        var isMapInitializedField = typeof(MapPaneViewModel).GetField("_isMapInitialized", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(mapField);
        Assert.NotNull(markerLayerField);
        Assert.NotNull(isMapInitializedField);

#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        var map = new Mapsui.Map();
        var markerLayer = new MemoryLayer
        {
            Features = Array.Empty<IFeature>()
        };
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        map.Layers.Add(markerLayer);

        mapField!.SetValue(viewModel, map);
        markerLayerField!.SetValue(viewModel, markerLayer);
        isMapInitializedField!.SetValue(viewModel, true);
    }

    private sealed class WorkspaceSelectionMapPaneService : IMapPaneService
    {
        private readonly TaskCompletionSource<bool> _loadCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LoadPhotoMetadataCallCount { get; private set; }

        public Task LoadCalled => _loadCalled.Task;

        public (Mapsui.Map Map, TileLayer TileLayer, MemoryLayer MarkerLayer) InitializeMap(
            MapTileSourceType tileSource,
            string userAgent)
        {
            throw new NotSupportedException();
        }

        public TileLayer CreateTileLayer(MapTileSourceType sourceType, string userAgent)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
            IReadOnlyList<PhotoListItem> items,
            CancellationToken cancellationToken)
        {
            LoadPhotoMetadataCallCount++;
            _loadCalled.TrySetResult(true);
            var metadataItems = items
                .Select(item => (item, (PhotoMetadata?)new PhotoMetadata(
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    latitude: 35.681236,
                    longitude: 139.767125)))
                .ToArray();
            return Task.FromResult<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>>(metadataItems);
        }

        public string GetTileCacheRootDirectory()
        {
            return Path.GetTempPath();
        }
    }
}
