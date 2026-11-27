using System;
using System.Collections.Concurrent;
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
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoGeoExplorer.Services.AreaNames;
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
        Assert.Null(viewModel.Surface);
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
        var viewModel = CreateViewModelWithoutDispatcher();
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
        var viewModel = CreateViewModelWithoutDispatcher();

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
        var map = CreateReadyMapImageViewModel(Path.GetTempPath());
        var closeDialog = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pickerCallCount = 0;
        string? notification = null;
        InfoBarSeverity? severity = null;
        map.WorkspaceState.NotificationRequested += (_, args) =>
        {
            notification = args.Message;
            severity = args.Severity;
        };

        var firstSave = map.ViewModel.SaveMapImageAsync(
            async (export, _) =>
            {
                await ShowPreviewAsync(export).ConfigureAwait(true);
                return await closeDialog.Task.ConfigureAwait(true);
            },
            (options, _) =>
            {
                pickerCallCount++;
                Assert.Equal(Path.GetTempPath(), options.SuggestedStartFolder);
                Assert.Equal("map.png", options.SuggestedFileName);
                return Task.FromResult<string?>(@"C:\Exports\map.png");
            });
        var duplicateSave = map.ViewModel.SaveMapImageAsync(
            (_, _) => throw new InvalidOperationException("The duplicate dialog must not open."),
            (_, _) => throw new InvalidOperationException("The duplicate picker must not run."));

        Assert.True(map.ViewModel.IsMapImageSaving);
        Assert.False(map.ViewModel.CanSaveMapImage);
        await duplicateSave.ConfigureAwait(true);
        closeDialog.SetResult(true);
        await firstSave.ConfigureAwait(true);

        Assert.Equal(1, pickerCallCount);
        Assert.Equal(@"C:\Exports\map.png", map.ImageService.SavedFilePath);
        Assert.Contains(@"C:\Exports\map.png", notification, StringComparison.Ordinal);
        Assert.Equal(InfoBarSeverity.Success, severity);
        Assert.False(map.ViewModel.IsMapImageSaving);
        Assert.True(map.ViewModel.CanSaveMapImage);
        Assert.Equal(1, Assert.Single(map.Service.Exporters).DisposeCount);
        map.ViewModel.Cleanup();
    }

    [Fact]
    public async Task SaveMapImageAsyncStartsTheDialogFromTheDisplayedMap()
    {
        var map = CreateReadyMapImageViewModel();
        map.Surface.View = new MapViewState(true, 15000000, 4000000, WebMercator.ResolutionFromLogicalZoom(12), 640, 360);
        var marker = new MapMarker(
            35,
            139,
            new PhotoItem(@"C:\Photos\a.jpg", 0, DateTimeOffset.UnixEpoch, isFolder: false),
            new PhotoMetadata(null, null, null, 35, 139),
            PinImagePath: null);
        map.Surface.ShowMarker(marker, 12);
        map.ViewModel.UpdatePixelDensity(1.5);
        map.ViewModel.SwitchTileSource(MapTileSourceType.EsriWorldImagery);

        await map.ViewModel.SaveMapImageAsync(
            async (export, _) =>
            {
                await export.PreviewTask.ConfigureAwait(true);
                return false;
            },
            (_, _) => throw new InvalidOperationException("The picker must not run.")).ConfigureAwait(true);

        var request = Assert.Single(Assert.Single(map.Service.Exporters).Rendered);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, request.Background.SourceType);
        Assert.Equal((15000000d, 4000000d, 12d), (request.CenterX, request.CenterY, Math.Round(request.LogicalZoom, 6)));
        Assert.Equal((960, 540, 1.5), (request.PixelWidth, request.PixelHeight, request.PixelDensity));
        Assert.Same(marker, Assert.Single(request.Markers));
        map.ViewModel.Cleanup();
    }

    [Theory]
    [InlineData(false, null, MapTileSourceType.OpenStreetMap,
        new[] { 0, 1, 3, 8 })]
    [InlineData(true, null, MapTileSourceType.GsiPale,
        new[] { 0, 1, 3, 4, 5, 6, 7, 8 })]
    [InlineData(false, "test-token", MapTileSourceType.EsriWorldImagery,
        new[] { 0, 1, 2, 3, 8, 9 })]
    [InlineData(true, "test-token", MapTileSourceType.MapboxSatellite,
        new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 })]
    [InlineData(false, "test-token", MapTileSourceType.MapboxSatelliteRoads,
        new[] { 0, 1, 2, 3, 8, 9 })]
    internal async Task SaveMapImageAsyncOffersTheBackgroundsThatCanBeShown(
        bool japanMapSources, string? token, MapTileSourceType displayed, int[] expected)
    {
        var secrets = new InMemorySecretStore();
        if (token is not null)
        {
            secrets.Set(SecretKeys.MapboxAccessToken, token);
        }

        var map = CreateReadyMapImageViewModel(secretStore: secrets);
        map.ViewModel.IsJapanMapSourcesEnabled = () => japanMapSources;
        map.ViewModel.SwitchTileSource(displayed);
        var types = expected.Select(value => (MapTileSourceType)value).ToArray();

        await map.ViewModel.SaveMapImageAsync(
            async (export, _) =>
            {
                Assert.Equal(types.Select(type => MapBackgroundCatalog.Get(type).DisplayName), export.BackgroundChoices);
                Assert.Equal(Array.IndexOf(types, displayed), export.BackgroundIndex);
                for (var i = 0; i < types.Length; i++)
                {
                    export.BackgroundIndex = i;
                    export.Retry();
                    await export.PreviewTask.ConfigureAwait(true);
                }

                return false;
            },
            (_, _) => throw new InvalidOperationException("The picker must not run.")).ConfigureAwait(true);

        var requests = Assert.Single(map.Service.Exporters).Rendered.Select(request => request.Background).ToArray();
        Assert.All(types, type => Assert.Contains(requests, request => request.SourceType == type));
        Assert.All(requests, request => Assert.Equal(
            request.SourceType is MapTileSourceType.MapboxSatellite or MapTileSourceType.MapboxSatelliteRoads ? token : null,
            request.MapboxAccessToken));
        Assert.Equal(displayed, map.ViewModel.CurrentTileSource);
        map.ViewModel.Cleanup();
    }

    [Fact]
    public async Task SaveMapImageAsyncOffersTheFallbackWhenTheDisplayedBackgroundNeedsAMissingToken()
    {
        var map = CreateReadyMapImageViewModel();
        map.ViewModel.SwitchTileSource(MapTileSourceType.MapboxSatellite);

        await map.ViewModel.SaveMapImageAsync(
            (export, _) =>
            {
                Assert.Equal(MapBackgroundCatalog.Default.DisplayName, export.BackgroundChoices[export.BackgroundIndex]);
                Assert.DoesNotContain(
                    MapBackgroundCatalog.Get(MapTileSourceType.MapboxSatellite).DisplayName, export.BackgroundChoices);
                return Task.FromResult(false);
            },
            (_, _) => throw new InvalidOperationException("The picker must not run.")).ConfigureAwait(true);

        map.ViewModel.Cleanup();
    }

    [Fact]
    public async Task SaveMapImageAsyncStartsFromTheLastConfirmedFolderAndIgnoresCancellation()
    {
        // Arrange
        var sourceFolder = Directory.CreateTempSubdirectory("pge-map-source-");
        var exportFolder = Directory.CreateTempSubdirectory("pge-map-export-");
        var map = CreateReadyMapImageViewModel(sourceFolder.FullName);
        var startFolders = new List<string>();
        string? pickerResult = Path.Combine(exportFolder.FullName, "map.png");

        Task<string?> PickAsync(MapImageSavePickerOptions options, CancellationToken cancellationToken)
        {
            startFolders.Add(options.SuggestedStartFolder);
            return Task.FromResult(pickerResult);
        }


        try
        {
            // Act
            await map.ViewModel.SaveMapImageAsync(AcceptAsync, PickAsync).ConfigureAwait(true);
            pickerResult = null;
            await map.ViewModel.SaveMapImageAsync(AcceptAsync, PickAsync).ConfigureAwait(true);
            exportFolder.Delete(recursive: true);
            await map.ViewModel.SaveMapImageAsync(AcceptAsync, PickAsync).ConfigureAwait(true);

            // Assert
            Assert.Equal(
                [sourceFolder.FullName, exportFolder.FullName, sourceFolder.FullName],
                startFolders);
        }
        finally
        {
            map.ViewModel.Cleanup();
            sourceFolder.Delete(recursive: true);
            if (exportFolder.Exists)
            {
                exportFolder.Delete(recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("dialog-closed")]
    [InlineData("picker-closed")]
    [InlineData("cleanup-in-dialog")]
    [InlineData("cleanup-in-picker")]
    public async Task SaveMapImageAsyncTreatsClosingAndCancellationAsInformational(string cancellation)
    {
        // Arrange
        var map = CreateReadyMapImageViewModel();
        InfoBarSeverity? severity = null;
        map.WorkspaceState.NotificationRequested += (_, args) => severity = args.Severity;

        async Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            map.ViewModel.Cleanup();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(true);
        }

        await map.ViewModel.SaveMapImageAsync(
            async (export, cancellationToken) =>
            {
                if (cancellation == "cleanup-in-dialog")
                {
                    await WaitForCancellationAsync(cancellationToken).ConfigureAwait(true);
                }

                await ShowPreviewAsync(export).ConfigureAwait(true);
                return cancellation != "dialog-closed";
            },
            async (_, cancellationToken) =>
            {
                if (cancellation == "cleanup-in-picker")
                {
                    await WaitForCancellationAsync(cancellationToken).ConfigureAwait(true);
                }

                return null;
            }).ConfigureAwait(true);

        Assert.Equal(InfoBarSeverity.Informational, severity);
        Assert.Null(map.ImageService.SavedFilePath);
        Assert.Equal(1, Assert.Single(map.Service.Exporters).DisposeCount);
        map.ViewModel.Cleanup();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveMapImageAsyncPublishesFailureAndRethrowsOriginalException(bool failDuringSave)
    {
        // Arrange
        var map = CreateReadyMapImageViewModel();
        var expectedException = failDuringSave
            ? new IOException("destination is not writable")
            : (Exception)new InvalidOperationException("the dialog could not be shown");
        map.ImageService.SaveFailure = failDuringSave ? expectedException : null;
        string? notification = null;
        InfoBarSeverity? severity = null;
        map.WorkspaceState.NotificationRequested += (_, args) =>
        {
            notification = args.Message;
            severity = args.Severity;
        };

        var actualException = await Assert.ThrowsAnyAsync<Exception>(
            () => map.ViewModel.SaveMapImageAsync(
                async (export, cancellationToken) => failDuringSave
                    ? await AcceptAsync(export, cancellationToken).ConfigureAwait(true)
                    : throw expectedException,
                (_, _) => Task.FromResult<string?>(@"C:\Exports\map.png"))).ConfigureAwait(true);

        Assert.Same(expectedException, actualException);
        Assert.Contains(expectedException.Message, notification, StringComparison.Ordinal);
        Assert.Equal(InfoBarSeverity.Error, severity);
        Assert.False(map.ViewModel.IsMapImageSaving);
        Assert.Equal(1, Assert.Single(map.Service.Exporters).DisposeCount);
        map.ViewModel.Cleanup();
    }

    // ダイアログと同じく、プレビューが画面に出てから決定する
    private static async Task ShowPreviewAsync(MapImageExportViewModel export)
    {
        await export.PreviewTask.ConfigureAwait(true);
        export.NotifyPreviewShown(export.PreviewPng!);
    }

    private static async Task<bool> AcceptAsync(MapImageExportViewModel export, CancellationToken cancellationToken)
    {
        await ShowPreviewAsync(export).ConfigureAwait(true);
        return true;
    }

    [Fact]
    public void CleanupDisposesResources()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act
        viewModel.Cleanup();

        // Assert
        Assert.Null(viewModel.Surface);
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

            await TestTasks.AssertCompletesWithinAsync(service.LoadCalled, TimeSpan.FromSeconds(1)).ConfigureAwait(true);

            // Assert
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

        await TestTasks.AssertCompletesWithinAsync(service.LoadStarted, TimeSpan.FromSeconds(1)).ConfigureAwait(true);

        // await 中に Cleanup() を発生させ、_map/_surface を破棄・null 化する
        viewModel.Cleanup();
        service.CompleteLoad(new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 35.681236, 139.767125));

        // Assert: 破棄済みの map/surface へアクセスせず例外なく完了すること
        await TestTasks.AssertCompletesWithinAsync(updateTask, TimeSpan.FromSeconds(1)).ConfigureAwait(true);
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
        var surface = SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        var photo = new PhotoListItem(
            new PhotoItem(@"C:\Photos\selected.jpg", 1, DateTimeOffset.UtcNow, isFolder: false),
            thumbnail: null);

        // Act: LoadPhotoMetadataAsync が結果を返す直前(Presenter 呼び出し前)に Cleanup() が走る
        await viewModel.UpdateMarkersFromSelectionAsync(new[] { photo }).ConfigureAwait(true);

        // Assert: 破棄済みの表示面へマーカーが書き込まれていない(no-op)こと
        Assert.Empty(surface.Markers);
        Assert.Null(viewModel.Surface);
    }

    [Fact]
    public async Task UpdateMarkersFromSelectionAsyncRaisesStatusChangesOnCallerContextAfterCancelingPreviousUpdate()
    {
        // Arrange: 直前の更新が読み込み中で、キャンセル時に登録済みコールバックが走る状態にする
        var service = new PendingLoadMapPaneService();
        var viewModel = new MapPaneViewModel(service);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        var photo = new PhotoListItem(
            new PhotoItem(@"C:\Photos\selected.jpg", 1, DateTimeOffset.UtcNow, isFolder: false),
            thumbnail: null);
        var folder = new PhotoListItem(
            new PhotoItem(@"C:\Photos\folder", 0, DateTimeOffset.UtcNow, isFolder: true),
            thumbnail: null);

        var statusThreadIds = new List<int>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapPaneViewModel.StatusVisibility))
            {
                lock (statusThreadIds)
                {
                    statusThreadIds.Add(Environment.CurrentManagedThreadId);
                }
            }
        };

        using var context = new SingleThreadSynchronizationContext();

        try
        {
            // Act
            var run = context.RunAsync(async () =>
            {
                var first = viewModel.UpdateMarkersFromSelectionAsync(new[] { photo });
                await service.LoadStarted.ConfigureAwait(true);
                await viewModel.UpdateMarkersFromSelectionAsync(new[] { folder }).ConfigureAwait(true);
                await first.ConfigureAwait(true);
            });

            await TestTasks.AssertCompletesWithinAsync(run, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            await run.ConfigureAwait(true);

            // Assert: UI スレッド相当のスレッド以外で PropertyChanged が発火していないこと
            lock (statusThreadIds)
            {
                Assert.NotEmpty(statusThreadIds);
                Assert.All(statusThreadIds, id => Assert.Equal(context.ThreadId, id));
            }
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("pk.stored-token", true)]
    public void SwitchTileSourceFallsBackToOpenStreetMapWhenMapboxTokenIsUnavailable(string? storedToken, bool storeFails)
    {
        // Arrange
        var secretStore = new InMemorySecretStore();
        if (storedToken is not null)
        {
            secretStore.Set(SecretKeys.MapboxAccessToken, storedToken);
        }

        if (storeFails)
        {
            secretStore.FailWithVaultError();
        }

        var viewModel = CreateViewModelWithSecretStore(secretStore);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        try
        {
            // Act
            viewModel.SwitchTileSource(MapTileSourceType.MapboxSatellite);

            // Assert: 表示は OSM に落とすが、選択されている設定値は書き換えない
            Assert.Equal(MapTileSourceType.MapboxSatellite, viewModel.CurrentTileSource);
            Assert.Equal("OpenStreetMap", GetBaseTileLayer(viewModel).Name);
            Assert.Equal(MapAttribution.OpenStreetMapText, viewModel.AttributionText);
            Assert.Equal(LocalizationService.GetString("MapStatus.MapboxTokenMissing"), viewModel.BackgroundNotice);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.BackgroundNoticeVisibility);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public async Task SwitchTileSourceUsesMapboxWhenTokenIsStoredAndReportsAuthenticationFailure()
    {
        // Arrange
        var secretStore = new InMemorySecretStore();
        secretStore.Set(SecretKeys.MapboxAccessToken, "pk.stored-token");
        var viewModel = CreateViewModelWithSecretStore(secretStore);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        using var handler = new StubHttpMessageHandler(System.Net.HttpStatusCode.Unauthorized);
        using var client = new System.Net.Http.HttpClient(handler);
        var tile = new BruTile.TileInfo { Index = new BruTile.TileIndex(0, 0, 0) };

        try
        {
            // Act
            viewModel.SwitchTileSource(MapTileSourceType.MapboxSatellite);

            // Assert
            var mapboxSource = Assert.IsType<MapboxSatelliteTileSource>(GetBaseTileLayer(viewModel).TileSource);
            Assert.Equal(MapTileSourceType.MapboxSatellite, viewModel.CurrentTileSource);
            Assert.Equal(MapAttribution.MapboxText, viewModel.AttributionText);
            Assert.Equal(string.Empty, viewModel.BackgroundNotice);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.BackgroundNoticeVisibility);

            // Act: 認証エラー
            await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(
                () => mapboxSource.GetTileAsync(client, tile, CancellationToken.None)).ConfigureAwait(true);

            // Assert: 自動で他の背景へ切り替えず、注意書きだけ出す
            Assert.Equal(LocalizationService.GetString("MapStatus.MapboxAuthFailed"), viewModel.BackgroundNotice);
            Assert.IsType<MapboxSatelliteTileSource>(GetBaseTileLayer(viewModel).TileSource);

            // Act: 別の背景へ切り替えた後に、古い背景から遅れて届いた通知
            viewModel.SwitchTileSource(MapTileSourceType.OpenStreetMap);
            await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(
                () => mapboxSource.GetTileAsync(client, tile, CancellationToken.None)).ConfigureAwait(true);

            // Assert
            Assert.Equal(string.Empty, viewModel.BackgroundNotice);
            Assert.Equal(MapAttribution.OpenStreetMapText, viewModel.AttributionText);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Theory]
    [InlineData(MapTileSourceType.GsiPhoto, null, 89)]
    [InlineData(MapTileSourceType.GsiPale, null, 38)]
    [InlineData(MapTileSourceType.MapboxSatellite, "pk.stored-token", 35)]
    // トークンがない Mapbox は代わりの背景を表示するため、その帰属表示の高さを避ける
    [InlineData(MapTileSourceType.MapboxSatellite, null, 21)]
    internal void SwitchTileSourceKeepsTheStatusPanelAboveTheDisplayedAttribution(
        MapTileSourceType sourceType, string? storedToken, double expectedBottom)
    {
        var secretStore = new InMemorySecretStore();
        if (storedToken is not null)
        {
            secretStore.Set(SecretKeys.MapboxAccessToken, storedToken);
        }

        var viewModel = CreateViewModelWithSecretStore(secretStore);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        try
        {
            viewModel.SwitchTileSource(sourceType);

            Assert.Equal(expectedBottom, viewModel.StatusPanelMargin.Bottom);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Theory]
    [InlineData(1, 0, "35.681236, 139.767125")]
    [InlineData(2, 0, "")]
    [InlineData(1, 1, "")]
    [InlineData(0, 1, "")]
    [InlineData(0, 0, "")]
    public async Task UpdateMarkersFromSelectionAsyncShowsLocationInfoOnlyForSingleSelection(
        int photoCount,
        int folderCount,
        string expectedCoordinates)
    {
        // Arrange: 直前に 1 枚選択して、撮影位置情報が出ている状態にする
        var viewModel = new MapPaneViewModel(new WorkspaceSelectionMapPaneService());
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        var selection = Enumerable.Range(0, photoCount)
            .Select(index => CreateListItem($@"C:\Photos\photo{index}.jpg", isFolder: false))
            .Concat(Enumerable.Range(0, folderCount)
                .Select(index => CreateListItem($@"C:\Photos\folder{index}", isFolder: true)))
            .ToArray();

        try
        {
            await viewModel.UpdateMarkersFromSelectionAsync(new[] { CreateListItem(@"C:\Photos\previous.jpg", isFolder: false) }).ConfigureAwait(true);
            Assert.Equal("35.681236, 139.767125", viewModel.LocationInfo.CoordinatesText);

            // Act
            await viewModel.UpdateMarkersFromSelectionAsync(selection).ConfigureAwait(true);

            // Assert
            Assert.Equal(expectedCoordinates, viewModel.LocationInfo.CoordinatesText);
        }
        finally
        {
            viewModel.Cleanup();
        }

        Assert.Equal(string.Empty, viewModel.LocationInfo.CoordinatesText);
    }

    [Theory]
    [InlineData(1, "35.681236, 139.767125")]
    [InlineData(2, "")]
    public async Task UpdateMarkersFromSelectionAsyncClearsPreviousLocationInfoBeforeMetadataLoadCompletes(
        int photoCount,
        string expectedCoordinatesAfterLoad)
    {
        // Arrange: 写真 A の撮影位置情報が出ている状態にする
        var service = new ControlledLoadMapPaneService();
        var viewModel = new MapPaneViewModel(service);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        var selection = Enumerable.Range(0, photoCount)
            .Select(index => CreateListItem($@"C:\Photos\next{index}.jpg", isFolder: false))
            .ToArray();

        try
        {
            var first = viewModel.UpdateMarkersFromSelectionAsync(new[] { CreateListItem(@"C:\Photos\a.jpg", isFolder: false) });
            service.CompleteLoad(0, latitude: 43.468212, longitude: 142.482015);
            await first.ConfigureAwait(true);
            Assert.Equal("43.468212, 142.482015", viewModel.LocationInfo.CoordinatesText);

            // Act: 次の選択のメタデータ読み込みを保留する
            var second = viewModel.UpdateMarkersFromSelectionAsync(selection);
            await service.WaitForLoadAsync(1).ConfigureAwait(true);

            // Assert: 読み込みの完了を待たずに、写真 A の座標が消えている
            Assert.Equal(string.Empty, viewModel.LocationInfo.CoordinatesText);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.LocationInfo.CoordinatesVisibility);

            service.CompleteLoad(1, latitude: 35.681236, longitude: 139.767125);
            await second.ConfigureAwait(true);
            Assert.Equal(expectedCoordinatesAfterLoad, viewModel.LocationInfo.CoordinatesText);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task UpdateMarkersFromSelectionAsyncDoesNotStartAreaDataAcquisitionForASelectionThatChangedWhileResolving(int nextSelectionCount)
    {
        // Arrange: 住所の解決を保留したまま、選択を解除（0 件）・複数選択（2 件）へ変える
        var service = new ControlledLoadMapPaneService();
        var resolver = new PendingAreaNameResolver();
        var detector = new RecordingNeedDetector();
        var acquisition = new AreaDataAcquisitionViewModel(detector, new UnusedDownloader(), (_, _) => Task.FromResult(AreaDataConsent.Decline), (_, _) => { }, null);
        var viewModel = new MapPaneViewModel(service, new WorkspaceState(), new MapImageService(), areaNameResolver: resolver, areaDataAcquisition: acquisition);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        try
        {
            var first = viewModel.UpdateMarkersFromSelectionAsync(new[] { CreateListItem(@"C:Photosa.jpg", isFolder: false) });
            service.CompleteLoad(0, latitude: 35.0, longitude: 133.0);
            await resolver.Started.Task.ConfigureAwait(true);

            // Act
            var next = Enumerable.Range(0, nextSelectionCount)
                .Select(index => CreateListItem($@"C:Photos
ext{index}.jpg", isFolder: false))
                .ToArray();
            var second = viewModel.UpdateMarkersFromSelectionAsync(next);
            if (nextSelectionCount > 0)
            {
                await service.WaitForLoadAsync(1).ConfigureAwait(true);
                service.CompleteLoad(1, latitude: 35.0, longitude: 133.0);
            }

            await second.ConfigureAwait(true);
            await first.ConfigureAwait(true);
            await Task.Delay(100).ConfigureAwait(true);

            // Assert: 古い選択のために、地域データの要否の確認（通信・確認ダイアログの入口）を始めていない
            Assert.Equal(0, detector.CallCount);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public async Task UpdateMarkersFromSelectionAsyncStartsAreaDataAcquisitionForTheCurrentSingleSelection()
    {
        var service = new ControlledLoadMapPaneService();
        var resolver = new PendingAreaNameResolver();
        var detector = new RecordingNeedDetector();
        var acquisition = new AreaDataAcquisitionViewModel(detector, new UnusedDownloader(), (_, _) => Task.FromResult(AreaDataConsent.Decline), (_, _) => { }, null);
        var viewModel = new MapPaneViewModel(service, new WorkspaceState(), new MapImageService(), areaNameResolver: resolver, areaDataAcquisition: acquisition);
#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        SetMapInitializedForTest(viewModel);
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        try
        {
            var update = viewModel.UpdateMarkersFromSelectionAsync(new[] { CreateListItem(@"C:Photosa.jpg", isFolder: false) });
            service.CompleteLoad(0, latitude: 35.0, longitude: 133.0);
            await resolver.Started.Task.ConfigureAwait(true);
            resolver.Complete();
            await update.ConfigureAwait(true);
            await detector.Called.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            Assert.Equal(1, detector.CallCount);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    private static PhotoListItem CreateListItem(string path, bool isFolder)
        => new(new PhotoItem(path, isFolder ? 0 : 1, DateTimeOffset.UtcNow, isFolder), thumbnail: null);

    private static MapPaneViewModel CreateViewModelWithSecretStore(ISecretStore secretStore)
        => new(new MapPaneService(), new WorkspaceState(), new MapImageService(), static () => null, secretStore);

    private static TileLayer GetBaseTileLayer(MapPaneViewModel viewModel)
    {
        var surface = Assert.IsType<MapsuiMapSurface>(viewModel.Surface);
        var map = surface.Map;
        return Assert.IsType<TileLayer>(map.Layers.First());
    }

    private static MapsuiMapSurface SetMapInitializedForTest(MapPaneViewModel viewModel)
    {
        var surfaceField = typeof(MapPaneViewModel).GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic);
        var isMapInitializedField = typeof(MapPaneViewModel).GetField("_isMapInitialized", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(surfaceField);
        Assert.NotNull(isMapInitializedField);

#pragma warning disable CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.
        var surface = MapsuiMapSurface.Create();
#pragma warning restore CA2000 // ownership is transferred to viewModel and disposed via viewModel.Cleanup.

        surfaceField!.SetValue(viewModel, surface);
        isMapInitializedField!.SetValue(viewModel, true);
        return surface;
    }

    private sealed record ReadyMapImage(
        MapPaneViewModel ViewModel,
        WorkspaceState WorkspaceState,
        RecordingMapImageService ImageService,
        MapImageMapPaneService Service,
        FakeMapSurface Surface);

    private static ReadyMapImage CreateReadyMapImageViewModel(
        string? currentFolderPath = null,
        InMemorySecretStore? secretStore = null)
    {
        var workspaceState = new WorkspaceState { CurrentFolderPath = currentFolderPath };
        var imageService = new RecordingMapImageService();
        var service = new MapImageMapPaneService();
        var viewModel = new MapPaneViewModel(
            service, workspaceState, imageService, static () => null, secretStore ?? new InMemorySecretStore());
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MapPaneViewModel).GetMethod("InitializeMapCore", flags)!.Invoke(viewModel, null);
        typeof(MapPaneViewModel).GetField("_isMapInitialized", flags)!.SetValue(viewModel, true);
        service.Surface.View = new MapViewState(true, 0, 0, WebMercator.ResolutionFromLogicalZoom(10), 400, 300);
        viewModel.UpdateMapImageSaveState(hasValidViewport: true, isTileLoading: false);
        return new ReadyMapImage(viewModel, workspaceState, imageService, service, service.Surface);
    }

    private static MapPaneViewModel CreateViewModelWithoutDispatcher()
        => new(new MapPaneService(), new WorkspaceState(), new MapImageService(), static () => null);

    private sealed class WorkspaceSelectionMapPaneService : IMapPaneService
    {
        private readonly TaskCompletionSource<bool> _loadCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LoadPhotoMetadataCallCount { get; private set; }

        public Task LoadCalled => _loadCalled.Task;

        public (IMapBackground Background, IMapSurface Surface) InitializeMap(MapBackgroundRequest background)
        {
            throw new NotSupportedException();
        }

        public IMapImageExporter CreateImageExporter() => throw new NotSupportedException();

        public IMapBackground CreateBackground(MapBackgroundRequest background)
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

    private sealed class MapImageMapPaneService : IMapPaneService
    {
        public FakeMapSurface Surface { get; } = new();

        public List<FakeMapImageExporter> Exporters { get; } = [];

        public (IMapBackground Background, IMapSurface Surface) InitializeMap(MapBackgroundRequest background)
            => (CreateBackground(background), Surface);

        public IMapImageExporter CreateImageExporter()
        {
            var exporter = new FakeMapImageExporter();
            Exporters.Add(exporter);
            return exporter;
        }

        public IMapBackground CreateBackground(MapBackgroundRequest background)
            => new FakeMapsuiBackground { SourceType = background.SourceType };

        public Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
            IReadOnlyList<PhotoListItem> items,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public string GetTileCacheRootDirectory() => Path.GetTempPath();

        public string GetPinImagePath(PhotoMetadata metadata) => throw new NotSupportedException();

        public bool FileExistsAtPath(string path) => false;
    }

    private sealed class RecordingMapImageService : IMapImageService
    {
        public string? SavedFilePath { get; private set; }

        public Exception? SaveFailure { get; set; }

        public string CreateDefaultFileName(DateTimeOffset timestamp) => "map.png";

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

        public (IMapBackground Background, IMapSurface Surface) InitializeMap(MapBackgroundRequest background)
        {
            throw new NotSupportedException();
        }

        public IMapImageExporter CreateImageExporter() => throw new NotSupportedException();

        public IMapBackground CreateBackground(MapBackgroundRequest background)
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

    private sealed class PendingLoadMapPaneService : IMapPaneService
    {
        private readonly TaskCompletionSource _loadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task LoadStarted => _loadStarted.Task;

        public (IMapBackground Background, IMapSurface Surface) InitializeMap(MapBackgroundRequest background)
        {
            throw new NotSupportedException();
        }

        public IMapImageExporter CreateImageExporter() => throw new NotSupportedException();

        public IMapBackground CreateBackground(MapBackgroundRequest background)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
            IReadOnlyList<PhotoListItem> items,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // キャンセル時に CancelAsync が実行する登録済みコールバックを再現する
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            _loadStarted.TrySetResult();
            return completion.Task;
        }

        public string GetTileCacheRootDirectory()
            => Path.GetTempPath();

        public string GetPinImagePath(PhotoMetadata metadata)
            => Path.Combine(Path.GetTempPath(), "red_pin.png");

        public bool FileExistsAtPath(string path)
            => false;
    }

    // メタデータ読み込みを呼び出しごとに保留し、テストから任意のタイミングで完了させる
    /// <summary>住所の解決を、Complete() まで（または取り消されるまで）保留する</summary>
    private sealed class PendingAreaNameResolver : IAreaNameResolver
    {
        private readonly TaskCompletionSource<AreaName?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler? ResultsInvalidated
        {
            add { }
            remove { }
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete() => _result.TrySetResult(null);

        public Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _result.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class RecordingNeedDetector : IAreaDataNeedDetector
    {
        public int CallCount { get; private set; }

        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<string>> FindMissingAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            CallCount++;
            Called.TrySetResult();
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    private sealed class UnusedDownloader : IAreaDataDownloader
    {
        public Task<AreaDataPackInfo> DownloadAsync(string prefectureCode, IProgress<long>? progress, CancellationToken cancellationToken)
            => throw new InvalidOperationException("must not download");
    }

    private sealed class ControlledLoadMapPaneService : IMapPaneService
    {
        private readonly object _gate = new();
        private readonly List<PendingLoad> _loads = new();

        public (IMapBackground Background, IMapSurface Surface) InitializeMap(MapBackgroundRequest background)
            => throw new NotSupportedException();

        public IMapImageExporter CreateImageExporter() => throw new NotSupportedException();

        public IMapBackground CreateBackground(MapBackgroundRequest background)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
            IReadOnlyList<PhotoListItem> items,
            CancellationToken cancellationToken)
        {
            var load = GetLoad(GetNextIndex());
            load.Items = items;
            load.Started.TrySetResult();
            return load.Completion.Task;
        }

        public Task WaitForLoadAsync(int index) => GetLoad(index).Started.Task;

        public void CompleteLoad(int index, double latitude, double longitude)
        {
            var load = GetLoad(index);
            var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, latitude, longitude);
            load.Completion.TrySetResult(load.Items!.Select(item => (item, (PhotoMetadata?)metadata)).ToArray());
        }

        public string GetTileCacheRootDirectory()
            => Path.GetTempPath();

        public string GetPinImagePath(PhotoMetadata metadata)
            => Path.Combine(Path.GetTempPath(), "red_pin.png");

        public bool FileExistsAtPath(string path)
            => false;

        private int _started;

        private int GetNextIndex()
        {
            lock (_gate)
            {
                return _started++;
            }
        }

        private PendingLoad GetLoad(int index)
        {
            lock (_gate)
            {
                while (_loads.Count <= index)
                {
                    _loads.Add(new PendingLoad());
                }

                return _loads[index];
            }
        }

        private sealed class PendingLoad
        {
            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public IReadOnlyList<PhotoListItem>? Items { get; set; }
        }
    }

    private sealed class CleanupAfterLoadMapPaneService : IMapPaneService
    {
        private readonly Action _onLoadCompleting;

        public CleanupAfterLoadMapPaneService(Action onLoadCompleting)
        {
            _onLoadCompleting = onLoadCompleting;
        }

        public (IMapBackground Background, IMapSurface Surface) InitializeMap(MapBackgroundRequest background)
        {
            throw new NotSupportedException();
        }

        public IMapImageExporter CreateImageExporter() => throw new NotSupportedException();

        public IMapBackground CreateBackground(MapBackgroundRequest background)
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
