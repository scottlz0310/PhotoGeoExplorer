using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.UI.Core;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace PhotoGeoExplorer.Panes.FileBrowser;

internal sealed partial class FileBrowserPaneView : UserControl
{
    private const string InternalDragKey = "PhotoGeoExplorer.InternalDrag";
    private const string DetailsColumnModifiedTag = "Modified";
    private const string DetailsColumnResolutionTag = "Resolution";
    private const string DetailsColumnSizeTag = "Size";
    private const string DetailsColumnTakenAtTag = "TakenAt";
    private const string DetailsColumnLocationTag = "Location";
    private List<PhotoListItem>? _dragItems;
    private bool _wasInternalDrop;
    private bool _suppressBreadcrumbNavigation;
    private bool _isWaitingForXamlRoot;
    private bool _fileListInputHandlersRegistered;
    private bool _suppressSelectionChangedForRightTap;
    private IReadOnlyList<PhotoListItem> _selectionBeforeChange = Array.Empty<PhotoListItem>();
    private FileBrowserPaneViewModel? _previousViewModel;
    private MenuFlyout? _detailsColumnsFlyout;
    private ToggleMenuFlyoutItem? _detailsModifiedColumnMenuItem;
    private ToggleMenuFlyoutItem? _detailsResolutionColumnMenuItem;
    private ToggleMenuFlyoutItem? _detailsSizeColumnMenuItem;
    private ToggleMenuFlyoutItem? _detailsTakenAtColumnMenuItem;
    private ToggleMenuFlyoutItem? _detailsLocationColumnMenuItem;

    public FileBrowserPaneView()
    {
        InitializeComponent();

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
        // DispatcherQueue は Loaded 時に確実に利用可能なため、ここで設定
        if (ViewModel is not null && DispatcherQueue is not null)
        {
            ViewModel.SetDispatcherQueue(DispatcherQueue);
        }

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
        // DataContext 変更時にも設定を試みる（ViewModel が後から設定される場合に備える）
        // SetDispatcherQueue 内で null チェックがあるため、DispatcherQueue が null でも安全
        if (ViewModel is not null && DispatcherQueue is not null)
        {
            ViewModel.SetDispatcherQueue(DispatcherQueue);
        }

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

    public async Task ResetFiltersAsync()
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.ResetFilters();
        await ViewModel.RefreshAsync().ConfigureAwait(true);
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
        if (ViewModel is null || sender is not FrameworkElement anchor)
        {
            return;
        }

        _detailsColumnsFlyout ??= BuildDetailsColumnsFlyout();
        SyncDetailsColumnsFlyout();
        _detailsColumnsFlyout.ShowAt(anchor);
    }

    private async void OnFiltersChanged(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void OnStatusPrimaryActionClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await PerformStatusActionAsync(ViewModel.StatusPrimaryAction).ConfigureAwait(true);
    }

    private async void OnStatusSecondaryActionClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await PerformStatusActionAsync(ViewModel.StatusSecondaryAction).ConfigureAwait(true);
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
        if (_suppressBreadcrumbNavigation)
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

