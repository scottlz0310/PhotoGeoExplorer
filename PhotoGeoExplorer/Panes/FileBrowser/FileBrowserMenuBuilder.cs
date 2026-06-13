using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// FileBrowser ペインのコンテキストメニュー・列トグルメニュー・ブレッドクラム子フォルダ
/// フライアウトの構築を担う View 層ヘルパー。宣言的な組み立てコードを View コードビハインドから
/// 切り出して View を薄く保つ（メニュー構築は純粋な View 責務）。コンテキストメニュー項目の
/// クリック処理はダイアログ操作を伴う View 責務のため、ハンドラを受け取り View 側に残す。
/// </summary>
internal sealed class FileBrowserMenuBuilder
{
    private readonly Func<FileBrowserPaneViewModel?> _viewModelAccessor;
    private readonly FileContextMenuHandlers _contextMenuHandlers;

    private MenuFlyout? _detailsColumnsFlyout;
    private IReadOnlyList<DetailsColumnToggle>? _detailsColumnToggles;

    public FileBrowserMenuBuilder(
        Func<FileBrowserPaneViewModel?> viewModelAccessor,
        FileContextMenuHandlers contextMenuHandlers)
    {
        _viewModelAccessor = viewModelAccessor;
        _contextMenuHandlers = contextMenuHandlers;
    }

    private FileBrowserPaneViewModel? ViewModel => _viewModelAccessor();

    /// <summary>
    /// ブレッドクラム子フォルダフライアウトの表示中は true。BreadcrumbBar の ItemClicked による
    /// 誤ナビゲーションを抑止するため View 側から参照する。
    /// </summary>
    public bool SuppressBreadcrumbNavigation { get; private set; }

    public MenuFlyout BuildFileContextFlyout()
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
        createFolder.Click += _contextMenuHandlers.CreateFolder;

