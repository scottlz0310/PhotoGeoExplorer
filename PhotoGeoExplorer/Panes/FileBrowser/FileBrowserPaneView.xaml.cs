using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
    private List<PhotoListItem>? _dragItems;
    private bool _suppressBreadcrumbNavigation;
    private bool _isWaitingForXamlRoot;

    public FileBrowserPaneView()
    {
        InitializeComponent();
    }

    public Window? HostWindow { get; set; }

    public event EventHandler? EditExifRequested;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // DispatcherQueue は Loaded 時に確実に利用可能なため、ここで設定
        if (ViewModel is not null && DispatcherQueue is not null)
        {
            ViewModel.SetDispatcherQueue(DispatcherQueue);
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
        foreach (var item in segment.Children.Select(child =>
                 {
                     var menuItem = new MenuFlyoutItem
                     {
                         Text = child.Name,
                         Tag = child.FullPath
                     };
                     menuItem.Click += OnBreadcrumbChildClicked;
                     return menuItem;
                 }))
        {
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

        e.AcceptedOperation = TryGetBreadcrumbTarget(breadcrumbBar, e, out _)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;

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

        await MoveItemsToFolderAsync(_dragItems ?? ViewModel.SelectedItems, target.FullPath)
            .ConfigureAwait(true);
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

            e.AcceptedOperation = sender is ListViewBase listView
                && TryGetDropTargetFolder(listView, RootGrid, e, out _)
                ? DataPackageOperation.Move
                : DataPackageOperation.None;

            e.Handled = true;
            return;
        }

        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
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
                await MoveItemsToFolderAsync(_dragItems ?? ViewModel.SelectedItems, targetFolder.FilePath)
                    .ConfigureAwait(true);
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

        var directory = Path.GetDirectoryName(firstFile.Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        await ViewModel.LoadFolderAsync(directory).ConfigureAwait(true);
        ViewModel.SelectItemByPath(firstFile.Path);
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
        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.Properties[InternalDragKey] = true;
    }

    private void OnFileItemsDragCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        _dragItems = null;
    }

    private void OnFileListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel is null || sender is not ListViewBase listView)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (source is not null)
        {
            var container = FindAncestor<SelectorItem>(source);
            if (container is not null
                && listView.ItemFromContainer(container) is PhotoListItem item)
            {
                ViewModel.SelectedItem = item;
            }
            else
            {
                listView.SelectedItems.Clear();
                ViewModel.SelectedItem = null;
                ViewModel.UpdateSelection(Array.Empty<PhotoListItem>());
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

    private async void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null || sender is not ListViewBase listView)
        {
            return;
        }

        var selected = listView.SelectedItems
            .OfType<PhotoListItem>()
            .ToList();
        ViewModel.UpdateSelection(selected);
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
        if (ViewModel is null)
        {
            return;
        }

        var currentFolder = ViewModel.CurrentFolderPath;
        if (string.IsNullOrWhiteSpace(currentFolder))
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

        if (ContainsInvalidFileNameChars(folderName))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.InvalidName.Title"),
                LocalizationService.GetString("Dialog.InvalidName.Detail")).ConfigureAwait(true);
            return;
        }

        var targetPath = Path.Combine(currentFolder, folderName);
        if (Directory.Exists(targetPath) || File.Exists(targetPath))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                LocalizationService.GetString("Dialog.AlreadyExists.Detail")).ConfigureAwait(true);
            return;
        }

        try
        {
            Directory.CreateDirectory(targetPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or IOException
            or NotSupportedException
            or ArgumentException
            or PathTooLongException)
        {
            AppLog.Error($"Failed to create folder: {targetPath}", ex);
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.CreateFolderFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")).ConfigureAwait(true);
            return;
        }

        await ViewModel.RefreshAsync().ConfigureAwait(true);
        ViewModel.SelectItemByPath(targetPath);
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

        var parent = Path.GetDirectoryName(item.FilePath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.RenameNotAvailable.Title"),
                LocalizationService.GetString("Dialog.RenameNotAvailable.Detail")).ConfigureAwait(true);
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

        var normalizedName = NormalizeRename(item, newName);
        if (string.Equals(normalizedName, item.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (ContainsInvalidFileNameChars(normalizedName))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.InvalidName.Title"),
                LocalizationService.GetString("Dialog.InvalidName.Detail")).ConfigureAwait(true);
            return;
        }

        var targetPath = Path.Combine(parent, normalizedName);
        if (Directory.Exists(targetPath) || File.Exists(targetPath))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                LocalizationService.GetString("Dialog.AlreadyExists.Detail")).ConfigureAwait(true);
            return;
        }

        try
        {
            if (item.IsFolder)
            {
                Directory.Move(item.FilePath, targetPath);
            }
            else
            {
                File.Move(item.FilePath, targetPath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or IOException
            or NotSupportedException
            or ArgumentException
            or PathTooLongException)
        {
            AppLog.Error($"Failed to rename item: {item.FilePath}", ex);
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.RenameFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")).ConfigureAwait(true);
            return;
        }

        await ViewModel.RefreshAsync().ConfigureAwait(true);
        ViewModel.SelectItemByPath(targetPath);
    }

    private async void OnMoveClicked(object sender, RoutedEventArgs e)
    {
        await MoveSelectionAsyncCore().ConfigureAwait(true);
    }

    private async Task MoveSelectionAsyncCore()
    {
        if (ViewModel is null)
        {
            return;
        }

        if (ViewModel.SelectedItems.Count == 0)
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

        await MoveItemsToFolderAsync(ViewModel.SelectedItems, destination.Path).ConfigureAwait(true);
    }

    private async void OnMoveToParentClicked(object sender, RoutedEventArgs e)
    {
        await MoveSelectionToParentAsyncCore().ConfigureAwait(true);
    }

    private async Task MoveSelectionToParentAsyncCore()
    {
        if (ViewModel is null)
        {
            return;
        }

        if (ViewModel.SelectedItems.Count == 0)
        {
            return;
        }

        var currentFolder = ViewModel.CurrentFolderPath;
        if (string.IsNullOrWhiteSpace(currentFolder))
        {
            return;
        }

        var parent = Directory.GetParent(currentFolder);
        if (parent is null)
        {
            return;
        }

        await MoveItemsToFolderAsync(ViewModel.SelectedItems, parent.FullName).ConfigureAwait(true);
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

        await DeleteItemsAsync(ViewModel.SelectedItems).ConfigureAwait(true);
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
            IsEnabled = viewModel.CanRenameSelection && IsJpegFile(viewModel.SelectedItem)
        };
        editExifItem.Click += OnEditExifClicked;

        flyout.Items.Add(createFolder);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(renameItem);
        flyout.Items.Add(moveItem);
        flyout.Items.Add(moveParentItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(editExifItem);
        flyout.Items.Add(deleteItem);

        return flyout;
    }

    private void OnEditExifClicked(object sender, RoutedEventArgs e)
    {
        EditExifRequested?.Invoke(this, EventArgs.Empty);
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
    private async Task MoveItemsToFolderAsync(IReadOnlyList<PhotoListItem>? items, string destinationFolder)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            return;
        }

        if (items is null || items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            var sourcePath = item.FilePath;
            var parent = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            if (IsSamePath(parent, destinationFolder))
            {
                continue;
            }

            if (item.IsFolder && IsDescendantPath(sourcePath, destinationFolder))
            {
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.MoveFailed.Title"),
                    LocalizationService.GetString("Dialog.MoveIntoSelf.Detail")).ConfigureAwait(true);
                return;
            }

            var targetPath = Path.Combine(destinationFolder, item.FileName);
            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                    LocalizationService.GetString("Dialog.AlreadyExistsDestination.Detail")).ConfigureAwait(true);
                return;
            }

            try
            {
                if (item.IsFolder)
                {
                    Directory.Move(sourcePath, targetPath);
                }
                else
                {
                    File.Move(sourcePath, targetPath);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                or IOException
                or NotSupportedException
                or ArgumentException
                or PathTooLongException)
            {
                AppLog.Error($"Failed to move item: {sourcePath}", ex);
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.MoveFailed.Title"),
                    LocalizationService.GetString("Dialog.SeeLogDetail")).ConfigureAwait(true);
                return;
            }
        }

        await ViewModel.RefreshAsync().ConfigureAwait(true);
    }

    private static bool IsSamePath(string left, string right)
    {
        var normalizedLeft = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDescendantPath(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
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

    private async Task DeleteItemsAsync(IReadOnlyList<PhotoListItem> items)
    {
        if (ViewModel is null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item.IsFolder && Directory.GetParent(item.FilePath) is null)
            {
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.DeleteNotAvailable.Title"),
                    LocalizationService.GetString("Dialog.DeleteNotAvailable.Detail")).ConfigureAwait(true);
                return;
            }

            try
            {
                if (item.IsFolder)
                {
                    Directory.Delete(item.FilePath, recursive: true);
                }
                else
                {
                    File.Delete(item.FilePath);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                or IOException
                or NotSupportedException
                or ArgumentException
                or PathTooLongException)
            {
                AppLog.Error($"Failed to delete item: {item.FilePath}", ex);
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.DeleteFailed.Title"),
                    LocalizationService.GetString("Dialog.SeeLogDetail")).ConfigureAwait(true);
                return;
            }
        }

        await ViewModel.RefreshAsync().ConfigureAwait(true);
    }

    private static string BuildDeleteMessage(PhotoListItem item)
    {
        return item.IsFolder
            ? LocalizationService.Format("Dialog.DeleteConfirm.Folder", item.FileName)
            : LocalizationService.Format("Dialog.DeleteConfirm.File", item.FileName);
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

    private static bool ContainsInvalidFileNameChars(string name)
    {
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }

    private static string NormalizeRename(PhotoListItem item, string newName)
    {
        var trimmed = newName.Trim();
        if (item.IsFolder)
        {
            return trimmed;
        }

        var originalExtension = Path.GetExtension(item.FileName);
        if (string.IsNullOrWhiteSpace(originalExtension))
        {
            return trimmed;
        }

        var newExtension = Path.GetExtension(trimmed);
        if (string.IsNullOrWhiteSpace(newExtension))
        {
            return $"{trimmed}{originalExtension}";
        }

        return trimmed;
    }

    private static bool IsJpegFile(PhotoListItem? item)
    {
        if (item is null || item.IsFolder)
        {
            return false;
        }

        var extension = Path.GetExtension(item.FilePath);
        return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
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
            var elapsed = 0;
            while (RootGrid.XamlRoot is null && elapsed < maxWaitMs)
            {
                await Task.Delay(intervalMs).ConfigureAwait(true);
                elapsed += intervalMs;
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
