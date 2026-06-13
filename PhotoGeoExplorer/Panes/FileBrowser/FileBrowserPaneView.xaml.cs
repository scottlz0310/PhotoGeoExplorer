using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.UI.Core;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;

namespace PhotoGeoExplorer.Panes.FileBrowser;

internal sealed partial class FileBrowserPaneView : UserControl
{
    private bool _fileListInputHandlersRegistered;
    private bool _suppressSelectionChangedForRightTap;
    private IReadOnlyList<PhotoListItem> _selectionBeforeChange = Array.Empty<PhotoListItem>();
    private FileBrowserPaneViewModel? _previousViewModel;
    private readonly FileBrowserDialogs _dialogs;
    private readonly FileBrowserDragDropHandler _dragDropHandler;
    private readonly FileBrowserMenuBuilder _menuBuilder;

    public FileBrowserPaneView()
    {
        InitializeComponent();

        _dialogs = new FileBrowserDialogs(RootGrid, () => HostWindow);
        _dragDropHandler = new FileBrowserDragDropHandler(RootGrid, () => ViewModel, _dialogs);
        _menuBuilder = new FileBrowserMenuBuilder(
            () => ViewModel,
            new FileContextMenuHandlers(
                OnCreateFolderClicked,
                OnOpenInExplorerClicked,
                OnOpenFolderInExplorerClicked,
                OnCopyPathClicked,
                OnOpenInGoogleMapsClicked,
                OnRenameClicked,
                OnMoveClicked,
                OnMoveToParentClicked,
                OnCopyClicked,
                OnDeleteClicked));

        OpenFolderCommand = new RelayCommand(async () => await OpenFolderAsync().ConfigureAwait(false));
        CreateFolderCommand = new RelayCommand(async () => await CreateFolderAsync().ConfigureAwait(false), () => ViewModel?.CanCreateFolder ?? false);
        RenameSelectionCommand = new RelayCommand(async () => await RenameSelectionAsync().ConfigureAwait(false), () => ViewModel?.CanRenameSelection ?? false);
        MoveSelectionCommand = new RelayCommand(async () => await MoveSelectionAsync().ConfigureAwait(false), () => ViewModel?.CanModifySelection ?? false);
        MoveSelectionToParentCommand = new RelayCommand(async () => await MoveSelectionToParentAsync().ConfigureAwait(false), () => ViewModel?.CanMoveToParentSelection ?? false);
        DeleteSelectionCommand = new RelayCommand(async () => await DeleteSelectionAsync().ConfigureAwait(false), () => ViewModel?.CanModifySelection ?? false);
    }

    public Window? HostWindow { get; set; }

    public ICommand OpenFolderCommand { get; }
    public ICommand CreateFolderCommand { get; }
    public ICommand RenameSelectionCommand { get; }
    public ICommand MoveSelectionCommand { get; }
    public ICommand MoveSelectionToParentCommand { get; }
    public ICommand DeleteSelectionCommand { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_fileListInputHandlersRegistered)
        {
            // WinUI 3 の ListView/GridView は Ctrl+C 等を内部でハンドルして e.Handled=true にするため
            // XAML の KeyDown では届かない。handledEventsToo: true で先取りして処理する。
            var keyHandler = new KeyEventHandler(OnFileListKeyDown);
            FileListList.AddHandler(UIElement.KeyDownEvent, keyHandler, handledEventsToo: true);
            FileListIcon.AddHandler(UIElement.KeyDownEvent, keyHandler, handledEventsToo: true);
            FileListDetails.AddHandler(UIElement.KeyDownEvent, keyHandler, handledEventsToo: true);

            // 右クリック時に WinUI 3 の ListView が PointerPressed で SHIFT 選択をリセットするのを防ぐ。
            // handledEventsToo: true で先取りし、右ボタン押下は e.Handled=true にして内部処理を止める。
            var pointerHandler = new PointerEventHandler(OnFileListPointerPressed);
            FileListList.AddHandler(UIElement.PointerPressedEvent, pointerHandler, handledEventsToo: true);
            FileListIcon.AddHandler(UIElement.PointerPressedEvent, pointerHandler, handledEventsToo: true);
            FileListDetails.AddHandler(UIElement.PointerPressedEvent, pointerHandler, handledEventsToo: true);

            _fileListInputHandlersRegistered = true;
        }
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        // 旧 ViewModel の購読を解除（メモリリーク防止）
        if (_previousViewModel is not null)
        {
            _previousViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _previousViewModel.ConfigureUiActionHandlers(null, null, null, null, null, null);
        }

