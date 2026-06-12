using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// FileBrowserStatusViewModel のテスト
/// </summary>
public class FileBrowserStatusViewModelTests
{
    [Fact]
    public void ConstructorThrowsWhenUiDispatcherIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FileBrowserStatusViewModel(null!));
    }

    [Fact]
    public void InitialStateHidesOverlayAndLocation()
    {
        using var viewModel = CreateViewModel();

        Assert.Equal(Visibility.Collapsed, viewModel.StatusVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusPrimaryActionVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusSecondaryActionVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusBarLocationVisibility);
        Assert.Null(viewModel.StatusBarText);
        Assert.Null(viewModel.SelectedMetadata);
    }

    [Fact]
    public void SetStatusWithNullMessageHidesOverlay()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetStatus("dummy", InfoBarSeverity.Error);

        viewModel.SetStatus(null, InfoBarSeverity.Informational);

        Assert.Null(viewModel.StatusMessage);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusVisibility);
        Assert.Null(viewModel.StatusTitle);
        Assert.Null(viewModel.StatusDetail);
        Assert.Equal(Symbol.Help, viewModel.StatusSymbol);
        Assert.Equal(StatusAction.None, viewModel.StatusPrimaryAction);
        Assert.Equal(StatusAction.None, viewModel.StatusSecondaryAction);
    }

    [Fact]
    public void SetStatusWithNoFilesFoundShowsEmptyFolderOverlay()
    {
        using var viewModel = CreateViewModel();

        viewModel.SetStatus(LocalizationService.GetString("Message.NoFilesFound"), InfoBarSeverity.Informational);

        Assert.Equal(Visibility.Visible, viewModel.StatusVisibility);
        Assert.Equal(LocalizationService.GetString("Overlay.NoFilesFoundTitle"), viewModel.StatusTitle);
        Assert.Equal(LocalizationService.GetString("Overlay.NoFilesFoundDetail"), viewModel.StatusDetail);
        Assert.Equal(Symbol.Pictures, viewModel.StatusSymbol);
        Assert.Equal(StatusAction.OpenFolder, viewModel.StatusPrimaryAction);
        Assert.Equal(StatusAction.None, viewModel.StatusSecondaryAction);
        Assert.Equal(Visibility.Visible, viewModel.StatusPrimaryActionVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusSecondaryActionVisibility);
    }

    [Fact]
    public void SetStatusWithNoFilesFoundAndActiveFiltersShowsResetFiltersAction()
    {
        using var viewModel = CreateViewModel();
        viewModel.NotifyFiltersChanged(hasActiveFilters: true);

        viewModel.SetStatus(LocalizationService.GetString("Message.NoFilesFound"), InfoBarSeverity.Informational);

        Assert.Equal(LocalizationService.GetString("Overlay.NoFilesFoundDetailWithFilters"), viewModel.StatusDetail);
        Assert.Equal(StatusAction.OpenFolder, viewModel.StatusPrimaryAction);
        Assert.Equal(StatusAction.ResetFilters, viewModel.StatusSecondaryAction);
        Assert.Equal(LocalizationService.GetString("Action.ResetFilters"), viewModel.StatusSecondaryActionLabel);
        Assert.Equal(Visibility.Visible, viewModel.StatusSecondaryActionVisibility);
    }

    [Fact]
    public void SetStatusWithErrorShowsErrorOverlay()
    {
        using var viewModel = CreateViewModel();
        const string errorMessage = "error message";

        viewModel.SetStatus(errorMessage, InfoBarSeverity.Error);

        Assert.Equal(Visibility.Visible, viewModel.StatusVisibility);
        Assert.Equal(InfoBarSeverity.Error, viewModel.StatusSeverity);
        Assert.Equal(LocalizationService.GetString("Overlay.LoadFolderErrorTitle"), viewModel.StatusTitle);
        Assert.Equal(errorMessage, viewModel.StatusDetail);
        Assert.Equal(Symbol.Folder, viewModel.StatusSymbol);
        Assert.Equal(StatusAction.OpenFolder, viewModel.StatusPrimaryAction);
        Assert.Equal(StatusAction.GoHome, viewModel.StatusSecondaryAction);
        Assert.Equal(LocalizationService.GetString("Action.OpenFolder"), viewModel.StatusPrimaryActionLabel);
        Assert.Equal(LocalizationService.GetString("Action.GoHome"), viewModel.StatusSecondaryActionLabel);
    }

    [Fact]
    public void SetStatusWithGenericMessageShowsTitleOnly()
    {
        using var viewModel = CreateViewModel();
        const string message = "generic message";

        viewModel.SetStatus(message, InfoBarSeverity.Informational);

        Assert.Equal(message, viewModel.StatusTitle);
        Assert.Null(viewModel.StatusDetail);
        Assert.Equal(Symbol.Help, viewModel.StatusSymbol);
        Assert.Equal(StatusAction.None, viewModel.StatusPrimaryAction);
        Assert.Equal(StatusAction.None, viewModel.StatusSecondaryAction);
    }

    [Fact]
    public void NotifyFiltersChangedReevaluatesOverlay()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetStatus(LocalizationService.GetString("Message.NoFilesFound"), InfoBarSeverity.Informational);
        Assert.Equal(StatusAction.None, viewModel.StatusSecondaryAction);

        viewModel.NotifyFiltersChanged(hasActiveFilters: true);

        Assert.Equal(LocalizationService.GetString("Overlay.NoFilesFoundDetailWithFilters"), viewModel.StatusDetail);
        Assert.Equal(StatusAction.ResetFilters, viewModel.StatusSecondaryAction);

        viewModel.NotifyFiltersChanged(hasActiveFilters: false);

        Assert.Equal(LocalizationService.GetString("Overlay.NoFilesFoundDetail"), viewModel.StatusDetail);
        Assert.Equal(StatusAction.None, viewModel.StatusSecondaryAction);
    }

    [Fact]
    public void UpdateStatusBarWithoutFolderShowsNoFolderLabel()
    {
        using var viewModel = CreateViewModel();

        viewModel.UpdateStatusBar(currentFolderPath: null, itemCount: 0, selectedCount: 0, selectedItem: null);

        var expected = $"{LocalizationService.GetString("StatusBar.NoFolderSelected")} | {LocalizationService.Format("StatusBar.Items", 0)}";
        Assert.Equal(expected, viewModel.StatusBarText);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusBarLocationVisibility);
    }

    [Fact]
    public void UpdateStatusBarWithSingleSelectionShowsFileNameAndResolution()
    {
        using var viewModel = CreateViewModel();
        var item = CreateFileItem("photo.jpg");

        viewModel.UpdateStatusBar("/test", itemCount: 3, selectedCount: 1, selectedItem: item);

        var expected = $"/test | {LocalizationService.Format("StatusBar.Items", 3)} | {LocalizationService.Format("StatusBar.Selected", "photo.jpg")} | {item.ResolutionText}";
        Assert.Equal(expected, viewModel.StatusBarText);
    }

    [Fact]
    public void UpdateStatusBarWithMultipleSelectionShowsCount()
    {
        using var viewModel = CreateViewModel();
        var item = CreateFileItem("photo.jpg");

        viewModel.UpdateStatusBar("/test", itemCount: 5, selectedCount: 3, selectedItem: item);

        var expected = $"/test | {LocalizationService.Format("StatusBar.Items", 5)} | {LocalizationService.Format("StatusBar.SelectedMultiple", 3)}";
        Assert.Equal(expected, viewModel.StatusBarText);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusBarLocationVisibility);
    }

    [Fact]
    public void SetStatusBarTextOverridesStatusBarText()
    {
        using var viewModel = CreateViewModel();
        viewModel.UpdateStatusBar("/test", itemCount: 1, selectedCount: 0, selectedItem: null);

        viewModel.SetStatusBarText("progress message");

        Assert.Equal("progress message", viewModel.StatusBarText);
    }

    [Theory]
    [InlineData(35.0, 139.0, true, Symbol.Map, "StatusBar.GpsAvailable")] // 有効な GPS 座標あり
    [InlineData(0.0, 0.0, true, Symbol.Important, "StatusBar.GpsFixFailed")] // GPS タグはあるが座標が無効（測位失敗の可能性）
    [InlineData(null, null, false, Symbol.Cancel, "StatusBar.GpsMissing")] // GPS 情報なし
    public async Task LoadMetadataAsyncUpdatesLocationStatus(
        double? latitude, double? longitude, bool hasGpsData, Symbol expectedSymbol, string expectedTooltipKey)
    {
        var metadata = new PhotoMetadata(null, null, null, latitude, longitude, hasGpsData);
        using var viewModel = CreateViewModel((_, _) => Task.FromResult<PhotoMetadata?>(metadata));
        var item = CreateFileItem("photo.jpg");
        viewModel.UpdateStatusBar("/test", itemCount: 1, selectedCount: 1, selectedItem: item);

        await viewModel.LoadMetadataAsync(item);

        Assert.Same(metadata, viewModel.SelectedMetadata);
        Assert.Equal(Visibility.Visible, viewModel.StatusBarLocationVisibility);
        Assert.Equal(expectedSymbol, viewModel.StatusBarLocationSymbol);
        Assert.Equal(LocalizationService.GetString(expectedTooltipKey), viewModel.StatusBarLocationTooltip);
    }

    [Fact]
    public async Task LoadMetadataAsyncWithNullItemClearsMetadataAndHidesLocation()
    {
        var metadata = new PhotoMetadata(null, null, null, latitude: 35.0, longitude: 139.0, hasGpsData: true);
        using var viewModel = CreateViewModel((_, _) => Task.FromResult<PhotoMetadata?>(metadata));
        var item = CreateFileItem("photo.jpg");
        viewModel.UpdateStatusBar("/test", itemCount: 1, selectedCount: 1, selectedItem: item);
        await viewModel.LoadMetadataAsync(item);
        Assert.NotNull(viewModel.SelectedMetadata);

        viewModel.UpdateStatusBar("/test", itemCount: 1, selectedCount: 0, selectedItem: null);
        await viewModel.LoadMetadataAsync(null);

        Assert.Null(viewModel.SelectedMetadata);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusBarLocationVisibility);
        Assert.Null(viewModel.StatusBarLocationTooltip);
    }

    [Fact]
    public async Task LoadMetadataAsyncWithFolderDoesNotLoadMetadata()
    {
        var loadCount = 0;
        using var viewModel = CreateViewModel((_, _) =>
        {
            Interlocked.Increment(ref loadCount);
            return Task.FromResult<PhotoMetadata?>(null);
        });
        var folder = CreateFolderItem("subfolder");
        viewModel.UpdateStatusBar("/test", itemCount: 1, selectedCount: 1, selectedItem: folder);

        await viewModel.LoadMetadataAsync(folder);

        Assert.Equal(0, loadCount);
        Assert.Null(viewModel.SelectedMetadata);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusBarLocationVisibility);
    }

    [Fact]
    public async Task LoadMetadataAsyncCancelsPreviousLoad()
    {
        var firstLoadStarted = new TaskCompletionSource();
        var firstLoadCancelled = new TaskCompletionSource();
        var callCount = 0;
        using var viewModel = CreateViewModel(async (_, token) =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                firstLoadStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    firstLoadCancelled.SetResult();
                    throw;
                }
            }

            return new PhotoMetadata(null, null, null, latitude: 35.0, longitude: 139.0, hasGpsData: true);
        });
        var first = CreateFileItem("first.jpg");
        var second = CreateFileItem("second.jpg");
        viewModel.UpdateStatusBar("/test", itemCount: 2, selectedCount: 1, selectedItem: first);

        var firstLoad = viewModel.LoadMetadataAsync(first);
        await firstLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.UpdateStatusBar("/test", itemCount: 2, selectedCount: 1, selectedItem: second);
        await viewModel.LoadMetadataAsync(second);

        await firstLoadCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await firstLoad.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(viewModel.SelectedMetadata);
        Assert.Equal(Visibility.Visible, viewModel.StatusBarLocationVisibility);
    }

    [Fact]
    public async Task LoadMetadataAsyncDiscardsResultWhenCancelledDuringLoad()
    {
        var loadStarted = new TaskCompletionSource();
        var resumeLoad = new TaskCompletionSource();
        using var viewModel = CreateViewModel(async (_, _) =>
        {
            loadStarted.SetResult();
            await resumeLoad.Task.ConfigureAwait(false);
            return new PhotoMetadata(null, null, null, latitude: 35.0, longitude: 139.0, hasGpsData: true);
        });
        var item = CreateFileItem("photo.jpg");
        viewModel.UpdateStatusBar("/test", itemCount: 1, selectedCount: 1, selectedItem: item);

        var load = viewModel.LoadMetadataAsync(item);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CancelMetadataLoad();
        resumeLoad.SetResult();

        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(viewModel.SelectedMetadata);
        Assert.Equal(Visibility.Collapsed, viewModel.StatusBarLocationVisibility);
    }

    [Fact]
    public async Task CancelMetadataLoadStopsPendingLoad()
    {
        var loadStarted = new TaskCompletionSource();
        using var viewModel = CreateViewModel(async (_, token) =>
        {
            loadStarted.SetResult();
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return null;
        });
        var item = CreateFileItem("photo.jpg");
        viewModel.UpdateStatusBar("/test", itemCount: 1, selectedCount: 1, selectedItem: item);

        var load = viewModel.LoadMetadataAsync(item);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CancelMetadataLoad();

        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(viewModel.SelectedMetadata);
    }

    [Fact]
    public async Task LoadMetadataAsyncCancelsPreviousLoadSuspendedBeforeLoaderStarts()
    {
        // 先行呼び出しが最初の dispatcher await で中断している間に後続の呼び出しが走っても、
        // 先行の CTS が観測されてキャンセルされ、古いメタデータで上書きされないこと（#164 回帰テスト）
        var firstDispatchGate = new TaskCompletionSource();
        var metadataFirst = new PhotoMetadata(null, null, null, latitude: 1.0, longitude: 1.0, hasGpsData: true);
        var metadataSecond = new PhotoMetadata(null, null, null, latitude: 35.0, longitude: 139.0, hasGpsData: true);
        using var viewModel = new FileBrowserStatusViewModel(
            new GatedFirstRunUiDispatcher(firstDispatchGate.Task),
            (path, _) => Task.FromResult<PhotoMetadata?>(
                path.EndsWith("first.jpg", StringComparison.Ordinal) ? metadataFirst : metadataSecond));
        var first = CreateFileItem("first.jpg");
        var second = CreateFileItem("second.jpg");
        viewModel.UpdateStatusBar("/test", itemCount: 2, selectedCount: 1, selectedItem: first);

        var firstLoad = viewModel.LoadMetadataAsync(first);

        viewModel.UpdateStatusBar("/test", itemCount: 2, selectedCount: 1, selectedItem: second);
        await viewModel.LoadMetadataAsync(second);

        firstDispatchGate.SetResult();
        await firstLoad.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(metadataSecond, viewModel.SelectedMetadata);
        Assert.Equal(Visibility.Visible, viewModel.StatusBarLocationVisibility);
    }

    private static FileBrowserStatusViewModel CreateViewModel(
        Func<string, CancellationToken, Task<PhotoMetadata?>>? getMetadataAsync = null)
    {
        return new FileBrowserStatusViewModel(
            new FakeUiDispatcher(),
            getMetadataAsync ?? ((_, _) => Task.FromResult<PhotoMetadata?>(null)));
    }

    private static PhotoListItem CreateFileItem(string fileName)
        => CreateListItem(fileName, isFolder: false);

    private static PhotoListItem CreateFolderItem(string folderName)
        => CreateListItem(folderName, isFolder: true);

    private static PhotoListItem CreateListItem(string name, bool isFolder)
    {
        var photoItem = new PhotoItem(
            filePath: $"/test/{name}",
            sizeBytes: 1000,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: isFolder,
            thumbnailPath: null,
            pixelWidth: 100,
            pixelHeight: 100);

        return new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: null);
    }

    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public bool IsAvailable => true;

        public Task RunAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => asyncFunc();

        public bool TryEnqueue(Action action)
        {
            action();
            return true;
        }

        public IUiDispatcherTimer? CreateTimer() => null;
    }

    /// <summary>最初の RunAsync 呼び出しだけ gate を待ってから実行するディスパッチャ。swap race の再現用。</summary>
    private sealed class GatedFirstRunUiDispatcher : IUiDispatcher
    {
        private readonly Task _gate;
        private int _runAsyncCallCount;

        public GatedFirstRunUiDispatcher(Task gate) => _gate = gate;

        public bool IsAvailable => true;

        public async Task RunAsync(Action action)
        {
            if (Interlocked.Increment(ref _runAsyncCallCount) == 1)
            {
                await _gate.ConfigureAwait(false);
            }

            action();
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc) => asyncFunc();

        public bool TryEnqueue(Action action)
        {
            action();
            return true;
        }

        public IUiDispatcherTimer? CreateTimer() => null;
    }
}