        ShowBreadcrumbChildrenFlyout(container, segment);
        e.Handled = true;
    }

    private async void OnBreadcrumbChildClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not MenuFlyoutItem item || item.Tag is not string folderPath)
        {
            return;
        }

        await ViewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);
    }

    private void ShowBreadcrumbChildrenFlyout(FrameworkElement anchor, BreadcrumbSegment segment)
    {
        if (segment.Children.Count == 0)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var child in segment.Children)
        {
            var item = new MenuFlyoutItem
            {
                Text = child.Name,
                Tag = child.FullPath
            };
            item.Click += OnBreadcrumbChildClicked;
            flyout.Items.Add(item);
        }

        _suppressBreadcrumbNavigation = true;
        flyout.Closed += (_, _) => _suppressBreadcrumbNavigation = false;
        flyout.ShowAt(anchor);
    }

    private void OnBreadcrumbDragOver(object sender, DragEventArgs e)
    {
        if (!IsInternalDrag(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        if (sender is not BreadcrumbBar breadcrumbBar)
        {
            return;
        }

        var accepted = TryGetBreadcrumbTarget(breadcrumbBar, e, out _)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;

        e.AcceptedOperation = accepted;
        if (accepted != DataPackageOperation.None)
        {
            e.DragUIOverride.Caption = LocalizationService.GetString("DragCaption.Move");
            e.DragUIOverride.IsCaptionVisible = true;
        }

        e.Handled = true;
    }

    private async void OnBreadcrumbDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is null || !IsInternalDrag(e))
        {
            return;
        }

        if (sender is not BreadcrumbBar breadcrumbBar)
        {
            return;
        }

        if (!TryGetBreadcrumbTarget(breadcrumbBar, e, out var target))
        {
            return;
        }

        _wasInternalDrop = true;
        var items = _dragItems ?? ViewModel.SelectedItems;
        var summary = await ViewModel.ExecuteMoveItemsToFolderAsync(items, target.FullPath).ConfigureAwait(true);
        if (summary.HasFailures)
        {
            await ShowMoveOperationErrorDialogAsync(summary).ConfigureAwait(true);
        }
    }
    private void OnFileListDragOver(object sender, DragEventArgs e)
    {
        if (IsInternalDrag(e))
        {
            if (sender is not ListViewBase)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.Handled = true;
                return;
            }

            if (sender is ListViewBase listView && TryGetDropTargetFolder(listView, RootGrid, e, out _))
            {
                var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
                var isCopy = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
                e.AcceptedOperation = isCopy ? DataPackageOperation.Copy : DataPackageOperation.Move;
                e.DragUIOverride.Caption = isCopy
                    ? LocalizationService.GetString("DragCaption.Copy")
                    : LocalizationService.GetString("DragCaption.Move");
                e.DragUIOverride.IsCaptionVisible = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }

            e.Handled = true;
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = LocalizationService.GetString("DragCaption.Copy");
            e.DragUIOverride.IsCaptionVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }

        e.Handled = true;
    }

    private async void OnFileListDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (IsInternalDrag(e))
        {
            if (sender is not ListViewBase)
            {
                return;
            }

            if (sender is ListViewBase listView
                && TryGetDropTargetFolder(listView, RootGrid, e, out var targetFolder))
            {
                _wasInternalDrop = true;
                var selectedItems = _dragItems ?? ViewModel.SelectedItems;
                FileOperationSummary summary;
                if (e.AcceptedOperation == DataPackageOperation.Copy)
                {
                    summary = await ViewModel.ExecuteCopyItemsToFolderAsync(
                        selectedItems, targetFolder.FilePath, ShowCopyConflictDialogAsync)
                        .ConfigureAwait(true);
                    if (summary.HasFailures && summary.Failures.Any(f => f.Error != FileOperationError.Cancelled))
                    {
                        await ShowCopyOperationErrorDialogAsync(summary).ConfigureAwait(true);
                    }
                }
                else
                {
                    summary = await ViewModel.ExecuteMoveItemsToFolderAsync(selectedItems, targetFolder.FilePath)
                        .ConfigureAwait(true);
                    if (summary.HasFailures)
                    {
                        await ShowMoveOperationErrorDialogAsync(summary).ConfigureAwait(true);
                    }
                }
            }

            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        if (items is null || items.Count == 0)
        {
            return;
        }

        StorageFolder? folder = null;
        StorageFile? firstFile = null;
        foreach (var item in items)
        {
            if (item is StorageFolder droppedFolder)
            {
                folder = droppedFolder;
                break;
            }

            if (firstFile is null && item is StorageFile droppedFile)
            {
                firstFile = droppedFile;
            }
        }

        if (folder is not null)
        {
            await ViewModel.LoadFolderAsync(folder.Path).ConfigureAwait(true);
            return;
        }

        if (firstFile is null)
        {
            return;
        }

        await ViewModel.HandleExternalFileDropAsync(firstFile.Path).ConfigureAwait(true);
    }

    private void OnFileItemsDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        _dragItems = e.Items.OfType<PhotoListItem>().ToList();
        if (_dragItems.Count == 0 && ViewModel.SelectedItems.Count > 0)
        {
            _dragItems = ViewModel.SelectedItems.ToList();
        }

        // Copy と Move の両方を提供することで、外部アプリがドライブ情報に基づき操作を決定できる
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        e.Data.Properties[InternalDragKey] = true;

        // ドラッグアウト: StorageItems を遅延提供して Explorer や他アプリが受け取れるようにする
        var capturedItems = _dragItems.ToList();
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var storageItems = new List<IStorageItem>();
                foreach (var item in capturedItems)
                {
                    try
                    {
                        IStorageItem storageItem = item.IsFolder
                            ? await StorageFolder.GetFolderFromPathAsync(item.FilePath)
                            : await StorageFile.GetFileFromPathAsync(item.FilePath);
                        storageItems.Add(storageItem);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
                        or PathTooLongException
                        or COMException)
                    {
                        AppLog.Error($"Failed to provide drag storage item: {item.FilePath}", ex);
                    }
                }

                request.SetData(storageItems);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private async void OnFileItemsDragCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        var items = _dragItems;
        var wasInternal = _wasInternalDrop;
        _dragItems = null;
        _wasInternalDrop = false;

        // 外部アプリへの Move 完了後にリストを更新して移動済みアイテムを除去する
        if (ViewModel is not null
            && items is { Count: > 0 }
            && e.DropResult == DataPackageOperation.Move
            && !wasInternal)
        {
            await ViewModel.RefreshAsync().ConfigureAwait(false);
        }
    }

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
                var currentSelection = ViewModel.SelectedItems.ToList();

                // 複数選択を維持すべきかを判定する:
                // ① ViewModel が複数のまま（SelectionChanged なし）→ currentSelection を使用
                // ② ViewModel が単数に変化（SelectionChanged あり）→ priorSelection を使用
                IReadOnlyList<PhotoListItem> selectionToRestore;
                if (currentSelection.Count > 1 && currentSelection.Contains(item))
                {
                    selectionToRestore = currentSelection;
                }
                else if (priorSelection.Count > 1 && priorSelection.Contains(item))
                {
                    selectionToRestore = priorSelection;
                }
                else
                {
                    selectionToRestore = Array.Empty<PhotoListItem>();
                }

                ViewModel.BeginBatchSelectionUpdate();
                _suppressSelectionChangedForRightTap = true;
                try
                {
                    listView.SelectedItems.Clear();

                    // item を先頭に追加することで TwoWay バインディングが SelectedItem=item をセットする。
                    // 後続の ViewModel.SelectedItem=item は SetProperty が false を返すため
                    // TwoWay が再発火せず SelectedItems が単数に上書きされない。
                    listView.SelectedItems.Add(item);
                    var toRestore = selectionToRestore.Count > 0 ? selectionToRestore : (IReadOnlyList<PhotoListItem>)new[] { item };
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

        var flyout = BuildFileContextFlyout();
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
                if (ViewModel.SelectedItems.Count > 0)
                {
                    ViewModel.SetClipboard(ViewModel.SelectedItems, ClipboardOperation.Copy);
                }
                break;
            case VirtualKey.X when ctrl:
                e.Handled = true;
                if (ViewModel.SelectedItems.Count > 0)
                {
                    ViewModel.SetClipboard(ViewModel.SelectedItems, ClipboardOperation.Cut);
                }
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
            resolveMoveConflictAsync: isCut ? ShowMoveConflictDialogAsync : null,
            resolveCopyConflictAsync: isCut ? null : ShowCopyConflictDialogAsync).ConfigureAwait(true);
        if (summary.HasFailures && summary.Failures.Any(f => f.Error != FileOperationError.Cancelled))
        {
            if (isCut)
            {
                await ShowMoveOperationErrorDialogAsync(summary).ConfigureAwait(true);
            }
            else
            {
                await ShowCopyOperationErrorDialogAsync(summary).ConfigureAwait(true);
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

        var folderName = await ShowTextInputDialogAsync(
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
            await ShowFileOperationErrorDialogAsync(result.Error, "Dialog.CreateFolderFailed.Title").ConfigureAwait(true);
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

        var newName = await ShowTextInputDialogAsync(
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
            await ShowFileOperationErrorDialogAsync(result.Error, "Dialog.RenameFailed.Title").ConfigureAwait(true);
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

        if (ViewModel.SelectedItems.Count == 1 && ViewModel.SelectedItems[0].IsFolder)
        {
            await ViewModel.LoadFolderAsync(ViewModel.SelectedItems[0].FilePath).ConfigureAwait(true);
            return;
        }

        var destination = await PickFolderAsync(PickerLocationId.PicturesLibrary).ConfigureAwait(true);
        if (destination is null)
        {
            return;
        }

        var summary = await ViewModel.ExecuteMoveItemsToFolderAsync(
            ViewModel.SelectedItems, destination.Path, ShowMoveConflictDialogAsync)
            .ConfigureAwait(true);
        if (summary.Failures.Any(f => f.Error != FileOperationError.Cancelled))
        {
            await ShowMoveOperationErrorDialogAsync(summary).ConfigureAwait(true);
        }
    }

    private async Task<ConflictResolution> ShowMoveConflictDialogAsync(string fileName, bool isFolder)
    {
        var detail = LocalizationService.Format("Dialog.MoveConflict.Detail", fileName);
        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("Dialog.MoveConflict.Title"),
            Content = detail,
            PrimaryButtonText = LocalizationService.GetString("Dialog.MoveConflict.Overwrite"),
            SecondaryButtonText = LocalizationService.GetString("Dialog.MoveConflict.Skip"),
            CloseButtonText = LocalizationService.GetString("Dialog.MoveConflict.Cancel"),
            XamlRoot = XamlRoot,
        };

        // StackPanel で「すべて上書き」「すべてスキップ」ボタンを追加
        var overwriteAllButton = new Button
        {
            Content = LocalizationService.GetString("Dialog.MoveConflict.OverwriteAll"),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 8, 0),
        };
        var skipAllButton = new Button
        {
            Content = LocalizationService.GetString("Dialog.MoveConflict.SkipAll"),
        };

        ConflictResolution? extraChoice = null;
        overwriteAllButton.Click += (_, _) =>
        {
            extraChoice = ConflictResolution.OverwriteAll;
            dialog.Hide();
        };
        skipAllButton.Click += (_, _) =>
        {
            extraChoice = ConflictResolution.SkipAll;
            dialog.Hide();
        };

        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = detail, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                    Children = { overwriteAllButton, skipAllButton },
                },
            },
        };

        var result = await dialog.ShowAsync();
        if (extraChoice.HasValue)
        {
            return extraChoice.Value;
        }

        return result switch
        {
            ContentDialogResult.Primary => ConflictResolution.Overwrite,
            ContentDialogResult.Secondary => ConflictResolution.Skip,
            _ => ConflictResolution.Cancel,
        };
    }

    private async Task<ConflictResolution> ShowCopyConflictDialogAsync(string fileName, bool isFolder)
    {
        var detail = LocalizationService.Format("Dialog.CopyConflict.Detail", fileName);
        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("Dialog.CopyConflict.Title"),
            Content = detail,
            PrimaryButtonText = LocalizationService.GetString("Dialog.CopyConflict.Overwrite"),
            SecondaryButtonText = LocalizationService.GetString("Dialog.CopyConflict.Skip"),
            CloseButtonText = LocalizationService.GetString("Dialog.CopyConflict.Cancel"),
            XamlRoot = XamlRoot,
        };

        var overwriteAllButton = new Button
        {
            Content = LocalizationService.GetString("Dialog.CopyConflict.OverwriteAll"),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 8, 0),
        };
        var skipAllButton = new Button
        {
            Content = LocalizationService.GetString("Dialog.CopyConflict.SkipAll"),
        };

        ConflictResolution? extraChoice = null;
        overwriteAllButton.Click += (_, _) =>
        {
            extraChoice = ConflictResolution.OverwriteAll;
            dialog.Hide();
        };
        skipAllButton.Click += (_, _) =>
        {
            extraChoice = ConflictResolution.SkipAll;
            dialog.Hide();
        };

        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = detail, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                    Children = { overwriteAllButton, skipAllButton },
                },
            },
        };

        var result = await dialog.ShowAsync();
        if (extraChoice.HasValue)
        {
            return extraChoice.Value;
        }

        return result switch
        {
            ContentDialogResult.Primary => ConflictResolution.Overwrite,
            ContentDialogResult.Secondary => ConflictResolution.Skip,
            _ => ConflictResolution.Cancel,
        };
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
        if (summary.HasFailures)
        {
            await ShowMoveOperationErrorDialogAsync(summary).ConfigureAwait(true);
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

        var message = ViewModel.SelectedItems.Count == 1
            ? BuildDeleteMessage(ViewModel.SelectedItems[0])
            : LocalizationService.Format("Dialog.DeleteConfirm.Multiple", ViewModel.SelectedItems.Count);
        var confirmed = await ShowConfirmationDialogAsync(
            LocalizationService.GetString("Dialog.DeleteConfirm.Title"),
            message,
            LocalizationService.GetString("Common.Delete")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var itemsToDelete = ViewModel.SelectedItems.ToList();
        var summary = await ViewModel.ExecuteDeleteItemsAsync(itemsToDelete).ConfigureAwait(true);
        if (summary.HasFailures)
        {
            await ShowDeleteOperationErrorDialogAsync(summary).ConfigureAwait(true);
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
        var metadata = ViewModel?.SelectedMetadata;
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

        var destination = await PickFolderAsync(PickerLocationId.PicturesLibrary).ConfigureAwait(true);
        if (destination is null)
        {
            return;
        }

        var summary = await ViewModel.ExecuteCopyItemsToFolderAsync(ViewModel.SelectedItems, destination.Path)
            .ConfigureAwait(true);
        if (summary.HasFailures)
        {
            await ShowCopyOperationErrorDialogAsync(summary).ConfigureAwait(true);
        }
    }

    private Task ShowCopyOperationErrorDialogAsync(FileOperationSummary summary)
    {
        var firstError = summary.Failures[0].Error;
        var (title, message) = firstError switch
        {
            FileOperationError.AlreadyExists => (
                LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                LocalizationService.GetString("Dialog.AlreadyExistsDestination.Detail")),
            FileOperationError.Unauthorized => (
                LocalizationService.GetString("Dialog.CopyFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")),
            _ => (
                LocalizationService.GetString("Dialog.CopyFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")),
        };
        return ShowMessageDialogAsync(title, message);
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

    private MenuFlyout BuildDetailsColumnsFlyout()
    {
        var flyout = new MenuFlyout();

        _detailsModifiedColumnMenuItem = CreateDetailsColumnToggleItem(
            LocalizationService.GetString("DetailsColumnMenu.Modified"),
            DetailsColumnModifiedTag);
        _detailsResolutionColumnMenuItem = CreateDetailsColumnToggleItem(
            LocalizationService.GetString("DetailsColumnMenu.Resolution"),
            DetailsColumnResolutionTag);
        _detailsSizeColumnMenuItem = CreateDetailsColumnToggleItem(
            LocalizationService.GetString("DetailsColumnMenu.Size"),
            DetailsColumnSizeTag);
        _detailsTakenAtColumnMenuItem = CreateDetailsColumnToggleItem(
            LocalizationService.GetString("DetailsColumnMenu.TakenAt"),
            DetailsColumnTakenAtTag);
        _detailsLocationColumnMenuItem = CreateDetailsColumnToggleItem(
            LocalizationService.GetString("DetailsColumnMenu.Location"),
            DetailsColumnLocationTag);

        flyout.Items.Add(_detailsModifiedColumnMenuItem);
        flyout.Items.Add(_detailsResolutionColumnMenuItem);
        flyout.Items.Add(_detailsSizeColumnMenuItem);
        flyout.Items.Add(_detailsTakenAtColumnMenuItem);
        flyout.Items.Add(_detailsLocationColumnMenuItem);

        return flyout;
    }

    private ToggleMenuFlyoutItem CreateDetailsColumnToggleItem(string text, string tag)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text,
            Tag = tag
        };
        item.Click += OnDetailsColumnMenuItemClicked;
        return item;
    }

    private void SyncDetailsColumnsFlyout()
    {
        if (ViewModel is null)
        {
            return;
        }

        if (_detailsModifiedColumnMenuItem is not null)
        {
            _detailsModifiedColumnMenuItem.IsChecked = ViewModel.ShowDetailsModifiedColumn;
        }

        if (_detailsResolutionColumnMenuItem is not null)
        {
            _detailsResolutionColumnMenuItem.IsChecked = ViewModel.ShowDetailsResolutionColumn;
        }

        if (_detailsSizeColumnMenuItem is not null)
        {
            _detailsSizeColumnMenuItem.IsChecked = ViewModel.ShowDetailsSizeColumn;
        }

        if (_detailsTakenAtColumnMenuItem is not null)
        {
            _detailsTakenAtColumnMenuItem.IsChecked = ViewModel.ShowDetailsTakenAtColumn;
        }

        if (_detailsLocationColumnMenuItem is not null)
        {
            _detailsLocationColumnMenuItem.IsChecked = ViewModel.ShowDetailsLocationColumn;
        }
    }

    private void OnDetailsColumnMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null
            || sender is not ToggleMenuFlyoutItem item
            || item.Tag is not string tag)
        {
            return;
        }

        switch (tag)
        {
            case DetailsColumnModifiedTag:
                ViewModel.ShowDetailsModifiedColumn = item.IsChecked;
                break;
            case DetailsColumnResolutionTag:
                ViewModel.ShowDetailsResolutionColumn = item.IsChecked;
                break;
            case DetailsColumnSizeTag:
                ViewModel.ShowDetailsSizeColumn = item.IsChecked;
                break;
            case DetailsColumnTakenAtTag:
                ViewModel.ShowDetailsTakenAtColumn = item.IsChecked;
                break;
            case DetailsColumnLocationTag:
                ViewModel.ShowDetailsLocationColumn = item.IsChecked;
                break;
        }
    }

    private MenuFlyout BuildFileContextFlyout()
    {
        var viewModel = ViewModel;
        var flyout = new MenuFlyout();
        if (viewModel is null)
        {
            return flyout;
        }

        var createFolder = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.NewFolder"),
            Icon = new SymbolIcon(Symbol.Folder),
            IsEnabled = viewModel.CanCreateFolder
        };
        createFolder.Click += OnCreateFolderClicked;

        var openInExplorerItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.OpenInExplorer"),
            Icon = new SymbolIcon(Symbol.Document),
            IsEnabled = viewModel.CanModifySelection
        };
        openInExplorerItem.Click += OnOpenInExplorerClicked;

        var openFolderInExplorerItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.OpenFolderInExplorer"),
            Icon = new SymbolIcon(Symbol.OpenWith),
            IsEnabled = viewModel.CanOpenInExplorer
        };
        openFolderInExplorerItem.Click += OnOpenFolderInExplorerClicked;

        var copyPathItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.CopyPath"),
            Icon = new SymbolIcon(Symbol.Copy),
            IsEnabled = viewModel.CanModifySelection
        };
        copyPathItem.Click += OnCopyPathClicked;

        var openInGoogleMapsItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.OpenInGoogleMaps"),
            Icon = new SymbolIcon(Symbol.Map),
            IsEnabled = viewModel.CanOpenInGoogleMaps
        };
        openInGoogleMapsItem.Click += OnOpenInGoogleMapsClicked;

        var renameItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Rename"),
            Icon = new SymbolIcon(Symbol.Edit),
            IsEnabled = viewModel.CanRenameSelection
        };
        renameItem.Click += OnRenameClicked;

        var moveItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Move"),
            Icon = new SymbolIcon(Symbol.Forward),
            IsEnabled = viewModel.CanModifySelection
        };
        moveItem.Click += OnMoveClicked;

        var moveParentItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.MoveToParent"),
            Icon = new SymbolIcon(Symbol.Up),
            IsEnabled = viewModel.CanMoveToParentSelection
        };
        moveParentItem.Click += OnMoveToParentClicked;

        var copyItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Copy"),
            Icon = new SymbolIcon(Symbol.Copy),
            IsEnabled = viewModel.CanCopySelection
        };
        copyItem.Click += OnCopyClicked;

        var deleteItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Delete"),
            Icon = new SymbolIcon(Symbol.Delete),
            IsEnabled = viewModel.CanModifySelection
        };
        deleteItem.Click += OnDeleteClicked;

        var editExifItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.EditExif"),
            Icon = new SymbolIcon(Symbol.Edit),
            Command = viewModel.EditExifCommand,
            IsEnabled = viewModel.CanEditExif
        };
        AutomationProperties.SetAutomationId(editExifItem, "FileBrowser.EditExifMenuItem");

        flyout.Items.Add(createFolder);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(openInExplorerItem);
        flyout.Items.Add(openFolderInExplorerItem);
        flyout.Items.Add(copyPathItem);
        flyout.Items.Add(openInGoogleMapsItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(renameItem);
        flyout.Items.Add(moveItem);
        flyout.Items.Add(moveParentItem);
        flyout.Items.Add(copyItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(editExifItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(deleteItem);

        return flyout;
    }

    private static bool IsInternalDrag(DragEventArgs e)
    {
        if (!e.DataView.Properties.TryGetValue(InternalDragKey, out var value))
        {
            return false;
        }

        return value is bool isInternal && isInternal;
    }

    private static bool TryGetDropTargetFolder(ListViewBase listView, UIElement root, DragEventArgs e, out PhotoListItem target)
    {
        target = null!;
        var point = e.GetPosition(root);
        var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, root);
        foreach (var element in elements)
        {
            var container = element as SelectorItem ?? FindAncestor<SelectorItem>(element);
            if (container is null)
            {
                continue;
            }

            if (!IsDescendantOf(container, listView))
            {
                continue;
            }

            if (listView.ItemFromContainer(container) is not PhotoListItem item || !item.IsFolder)
            {
                continue;
            }

            target = item;
            return true;
        }

        return false;
    }

    private bool TryGetBreadcrumbTarget(BreadcrumbBar breadcrumbBar, DragEventArgs e, out BreadcrumbSegment target)
    {
        target = null!;
        var point = e.GetPosition(RootGrid);
        var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, RootGrid);
        foreach (var element in elements)
        {
            var container = element as BreadcrumbBarItem ?? FindAncestor<BreadcrumbBarItem>(element);
            if (container is null)
            {
                continue;
            }

            if (!IsDescendantOf(container, breadcrumbBar))
            {
                continue;
            }

            if (container.DataContext is not BreadcrumbSegment segment)
            {
                continue;
            }

            target = segment;
            return true;
        }

        return false;
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

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject ancestor)
    {
        var current = child;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static string BuildDeleteMessage(PhotoListItem item)
    {
        return item.IsFolder
            ? LocalizationService.Format("Dialog.DeleteConfirm.Folder", item.FileName)
            : LocalizationService.Format("Dialog.DeleteConfirm.File", item.FileName);
    }

    private Task ShowFileOperationErrorDialogAsync(FileOperationError error, string defaultTitleKey)
    {
        var (title, message) = error switch
        {
            FileOperationError.InvalidName => (
                LocalizationService.GetString("Dialog.InvalidName.Title"),
                LocalizationService.GetString("Dialog.InvalidName.Detail")),
            FileOperationError.AlreadyExists => (
                LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                LocalizationService.GetString("Dialog.AlreadyExists.Detail")),
            FileOperationError.NoParent => (
                LocalizationService.GetString("Dialog.RenameNotAvailable.Title"),
                LocalizationService.GetString("Dialog.RenameNotAvailable.Detail")),
            FileOperationError.Unauthorized => (
                LocalizationService.GetString(defaultTitleKey),
                LocalizationService.GetString("Dialog.SeeLogDetail")),
            _ => (
                LocalizationService.GetString(defaultTitleKey),
                LocalizationService.GetString("Dialog.SeeLogDetail")),
        };
        return ShowMessageDialogAsync(title, message);
    }

    private Task ShowMoveOperationErrorDialogAsync(FileOperationSummary summary)
    {
        var firstError = summary.Failures[0].Error;
        var (title, message) = firstError switch
        {
            FileOperationError.DescendantPath => (
                LocalizationService.GetString("Dialog.MoveFailed.Title"),
                LocalizationService.GetString("Dialog.MoveIntoSelf.Detail")),
            FileOperationError.AlreadyExists => (
                LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                LocalizationService.GetString("Dialog.AlreadyExistsDestination.Detail")),
            FileOperationError.Unauthorized => (
                LocalizationService.GetString("Dialog.MoveFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")),
            _ => (
                LocalizationService.GetString("Dialog.MoveFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")),
        };
        return ShowMessageDialogAsync(title, message);
    }

    private Task ShowDeleteOperationErrorDialogAsync(FileOperationSummary summary)
    {
        var firstError = summary.Failures[0].Error;
        var (title, message) = firstError switch
        {
            FileOperationError.NoParent => (
                LocalizationService.GetString("Dialog.DeleteNotAvailable.Title"),
                LocalizationService.GetString("Dialog.DeleteNotAvailable.Detail")),
            FileOperationError.Unauthorized => (
                LocalizationService.GetString("Dialog.DeleteFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")),
            _ => (
                LocalizationService.GetString("Dialog.DeleteFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")),
        };
        return ShowMessageDialogAsync(title, message);
    }

    private async Task OpenFolderPickerAsync()
    {
        if (ViewModel is null)
        {
            return;
        }

        var folder = await PickFolderAsync(PickerLocationId.PicturesLibrary).ConfigureAwait(true);

        if (folder is null)
        {
            return;
        }

        await ViewModel.LoadFolderAsync(folder.Path).ConfigureAwait(true);
    }

    private async Task<StorageFolder?> PickFolderAsync(PickerLocationId startLocation)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = startLocation
        };
        picker.FileTypeFilter.Add("*");

        if (HostWindow is null)
        {
            AppLog.Error("HostWindow is not set for FileBrowserPaneView.");
            return null;
        }

        var hwnd = WindowNative.GetWindowHandle(HostWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        try
        {
            return await picker.PickSingleFolderAsync().AsTask().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
        }

        return null;
    }

    private async Task<string?> ShowTextInputDialogAsync(
        string title,
        string primaryButtonText,
        string? defaultText,
        string placeholderText)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return null;
        }

        var textBox = new TextBox
        {
            Text = defaultText ?? string.Empty,
            PlaceholderText = placeholderText,
            MinWidth = 260
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = LocalizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        textBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        };
        dialog.Opened += (_, _) =>
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        };

        var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        var value = textBox.Text.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<bool> ShowConfirmationDialogAsync(
        string title,
        string message,
        string primaryButtonText)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = LocalizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        return result == ContentDialogResult.Primary;
    }

    private async Task ShowMessageDialogAsync(string title, string message)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = LocalizationService.GetString("Common.Ok"),
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync().AsTask().ConfigureAwait(true);
    }

    private async Task<bool> EnsureXamlRootAsync()
    {
        const int maxWaitMs = 3000;
        const int intervalMs = 50;

        if (RootGrid.XamlRoot is not null)
        {
            return true;
        }

        // 既に別の呼び出しで待機中の場合は、重複してイベントハンドラを登録しない
        if (_isWaitingForXamlRoot)
        {
            // ポーリングのみで待機
            var pollingElapsed = 0;
            while (RootGrid.XamlRoot is null && pollingElapsed < maxWaitMs)
            {
                await Task.Delay(intervalMs).ConfigureAwait(true);
                pollingElapsed += intervalMs;
            }
            return RootGrid.XamlRoot is not null;
        }

        _isWaitingForXamlRoot = true;

        AppLog.Info("EnsureXamlRootAsync: XamlRoot is null, waiting for it to become available...");

        var tcs = new TaskCompletionSource<bool>();
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= OnLoaded;
            tcs.TrySetResult(true);
        }

        RootGrid.Loaded += OnLoaded;

        var elapsed = 0;
        while (RootGrid.XamlRoot is null && elapsed < maxWaitMs)
        {
            await Task.Delay(intervalMs).ConfigureAwait(true);
            elapsed += intervalMs;

            if (tcs.Task.IsCompleted)
            {
                break;
            }
        }

        RootGrid.Loaded -= OnLoaded;
        _isWaitingForXamlRoot = false;

        if (RootGrid.XamlRoot is not null)
        {
            AppLog.Info($"EnsureXamlRootAsync: XamlRoot became available after {elapsed}ms.");
            return true;
        }

        AppLog.Info($"EnsureXamlRootAsync: XamlRoot still null after {elapsed}ms, giving up.");
        return false;
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
