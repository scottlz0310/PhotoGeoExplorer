using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// FileBrowser ペインのドラッグ＆ドロップ処理を担う View 層ハンドラ。
/// 内部項目の移動/コピー・外部ファイル/フォルダの受け入れ・Explorer 等へのドラッグアウト・
/// ブレッドクラムへのドロップを扱う。ビジュアルツリー操作・DataPackage 操作を伴う純粋な
/// View 責務のため、ViewModel ではなく View 層のヘルパーとして分離する（MVVM ガードレール準拠）。
/// </summary>
internal sealed class FileBrowserDragDropHandler
{
    private const string InternalDragKey = "PhotoGeoExplorer.InternalDrag";

    private readonly FrameworkElement _root;
    private readonly Func<FileBrowserPaneViewModel?> _viewModelAccessor;
    private readonly FileBrowserDialogs _dialogs;

    private List<PhotoListItem>? _dragItems;
    private bool _wasInternalDrop;

    public FileBrowserDragDropHandler(
        FrameworkElement root,
        Func<FileBrowserPaneViewModel?> viewModelAccessor,
        FileBrowserDialogs dialogs)
    {
        _root = root;
        _viewModelAccessor = viewModelAccessor;
        _dialogs = dialogs;
    }

    private FileBrowserPaneViewModel? ViewModel => _viewModelAccessor();

    public void OnBreadcrumbDragOver(object sender, DragEventArgs e)
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

    public async Task OnBreadcrumbDropAsync(object sender, DragEventArgs e)
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
            await _dialogs.ShowMoveOperationErrorAsync(summary).ConfigureAwait(true);
        }
    }

    public void OnFileListDragOver(object sender, DragEventArgs e)
    {
        if (IsInternalDrag(e))
        {
            if (sender is not ListViewBase)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.Handled = true;
                return;
            }

            if (sender is ListViewBase listView && TryGetDropTargetFolder(listView, e, out _))
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

    public async Task OnFileListDropAsync(object sender, DragEventArgs e)
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
                && TryGetDropTargetFolder(listView, e, out var targetFolder))
            {
                _wasInternalDrop = true;
                var selectedItems = _dragItems ?? ViewModel.SelectedItems;
                FileOperationSummary summary;
                if (e.AcceptedOperation == DataPackageOperation.Copy)
                {
                    summary = await ViewModel.ExecuteCopyItemsToFolderAsync(
                        selectedItems, targetFolder.FilePath, _dialogs.ShowCopyConflictAsync)
                        .ConfigureAwait(true);
                    if (summary.HasFailures && summary.Failures.Any(f => f.Error != FileOperationError.Cancelled))
                    {
                        await _dialogs.ShowCopyOperationErrorAsync(summary).ConfigureAwait(true);
                    }
                }
                else
                {
                    summary = await ViewModel.ExecuteMoveItemsToFolderAsync(selectedItems, targetFolder.FilePath)
                        .ConfigureAwait(true);
                    if (summary.HasFailures)
                    {
                        await _dialogs.ShowMoveOperationErrorAsync(summary).ConfigureAwait(true);
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

    public void OnFileItemsDragStarting(object sender, DragItemsStartingEventArgs e)
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

    public async Task OnFileItemsDragCompletedAsync(object sender, DragItemsCompletedEventArgs e)
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

    private static bool IsInternalDrag(DragEventArgs e)
    {
        if (!e.DataView.Properties.TryGetValue(InternalDragKey, out var value))
        {
            return false;
        }

        return value is bool isInternal && isInternal;
    }

    private bool TryGetDropTargetFolder(ListViewBase listView, DragEventArgs e, out PhotoListItem target)
    {
        target = null!;
        var point = e.GetPosition(_root);
        var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, _root);
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
        var point = e.GetPosition(_root);
        var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, _root);
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
}