        // ViewModel の CanExecute 関連プロパティ変更を監視し、Command の状態を更新
        if (args.NewValue is FileBrowserPaneViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.ConfigureUiActionHandlers(
                OpenFolderAsync,
                CreateFolderAsync,
                RenameSelectionAsync,
                MoveSelectionAsync,
                MoveSelectionToParentAsync,
                DeleteSelectionAsync);
            _previousViewModel = vm;
        }
        else
        {
            _previousViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileBrowserPaneViewModel.CanCreateFolder):
                (CreateFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
                break;
            case nameof(FileBrowserPaneViewModel.CanRenameSelection):
                (RenameSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                break;
            case nameof(FileBrowserPaneViewModel.CanModifySelection):
                (MoveSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DeleteSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                break;
            case nameof(FileBrowserPaneViewModel.CanMoveToParentSelection):
                (MoveSelectionToParentCommand as RelayCommand)?.RaiseCanExecuteChanged();
                break;
        }
    }

    public Task OpenFolderAsync()
    {
        return OpenFolderPickerAsync();
    }

    public Task NavigateHomeAsync()
    {
        return ViewModel?.OpenHomeAsync() ?? Task.CompletedTask;
    }

    public Task NavigateUpAsync()
    {
        return ViewModel?.NavigateUpAsync() ?? Task.CompletedTask;
    }

    public Task RefreshAsync()
    {
        return ViewModel?.RefreshAsync() ?? Task.CompletedTask;
    }

    public Task ResetFiltersAsync()
    {
        // ResetFilters() が UpdateFilterState 経由でフォルダ再読み込みまで担うため、
        // ここで RefreshAsync を重ねると LoadFolderAsync が並行実行される（#164 と同根）。
        // メニュー経路（ResetFiltersCommand）と同じく再読み込みを 1 回に揃える。
        ViewModel?.ResetFilters();
        return Task.CompletedTask;
    }

    public Task CreateFolderAsync()
    {
        return CreateFolderAsyncCore();
    }

    public Task RenameSelectionAsync()
    {
        return RenameSelectionAsyncCore();
    }

    public Task MoveSelectionAsync()
    {
        return MoveSelectionAsyncCore();
    }

    public Task MoveSelectionToParentAsync()
    {
        return MoveSelectionToParentAsyncCore();
    }

    public Task DeleteSelectionAsync()
    {
        return DeleteSelectionAsyncCore();
    }

    internal void SelectItems(IReadOnlyList<PhotoListItem> selectedItems)
    {
        var listView = GetFileListView();
        if (listView is not null)
        {
            listView.SelectedItems.Clear();
            foreach (var item in selectedItems)
            {
                listView.SelectedItems.Add(item);
            }
        }

        if (ViewModel is null)
        {
            return;
        }

        ViewModel.UpdateSelection(selectedItems);
        ViewModel.SelectedItem = selectedItems.Count > 0 ? selectedItems[0] : null;

        if (selectedItems.Count > 0)
        {
            listView?.ScrollIntoView(selectedItems[0]);
        }
    }

    internal void SelectItemsByFilePaths(IReadOnlyList<string> filePaths)
    {
        if (ViewModel is null)
        {
            return;
        }

        var selectedItems = ViewModel.ResolveItemsByFilePaths(filePaths);
        SelectItems(selectedItems);
    }

    internal void FocusPhotoItem(PhotoItem photoItem)
    {
        if (ViewModel is null)
        {
            return;
        }

        var target = ViewModel.Items.FirstOrDefault(item
            => !item.IsFolder
               && string.Equals(item.FilePath, photoItem.FilePath, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        ViewModel.SelectedItem = target;

        var listView = GetFileListView();
        listView?.ScrollIntoView(target);
    }

    private FileBrowserPaneViewModel? ViewModel => DataContext as FileBrowserPaneViewModel;

    private async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        await OpenFolderAsync().ConfigureAwait(true);
    }

    private async void OnApplyFiltersClicked(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(true);
    }

    private void OnDetailsColumnsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor)
        {
            _menuBuilder.ShowDetailsColumnsFlyout(anchor);
        }
    }

    private async void OnStatusPrimaryActionClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await PerformStatusActionAsync(ViewModel.Status.StatusPrimaryAction).ConfigureAwait(true);
    }

    private async void OnStatusSecondaryActionClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await PerformStatusActionAsync(ViewModel.Status.StatusSecondaryAction).ConfigureAwait(true);
    }

    private async Task PerformStatusActionAsync(StatusAction action)
    {
        if (ViewModel is null)
        {
            return;
        }

        switch (action)
        {
            case StatusAction.OpenFolder:
                await OpenFolderAsync().ConfigureAwait(true);
                break;
            case StatusAction.GoHome:
                await NavigateHomeAsync().ConfigureAwait(true);
                break;
            case StatusAction.ResetFilters:
                await ResetFiltersAsync().ConfigureAwait(true);
                break;
        }
    }

    private async void OnBreadcrumbItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (_menuBuilder.SuppressBreadcrumbNavigation)
        {
            return;
        }

        if (ViewModel is null || args.Item is not BreadcrumbSegment segment)
        {
            return;
        }

        await ViewModel.LoadFolderAsync(segment.FullPath).ConfigureAwait(true);
    }

    private void OnBreadcrumbPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var container = FindAncestor<BreadcrumbBarItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not BreadcrumbSegment segment)
        {
            return;
        }

        if (segment.Children.Count == 0 || container.ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetCurrentPoint(container);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = point.Position;
        const double separatorHitWidth = 18;
        if (position.X < container.ActualWidth - separatorHitWidth)
        {
            return;
        }

        _menuBuilder.ShowBreadcrumbChildrenFlyout(container, segment);
        e.Handled = true;
    }

    private void OnBreadcrumbDragOver(object sender, DragEventArgs e)
        => _dragDropHandler.OnBreadcrumbDragOver(sender, e);

    private async void OnBreadcrumbDrop(object sender, DragEventArgs e)
        => await _dragDropHandler.OnBreadcrumbDropAsync(sender, e).ConfigureAwait(true);

    private void OnFileListDragOver(object sender, DragEventArgs e)
        => _dragDropHandler.OnFileListDragOver(sender, e);

    private async void OnFileListDrop(object sender, DragEventArgs e)
        => await _dragDropHandler.OnFileListDropAsync(sender, e).ConfigureAwait(true);

    private void OnFileItemsDragStarting(object sender, DragItemsStartingEventArgs e)
        => _dragDropHandler.OnFileItemsDragStarting(sender, e);

    private async void OnFileItemsDragCompleted(object sender, DragItemsCompletedEventArgs e)
        => await _dragDropHandler.OnFileItemsDragCompletedAsync(sender, e).ConfigureAwait(false);

    private void OnFileListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel is null || sender is not ListViewBase listView)
        {
            return;
        }

        var priorSelection = _selectionBeforeChange;

        var source = e.OriginalSource as DependencyObject;
        if (source is not null)
        {
            var container = FindAncestor<SelectorItem>(source);
            if (container is not null
                && listView.ItemFromContainer(container) is PhotoListItem item)
            {
                var toRestore = ViewModel.ResolveRightTapSelection(item, priorSelection);

                ViewModel.BeginBatchSelectionUpdate();
                _suppressSelectionChangedForRightTap = true;
                try
                {
                    listView.SelectedItems.Clear();

                    // item を先頭に追加することで TwoWay バインディングが SelectedItem=item をセットする。
                    // 後続の ViewModel.SelectedItem=item は SetProperty が false を返すため
                    // TwoWay が再発火せず SelectedItems が単数に上書きされない。
                    listView.SelectedItems.Add(item);
                    foreach (var savedItem in toRestore)
                    {
                        if (!object.ReferenceEquals(savedItem, item))
                        {
                            listView.SelectedItems.Add(savedItem);
                        }
                    }
                    ViewModel.UpdateSelection(toRestore.ToList());
                    ViewModel.SelectedItem = item;
                }
                finally
                {
                    _suppressSelectionChangedForRightTap = false;
                    ViewModel.EndBatchSelectionUpdate();
                }
            }
            else
            {
                _suppressSelectionChangedForRightTap = true;
                try
                {
                    listView.SelectedItems.Clear();
                    ViewModel.SelectedItem = null;
                    ViewModel.UpdateSelection(Array.Empty<PhotoListItem>());
                }
                finally
                {
                    _suppressSelectionChangedForRightTap = false;
                }
            }
        }

        var flyout = _menuBuilder.BuildFileContextFlyout();
        flyout.ShowAt(listView, e.GetPosition(listView));
        e.Handled = true;
    }

    private void OnFileItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PhotoListItem)
        {
            return;
        }
    }

    private async void OnFileItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel is null || sender is not ListViewBase listView)
        {
            return;
        }

        var container = FindAncestor<SelectorItem>(e.OriginalSource as DependencyObject);
        if (container is null || listView.ItemFromContainer(container) is not PhotoListItem item)
        {
            return;
        }

        if (!item.IsFolder)
        {
            return;
        }

        await ViewModel.LoadFolderAsync(item.FilePath).ConfigureAwait(true);
        e.Handled = true;
    }

    private void OnFileListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
        }
    }

    private async void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // RightTapped ハンドラ内で listView.SelectedItems を操作中は再帰的な更新を防ぐ。
        if (_suppressSelectionChangedForRightTap)
        {
            return;
        }

        if (ViewModel is null || sender is not ListViewBase listView)
        {
            return;
        }

        // 右クリック直前の選択状態をスナップショットとして保持する。
        // WinUI3 が右クリックで選択を変更した場合、RightTapped ハンドラで復元に使う。
        _selectionBeforeChange = ViewModel.SelectedItems.ToList();

        var selected = listView.SelectedItems
            .OfType<PhotoListItem>()
            .ToList();
        ViewModel.UpdateSelection(selected);
    }

    private void OnFileListTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel is null || sender is not ListViewBase listView)
        {
            return;
        }

        // アイテム以外の余白をタップした場合は選択解除してフォーカスを当てる。
        var container = FindAncestor<SelectorItem>(e.OriginalSource as DependencyObject);
        if (container is null)
        {
            listView.SelectedItems.Clear();
            ViewModel.SelectedItem = null;
            ViewModel.UpdateSelection(Array.Empty<PhotoListItem>());
            listView.Focus(FocusState.Pointer);
        }
    }

    private async void OnFileListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var listView = sender as ListViewBase ?? GetFileListView();
        if (listView is null)
        {
            return;
        }

        var ctrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;

        switch (e.Key)
        {
            case VirtualKey.A when ctrl:
                e.Handled = true;
                listView.SelectAll();
                break;
            case VirtualKey.C when ctrl:
                e.Handled = true;
                ViewModel.CopySelectionToClipboard();
                break;
            case VirtualKey.X when ctrl:
                e.Handled = true;
                ViewModel.CutSelectionToClipboard();
                break;
            case VirtualKey.V when ctrl:
                e.Handled = true;
                await PasteSelectionAsyncCore().ConfigureAwait(true);
                break;
            case VirtualKey.Delete:
                if (!ViewModel.CanModifySelection)
                {
                    break;
                }
                e.Handled = true;
                await DeleteSelectionAsyncCore().ConfigureAwait(true);
                break;
            case VirtualKey.F2:
                if (!ViewModel.CanRenameSelection)
                {
                    break;
                }
                e.Handled = true;
                await RenameSelectionAsyncCore().ConfigureAwait(true);
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                listView.SelectedItems.Clear();
                ViewModel.SelectedItem = null;
                break;
        }
    }

    private async Task PasteSelectionAsyncCore()
    {
        if (ViewModel is null || !ViewModel.CanPasteSelection)
        {
            return;
        }

        var isCut = ViewModel.IsCutClipboard;
        var summary = await ViewModel.ExecutePasteAsync(
            resolveMoveConflictAsync: isCut ? _dialogs.ShowMoveConflictAsync : null,
            resolveCopyConflictAsync: isCut ? null : _dialogs.ShowCopyConflictAsync).ConfigureAwait(true);
        if (summary.HasReportableFailures)
        {
            if (isCut)
            {
                await _dialogs.ShowMoveOperationErrorAsync(summary).ConfigureAwait(true);
            }
            else
            {
                await _dialogs.ShowCopyOperationErrorAsync(summary).ConfigureAwait(true);
            }
        }
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is null || e.Key != VirtualKey.Enter)
        {
            return;
        }

        await ViewModel.RefreshAsync().ConfigureAwait(true);
    }
    private async void OnCreateFolderClicked(object sender, RoutedEventArgs e)
    {
        await CreateFolderAsyncCore().ConfigureAwait(true);
    }

    private async Task CreateFolderAsyncCore()
    {
        if (ViewModel is null || string.IsNullOrWhiteSpace(ViewModel.CurrentFolderPath))
        {
            return;
        }

        var folderName = await _dialogs.ShowTextInputAsync(
            LocalizationService.GetString("Dialog.NewFolder.Title"),
            LocalizationService.GetString("Dialog.NewFolder.Primary"),
            LocalizationService.GetString("Dialog.NewFolder.DefaultName"),
            LocalizationService.GetString("Dialog.NewFolder.Placeholder")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var result = await ViewModel.ExecuteCreateFolderAsync(folderName).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            await _dialogs.ShowFileOperationErrorAsync(result.Error, "Dialog.CreateFolderFailed.Title").ConfigureAwait(true);
        }
    }

    private async void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        await RenameSelectionAsyncCore().ConfigureAwait(true);
    }

    private async Task RenameSelectionAsyncCore()
    {
        if (ViewModel is null || ViewModel.SelectedItems.Count != 1 || ViewModel.SelectedItems[0] is not PhotoListItem item)
        {
            return;
        }

        var newName = await _dialogs.ShowTextInputAsync(
            LocalizationService.GetString("Dialog.Rename.Title"),
            LocalizationService.GetString("Dialog.Rename.Primary"),
            item.FileName,
            LocalizationService.GetString("Dialog.Rename.Placeholder")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var result = await ViewModel.ExecuteRenameAsync(item, newName).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            await _dialogs.ShowFileOperationErrorAsync(result.Error, "Dialog.RenameFailed.Title").ConfigureAwait(true);
        }
    }

    private async void OnMoveClicked(object sender, RoutedEventArgs e)
    {
        await MoveSelectionAsyncCore().ConfigureAwait(true);
    }

    private async Task MoveSelectionAsyncCore()
    {
        if (ViewModel is null || ViewModel.SelectedItems.Count == 0)
        {
            return;
        }

        if (ViewModel.IsSingleFolderSelected)
        {
            await ViewModel.LoadFolderAsync(ViewModel.SelectedItems[0].FilePath).ConfigureAwait(true);
            return;
        }

        var destination = await _dialogs.PickFolderAsync(PickerLocationId.PicturesLibrary).ConfigureAwait(true);
        if (destination is null)
        {
            return;
        }

        var summary = await ViewModel.ExecuteMoveItemsToFolderAsync(
            ViewModel.SelectedItems, destination.Path, _dialogs.ShowMoveConflictAsync)
            .ConfigureAwait(true);
        if (summary.HasReportableFailures)
        {
            await _dialogs.ShowMoveOperationErrorAsync(summary).ConfigureAwait(true);
        }
    }

    private async void OnMoveToParentClicked(object sender, RoutedEventArgs e)
    {
        await MoveSelectionToParentAsyncCore().ConfigureAwait(true);
    }

    private async Task MoveSelectionToParentAsyncCore()
    {
        if (ViewModel is null || ViewModel.SelectedItems.Count == 0)
        {
            return;
        }

        var summary = await ViewModel.ExecuteMoveToParentAsync().ConfigureAwait(true);
        if (summary.HasReportableFailures)
        {
            await _dialogs.ShowMoveOperationErrorAsync(summary).ConfigureAwait(true);
        }
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        await DeleteSelectionAsyncCore().ConfigureAwait(true);
    }

    private async Task DeleteSelectionAsyncCore()
    {
        if (ViewModel is null || ViewModel.SelectedItems.Count == 0)
        {
            return;
        }

        var confirmed = await _dialogs.ShowConfirmationAsync(
            LocalizationService.GetString("Dialog.DeleteConfirm.Title"),
            ViewModel.BuildDeleteConfirmationMessage(),
            LocalizationService.GetString("Common.Delete")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var itemsToDelete = ViewModel.SelectedItems.ToList();
        var summary = await ViewModel.ExecuteDeleteItemsAsync(itemsToDelete).ConfigureAwait(true);
        if (summary.HasReportableFailures)
        {
            await _dialogs.ShowDeleteOperationErrorAsync(summary).ConfigureAwait(true);
        }
    }

    private void OnOpenInExplorerClicked(object sender, RoutedEventArgs e)
    {
        var item = ViewModel?.SelectedItem;
        if (item is null)
        {
            return;
        }

        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{item.FilePath}\"",
            UseShellExecute = true
        });
    }

    private async void OnOpenFolderInExplorerClicked(object sender, RoutedEventArgs e)
    {
        var folderPath = ViewModel?.CurrentFolderPath;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        await Launcher.LaunchFolderPathAsync(folderPath);
    }

    private void OnCopyPathClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || ViewModel.SelectedItems.Count == 0)
        {
            return;
        }

        var paths = string.Join(Environment.NewLine, ViewModel.SelectedItems.Select(item => item.FilePath));
        var dataPackage = new DataPackage();
        dataPackage.SetText(paths);
        Clipboard.SetContent(dataPackage);
    }

    private async void OnOpenInGoogleMapsClicked(object sender, RoutedEventArgs e)
    {
        var metadata = ViewModel?.Status.SelectedMetadata;
        if (metadata?.HasValidLocation != true)
        {
            return;
        }

        var url = $"https://www.google.com/maps?q={metadata.Latitude!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},{metadata.Longitude!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException
            or ArgumentException)
        {
            AppLog.Error("Failed to launch Google Maps URL.", ex);
        }
    }

    private async void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        await CopySelectionAsyncCore().ConfigureAwait(true);
    }

    private async Task CopySelectionAsyncCore()
    {
        if (ViewModel is null || ViewModel.SelectedItems.Count == 0)
        {
            return;
        }

        var destination = await _dialogs.PickFolderAsync(PickerLocationId.PicturesLibrary).ConfigureAwait(true);
        if (destination is null)
        {
            return;
        }

        var summary = await ViewModel.ExecuteCopyItemsToFolderAsync(ViewModel.SelectedItems, destination.Path)
            .ConfigureAwait(true);
        if (summary.HasReportableFailures)
        {
            await _dialogs.ShowCopyOperationErrorAsync(summary).ConfigureAwait(true);
        }
    }

    private void OnDetailsSortClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button button || button.Tag is not string tag)
        {
            return;
        }

        if (!Enum.TryParse(tag, out FileSortColumn column))
        {
            return;
        }

        ViewModel.ToggleSort(column);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private async Task OpenFolderPickerAsync()
    {
        if (ViewModel is null)
        {
            return;
        }

        var folder = await _dialogs.PickFolderAsync(PickerLocationId.PicturesLibrary).ConfigureAwait(true);

        if (folder is null)
        {
            return;
        }

        await ViewModel.LoadFolderAsync(folder.Path).ConfigureAwait(true);
    }

    private ListViewBase? GetFileListView()
    {
        if (FileListDetails.Visibility == Visibility.Visible)
        {
            return FileListDetails;
        }

        if (FileListIcon.Visibility == Visibility.Visible)
        {
            return FileListIcon;
        }

        if (FileListList.Visibility == Visibility.Visible)
        {
            return FileListList;
        }

        return FileListDetails as ListViewBase
            ?? FileListIcon as ListViewBase
            ?? FileListList as ListViewBase;
    }
}
