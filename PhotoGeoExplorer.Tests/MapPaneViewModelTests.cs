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
using PhotoGeoExplorer.Services;
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
        Assert.False(viewModel.IsMapImageSaving);
        Assert.False(viewModel.CanSaveMapImage);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.StatusVisibility);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public async Task CanSaveMapImageReflectsRequiredState(
        bool isMapInitialized,
        bool hasValidViewport,
        bool isTileLoading,
        bool expected)
    {
        // Arrange
        var viewModel = new MapPaneViewModel();
        if (isMapInitialized)
        {
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }

        viewModel.UpdateMapImageSaveState(hasValidViewport, isTileLoading);

        Assert.Equal(expected, viewModel.CanSaveMapImage);
        viewModel.Cleanup();
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
    public async Task SaveMapImageAsyncSavesOnceAndPublishesDestination()
    {
        // Arrange
        var (viewModel, workspaceState, imageService) = await CreateReadyMapImageViewModelAsync(
            Path.GetTempPath()).ConfigureAwait(true);
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pickerCallCount = 0;
        string? notification = null;
        InfoBarSeverity? severity = null;
        workspaceState.NotificationRequested += (_, args) =>
        {
            notification = args.Message;
            severity = args.Severity;
        };

        var firstSave = viewModel.SaveMapImageAsync(
            (options, _) =>
            {
                pickerCallCount++;
                Assert.Equal(Path.GetTempPath(), options.SuggestedStartFolder);
                Assert.Equal("map.png", options.SuggestedFileName);
                return Task.FromResult<string?>(@"C:\Exports\map.png");
            },
            async _ =>
            {
                captureStarted.SetResult();
                await completeCapture.Task.ConfigureAwait(true);
                return new MemoryStream([1, 2, 3]);
            });
        await captureStarted.Task.ConfigureAwait(true);
        var duplicateSave = viewModel.SaveMapImageAsync(
            (_, _) => throw new InvalidOperationException("The duplicate picker must not run."),
            _ => throw new InvalidOperationException("The duplicate capture must not run."));

        Assert.True(viewModel.IsMapImageSaving);
        Assert.False(viewModel.CanSaveMapImage);
        await duplicateSave.ConfigureAwait(true);
        completeCapture.SetResult();
        await firstSave.ConfigureAwait(true);

        Assert.Equal(1, pickerCallCount);
        Assert.Equal(@"C:\Exports\map.png", imageService.SavedFilePath);
        Assert.Contains(@"C:\Exports\map.png", notification, StringComparison.Ordinal);
        Assert.Equal(InfoBarSeverity.Success, severity);
        Assert.False(viewModel.IsMapImageSaving);
        Assert.True(viewModel.CanSaveMapImage);
        viewModel.Cleanup();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveMapImageAsyncTreatsPickerAndOperationCancellationAsInformational(bool cancelDuringCapture)
    {
        // Arrange
        var (viewModel, workspaceState, imageService) = await CreateReadyMapImageViewModelAsync().ConfigureAwait(true);
        InfoBarSeverity? severity = null;
        workspaceState.NotificationRequested += (_, args) => severity = args.Severity;

        var saveTask = viewModel.SaveMapImageAsync(
            (_, _) => Task.FromResult<string?>(cancelDuringCapture ? @"C:\Exports\map.png" : null),
            async cancellationToken =>
            {
                viewModel.Cleanup();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(true);
                return new MemoryStream();
            });
        await saveTask.ConfigureAwait(true);

        Assert.Equal(InfoBarSeverity.Informational, severity);
        Assert.Null(imageService.SavedFilePath);
        viewModel.Cleanup();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveMapImageAsyncPublishesFailureAndRethrowsOriginalException(bool failDuringSave)
    {
        // Arrange
        var (viewModel, workspaceState, imageService) = await CreateReadyMapImageViewModelAsync().ConfigureAwait(true);
        var expectedException = failDuringSave
            ? new IOException("destination is not writable")
            : (Exception)new InvalidOperationException("snapshot failed");
        imageService.SaveFailure = failDuringSave ? expectedException : null;
        string? notification = null;
        InfoBarSeverity? severity = null;
        workspaceState.NotificationRequested += (_, args) =>
        {
            notification = args.Message;
            severity = args.Severity;
        };

        var actualException = await Assert.ThrowsAnyAsync<Exception>(
            () => viewModel.SaveMapImageAsync(
                (_, _) => Task.FromResult<string?>(@"C:\Exports\map.png"),
                _ => failDuringSave
                    ? Task.FromResult<Stream>(new MemoryStream([1, 2, 3]))
                    : Task.FromException<Stream>(expectedException))).ConfigureAwait(true);

        Assert.Same(expectedException, actualException);
        Assert.Contains(expectedException.Message, notification, StringComparison.Ordinal);
        Assert.Equal(InfoBarSeverity.Error, severity);
        Assert.False(viewModel.IsMapImageSaving);
        viewModel.Cleanup();
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
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

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

    [Fact]
    public async Task UpdateMarkersFromSelectionAsyncDoesNotThrowWhenCleanupHappensDuringLoad()
    {
        // Arrange
        var service = new CleanupDuringLoadMapPaneService();
        var viewModel = new MapPaneViewModel(service);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        var photo = new PhotoListItem(
            new PhotoItem(@"C:\Photos\selected.jpg", 1, DateTimeOffset.UtcNow, isFolder: false),
            thumbnail: null);

        // Act
        var updateTask = viewModel.UpdateMarkersFromSelectionAsync(new[] { photo });

        var loadStarted = await Task.WhenAny(service.LoadStarted, Task.Delay(1000)).ConfigureAwait(true);
        Assert.Same(service.LoadStarted, loadStarted);

        // await 中に Cleanup() を発生させ、_map/_markerLayer を破棄・null 化する
        viewModel.Cleanup();
        service.CompleteLoad(new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 35.681236, 139.767125));

        // Assert: 破棄済みの map/markerLayer へアクセスせず例外なく完了すること
        var completedTask = await Task.WhenAny(updateTask, Task.Delay(1000)).ConfigureAwait(true);
        Assert.Same(updateTask, completedTask);
        await updateTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateMarkersFromSelectionAsyncDoesNotUpdateDisposedLayerWhenCleanupHappensAfterLoadCompletes()
    {
        // Arrange
        MapPaneViewModel? viewModel = null;
        var service = new CleanupAfterLoadMapPaneService(() => viewModel!.Cleanup());
        viewModel = new MapPaneViewModel(service);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        var markerLayer = SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        var photo = new PhotoListItem(
            new PhotoItem(@"C:\Photos\selected.jpg", 1, DateTimeOffset.UtcNow, isFolder: false),
            thumbnail: null);

        // Act: LoadPhotoMetadataAsync が結果を返す直前(Presenter 呼び出し前)に Cleanup() が走る
        await viewModel.UpdateMarkersFromSelectionAsync(new[] { photo }).ConfigureAwait(true);

        // Assert: 破棄済みレイヤーへマーカーが書き込まれていない(no-op)こと
        Assert.Empty(markerLayer.Features);
        Assert.Null(viewModel.Map);
    }

    private static MemoryLayer SetMapInitializedForTest(MapPaneViewModel viewModel)
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
        return markerLayer;
    }

    private static async Task<(
        MapPaneViewModel ViewModel,
        WorkspaceState WorkspaceState,
        RecordingMapImageService ImageService)> CreateReadyMapImageViewModelAsync(string? currentFolderPath = null)
    {
        var workspaceState = new WorkspaceState { CurrentFolderPath = currentFolderPath };
        var imageService = new RecordingMapImageService();
        var viewModel = new MapPaneViewModel(new MapPaneService(), workspaceState, imageService);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.UpdateMapImageSaveState(hasValidViewport: true, isTileLoading: false);
        return (viewModel, workspaceState, imageService);
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

        public string GetPinImagePath(PhotoMetadata metadata)
            => Path.Combine(Path.GetTempPath(), "red_pin.png");

        public bool FileExistsAtPath(string path)
            => false;
    }

    private sealed class RecordingMapImageService : IMapImageService
    {
        public string? SavedFilePath { get; private set; }

        public Exception? SaveFailure { get; set; }

        public string CreateDefaultFileName(DateTimeOffset timestamp) => "map.png";

        public Stream RenderPng(Map map, Viewport viewport, float pixelDensity)
            => throw new NotSupportedException();

        public Task SavePngAsync(Stream pngStream, string filePath, CancellationToken cancellationToken)
        {
            if (SaveFailure is not null)
            {
                return Task.FromException(SaveFailure);
            }

            SavedFilePath = filePath;
            return Task.CompletedTask;
        }
    }

    private sealed class CleanupDuringLoadMapPaneService : IMapPaneService
    {
        private readonly TaskCompletionSource _loadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<PhotoMetadata?> _loadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task LoadStarted => _loadStarted.Task;

        public void CompleteLoad(PhotoMetadata? metadata) => _loadCompletion.TrySetResult(metadata);

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

        public async Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
            IReadOnlyList<PhotoListItem> items,
            CancellationToken cancellationToken)
        {
            _loadStarted.TrySetResult();
            var metadata = await _loadCompletion.Task.ConfigureAwait(false);
            return items.Select(item => (item, metadata)).ToArray();
        }

        public string GetTileCacheRootDirectory()
            => Path.GetTempPath();

        public string GetPinImagePath(PhotoMetadata metadata)
            => Path.Combine(Path.GetTempPath(), "red_pin.png");

        public bool FileExistsAtPath(string path)
            => false;
    }

    private sealed class CleanupAfterLoadMapPaneService : IMapPaneService
    {
        private readonly Action _onLoadCompleting;

        public CleanupAfterLoadMapPaneService(Action onLoadCompleting)
        {
            _onLoadCompleting = onLoadCompleting;
        }

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
            var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, latitude: 35.681236, longitude: 139.767125);

            // LoadPhotoMetadataAsync が結果を返す直前(= UpdateMarkersFromSelectionAsync の
            // 続きの処理が実行される直前)に Cleanup() を発生させる
            _onLoadCompleting();

            var metadataItems = items
                .Select(item => (item, (PhotoMetadata?)metadata))
                .ToArray();
            return Task.FromResult<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>>(metadataItems);
        }

        public string GetTileCacheRootDirectory()
            => Path.GetTempPath();

        public string GetPinImagePath(PhotoMetadata metadata)
            => Path.Combine(Path.GetTempPath(), "red_pin.png");

        public bool FileExistsAtPath(string path)
            => false;
    }
}