        var openInExplorerItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.OpenInExplorer"),
            Icon = new SymbolIcon(Symbol.Document),
            IsEnabled = viewModel.CanModifySelection
        };
        openInExplorerItem.Click += _contextMenuHandlers.OpenInExplorer;

        var openFolderInExplorerItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.OpenFolderInExplorer"),
            Icon = new SymbolIcon(Symbol.OpenWith),
            IsEnabled = viewModel.CanOpenInExplorer
        };
        openFolderInExplorerItem.Click += _contextMenuHandlers.OpenFolderInExplorer;

        var copyPathItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.CopyPath"),
            Icon = new SymbolIcon(Symbol.Copy),
            IsEnabled = viewModel.CanModifySelection
        };
        copyPathItem.Click += _contextMenuHandlers.CopyPath;

        var openInGoogleMapsItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.OpenInGoogleMaps"),
            Icon = new SymbolIcon(Symbol.Map),
            IsEnabled = viewModel.CanOpenInGoogleMaps
        };
        openInGoogleMapsItem.Click += _contextMenuHandlers.OpenInGoogleMaps;

        var renameItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Rename"),
            Icon = new SymbolIcon(Symbol.Edit),
            IsEnabled = viewModel.CanRenameSelection
        };
        renameItem.Click += _contextMenuHandlers.Rename;

        var moveItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Move"),
            Icon = new SymbolIcon(Symbol.Forward),
            IsEnabled = viewModel.CanModifySelection
        };
        moveItem.Click += _contextMenuHandlers.Move;

        var moveParentItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.MoveToParent"),
            Icon = new SymbolIcon(Symbol.Up),
            IsEnabled = viewModel.CanMoveToParentSelection
        };
        moveParentItem.Click += _contextMenuHandlers.MoveToParent;

        var copyItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Copy"),
            Icon = new SymbolIcon(Symbol.Copy),
            IsEnabled = viewModel.CanCopySelection
        };
        copyItem.Click += _contextMenuHandlers.Copy;

        var deleteItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Delete"),
            Icon = new SymbolIcon(Symbol.Delete),
            IsEnabled = viewModel.CanModifySelection
        };
        deleteItem.Click += _contextMenuHandlers.Delete;

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

    public void ShowDetailsColumnsFlyout(FrameworkElement anchor)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        _detailsColumnsFlyout ??= BuildDetailsColumnsFlyout();
        SyncDetailsColumnsFlyout(viewModel);
        _detailsColumnsFlyout.ShowAt(anchor);
    }

    public void ShowBreadcrumbChildrenFlyout(FrameworkElement anchor, BreadcrumbSegment segment)
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

        SuppressBreadcrumbNavigation = true;
        flyout.Closed += (_, _) => SuppressBreadcrumbNavigation = false;
        flyout.ShowAt(anchor);
    }

    private MenuFlyout BuildDetailsColumnsFlyout()
    {
        _detailsColumnToggles = new[]
        {
            CreateDetailsColumnToggle(
                "DetailsColumnMenu.Modified",
                static vm => vm.ShowDetailsModifiedColumn,
                static (vm, isChecked) => vm.ShowDetailsModifiedColumn = isChecked),
            CreateDetailsColumnToggle(
                "DetailsColumnMenu.Resolution",
                static vm => vm.ShowDetailsResolutionColumn,
                static (vm, isChecked) => vm.ShowDetailsResolutionColumn = isChecked),
            CreateDetailsColumnToggle(
                "DetailsColumnMenu.Size",
                static vm => vm.ShowDetailsSizeColumn,
                static (vm, isChecked) => vm.ShowDetailsSizeColumn = isChecked),
            CreateDetailsColumnToggle(
                "DetailsColumnMenu.TakenAt",
                static vm => vm.ShowDetailsTakenAtColumn,
                static (vm, isChecked) => vm.ShowDetailsTakenAtColumn = isChecked),
            CreateDetailsColumnToggle(
                "DetailsColumnMenu.Location",
                static vm => vm.ShowDetailsLocationColumn,
                static (vm, isChecked) => vm.ShowDetailsLocationColumn = isChecked),
        };

        var flyout = new MenuFlyout();
        foreach (var toggle in _detailsColumnToggles)
        {
            flyout.Items.Add(toggle.Item);
        }

        return flyout;
    }

    private DetailsColumnToggle CreateDetailsColumnToggle(
        string textResourceKey,
        Func<FileBrowserPaneViewModel, bool> getter,
        Action<FileBrowserPaneViewModel, bool> setter)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = LocalizationService.GetString(textResourceKey)
        };
        item.Click += OnDetailsColumnMenuItemClicked;
        return new DetailsColumnToggle(item, getter, setter);
    }

    private void SyncDetailsColumnsFlyout(FileBrowserPaneViewModel viewModel)
    {
        if (_detailsColumnToggles is null)
        {
            return;
        }

        foreach (var toggle in _detailsColumnToggles)
        {
            toggle.Item.IsChecked = toggle.Getter(viewModel);
        }
    }

    private void OnDetailsColumnMenuItemClicked(object sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null
            || _detailsColumnToggles is null
            || sender is not ToggleMenuFlyoutItem item)
        {
            return;
        }

        foreach (var toggle in _detailsColumnToggles)
        {
            if (ReferenceEquals(toggle.Item, item))
            {
                toggle.Setter(viewModel, item.IsChecked);
                return;
            }
        }
    }

    private async void OnBreadcrumbChildClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not MenuFlyoutItem item || item.Tag is not string folderPath)
        {
            return;
        }

        await ViewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);
    }

    private sealed record DetailsColumnToggle(
        ToggleMenuFlyoutItem Item,
        Func<FileBrowserPaneViewModel, bool> Getter,
        Action<FileBrowserPaneViewModel, bool> Setter);
}

/// <summary>
/// ファイル一覧コンテキストメニュー各項目のクリックハンドラ群。ダイアログ表示・ピッカー操作を
/// 伴うため View に実装を残し、メニュー構築時に <see cref="FileBrowserMenuBuilder"/> へ注入する。
/// </summary>
internal sealed record FileContextMenuHandlers(
    RoutedEventHandler CreateFolder,
    RoutedEventHandler OpenInExplorer,
    RoutedEventHandler OpenFolderInExplorer,
    RoutedEventHandler CopyPath,
    RoutedEventHandler OpenInGoogleMaps,
    RoutedEventHandler Rename,
    RoutedEventHandler Move,
    RoutedEventHandler MoveToParent,
    RoutedEventHandler Copy,
    RoutedEventHandler Delete);
