using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// FileBrowserPaneViewModel のテスト
/// </summary>
public class FileBrowserPaneViewModelTests
{
    [Fact]
    public void ConstructorThrowsWhenServiceIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FileBrowserPaneViewModel(null!, new WorkspaceState()));
    }

    [Fact]
    public void ConstructorThrowsWhenWorkspaceStateIsNull()
    {
        // Arrange
        var service = new FileBrowserPaneService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FileBrowserPaneViewModel(service, null!));
    }

    [Fact]
    public void ConstructorInitializesProperties()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Assert
        Assert.NotNull(viewModel.Items);
        Assert.NotNull(viewModel.BreadcrumbItems);
        Assert.NotNull(viewModel.NavigateBackCommand);
        Assert.NotNull(viewModel.NavigateForwardCommand);
        Assert.NotNull(viewModel.NavigateUpCommand);
        Assert.NotNull(viewModel.NavigateHomeCommand);
        Assert.NotNull(viewModel.RefreshCommand);
        Assert.NotNull(viewModel.ToggleSortCommand);
        Assert.NotNull(viewModel.ResetFiltersCommand);
        Assert.NotNull(viewModel.ToggleImagesOnlyCommand);
        Assert.NotNull(viewModel.EditExifCommand);
        Assert.NotNull(viewModel.SetViewModeCommand);
        Assert.NotNull(viewModel.OpenFolderCommand);
        Assert.NotNull(viewModel.CreateFolderCommand);
        Assert.NotNull(viewModel.RenameSelectionCommand);
        Assert.NotNull(viewModel.MoveSelectionCommand);
        Assert.NotNull(viewModel.MoveSelectionToParentCommand);
        Assert.NotNull(viewModel.DeleteSelectionCommand);
    }

    [Fact]
    public void ShowImagesOnlyDefaultsToTrue()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Assert
        Assert.True(viewModel.ShowImagesOnly);
    }

    [Fact]
    public void FileViewModeDefaultsToDetails()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Assert
        Assert.Equal(FileViewMode.Details, viewModel.FileViewMode);
    }

    [Fact]
    public void DetailsColumnsDefaultStateMatchesExpected()
    {
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();

        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        Assert.True(viewModel.ShowDetailsModifiedColumn);
        Assert.True(viewModel.ShowDetailsResolutionColumn);
        Assert.True(viewModel.ShowDetailsSizeColumn);
        Assert.False(viewModel.ShowDetailsTakenAtColumn);
        Assert.False(viewModel.ShowDetailsLocationColumn);
        Assert.Equal(Visibility.Visible, viewModel.DetailsModifiedColumnVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.DetailsTakenAtColumnVisibility);
    }

    [Fact]
    public void SettingDetailsColumnVisibilityPropertiesUpdatesVisibility()
    {
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();

        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        viewModel.ShowDetailsModifiedColumn = false;
        viewModel.ShowDetailsTakenAtColumn = true;

        Assert.Equal(Visibility.Collapsed, viewModel.DetailsModifiedColumnVisibility);
        Assert.Equal(Visibility.Visible, viewModel.DetailsTakenAtColumnVisibility);
    }

    [Fact]
    public void SortColumnDefaultsToName()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Assert
        Assert.Equal(FileSortColumn.Name, viewModel.SortColumn);
    }

    [Fact]
    public void SortDirectionDefaultsToAscending()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Assert
        Assert.Equal(SortDirection.Ascending, viewModel.SortDirection);
    }

    [Fact]
    public void ToggleSortTogglesSortDirection()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Act
        viewModel.ToggleSort(FileSortColumn.Name);

        // Assert
        Assert.Equal(SortDirection.Descending, viewModel.SortDirection);
    }

    [Fact]
    public void ToggleSortChangesSortColumn()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Act
        viewModel.ToggleSort(FileSortColumn.Size);

        // Assert
        Assert.Equal(FileSortColumn.Size, viewModel.SortColumn);
        Assert.Equal(SortDirection.Ascending, viewModel.SortDirection);
    }

    [Fact]
    public void ToggleSortSupportsTakenAtAndLocationColumns()
    {
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        viewModel.ToggleSort(FileSortColumn.TakenAt);
        Assert.Equal(FileSortColumn.TakenAt, viewModel.SortColumn);
        Assert.Equal("▲", viewModel.TakenAtSortIndicator);

        viewModel.ToggleSort(FileSortColumn.Location);
        Assert.Equal(FileSortColumn.Location, viewModel.SortColumn);
        Assert.Equal("▲", viewModel.LocationSortIndicator);
    }

    [Fact]
    public void ResetFiltersClearsSearchText()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState)
        {
            SearchText = "test"
        };

        // Act
        viewModel.ResetFilters();

        // Assert
        Assert.Null(viewModel.SearchText);
    }

    [Fact]
    public void ResetFiltersSetsShowImagesOnlyToTrue()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState)
        {
            ShowImagesOnly = false
        };

        // Act
        viewModel.ResetFilters();

        // Assert
        Assert.True(viewModel.ShowImagesOnly);
    }

    [Fact]
    public void SettingSearchTextUpdatesHasActiveFilters()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Act
        viewModel.SearchText = "test";

        // Assert
        Assert.True(viewModel.HasActiveFilters);
    }

    [Fact]
    public void SettingShowImagesOnlyToFalseUpdatesHasActiveFilters()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Act
        viewModel.ShowImagesOnly = false;

        // Assert
        Assert.True(viewModel.HasActiveFilters);
    }

    [Fact]
    public void SelectedItemUpdatesWorkspaceState()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var photoListItem = CreatePhotoListItem("test.jpg");

        // Act
        viewModel.SelectedItem = photoListItem;

        // Assert
        Assert.Equal(1, workspaceState.SelectedPhotoCount);
        Assert.NotNull(workspaceState.SelectedPhotos);
        Assert.Single(workspaceState.SelectedPhotos);
    }

    [Fact]
    public void SelectedFolderDoesNotUpdateWorkspaceState()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var folderItem = CreateFolderListItem("TestFolder");

        // Act
        viewModel.SelectedItem = folderItem;

        // Assert
        Assert.Equal(0, workspaceState.SelectedPhotoCount);
    }

    [Fact]
    public void WorkspacePhotoFocusRequestSelectsMatchingItem()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var first = CreatePhotoListItem("first.jpg");
        var second = CreatePhotoListItem("second.jpg");
        viewModel.Items.Add(first);
        viewModel.Items.Add(second);

        // Act
        workspaceState.RequestPhotoFocus(second.FilePath);

        // Assert
        Assert.Equal(second, viewModel.SelectedItem);
        Assert.Single(viewModel.SelectedItems);
        Assert.Equal(second, viewModel.SelectedItems[0]);
    }

    [Fact]
    public async Task LoadFolderAsyncUpdatesCurrentFolderPath()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        try
        {
            // Act
            await viewModel.LoadFolderAsync(tempDir).ConfigureAwait(true);

            // Assert
            Assert.Equal(tempDir, viewModel.CurrentFolderPath);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoadFolderAsyncUpdatesWorkspaceState()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        try
        {
            // Act
            await viewModel.LoadFolderAsync(tempDir).ConfigureAwait(true);

            // Assert
            Assert.Equal(tempDir, workspaceState.CurrentFolderPath);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void CanCreateFolderDefaultsToFalse()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Assert
        Assert.False(viewModel.CanCreateFolder);
    }

    [Fact]
    public async Task CanCreateFolderBecomesTrueAfterLoadFolderAsync()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        try
        {
            // Act
            await viewModel.LoadFolderAsync(tempDir).ConfigureAwait(true);

            // Assert
            Assert.True(viewModel.CanCreateFolder);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void CanRenameSelectionTrueWhenSingleItemSelected()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var item = CreatePhotoListItem("test.jpg");

        // Act
        viewModel.UpdateSelection(new[] { item });

        // Assert
        Assert.True(viewModel.CanRenameSelection);
    }

    [Fact]
    public void CanRenameSelectionFalseWhenNoSelection()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Act
        viewModel.UpdateSelection(Array.Empty<PhotoListItem>());

        // Assert
        Assert.False(viewModel.CanRenameSelection);
    }

    [Fact]
    public void CanRenameSelectionFalseWhenMultipleSelected()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var first = CreatePhotoListItem("test1.jpg");
        var second = CreatePhotoListItem("test2.jpg");

        // Act
        viewModel.UpdateSelection(new[] { first, second });

        // Assert
        Assert.False(viewModel.CanRenameSelection);
    }

    [Fact]
    public void CanModifySelectionTrueWhenSelected()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var item = CreatePhotoListItem("test.jpg");

        // Act
        viewModel.UpdateSelection(new[] { item });

        // Assert
        Assert.True(viewModel.CanModifySelection);
    }

    [Fact]
    public async Task CanMoveToParentSelectionTrueWithSelectionAndParentFolder()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var item = CreatePhotoListItem("test.jpg");

        try
        {
            await viewModel.LoadFolderAsync(tempDir).ConfigureAwait(true);

            // Act
            viewModel.UpdateSelection(new[] { item });

            // Assert
            Assert.True(viewModel.CanMoveToParentSelection);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void CanMoveToParentSelectionFalseWithoutSelection()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Act
        viewModel.UpdateSelection(Array.Empty<PhotoListItem>());

        // Assert
        Assert.False(viewModel.CanMoveToParentSelection);
    }

    [Fact]
    public void DisposeCanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Act & Assert (Should not throw)
        viewModel.Dispose();
        viewModel.Dispose();
    }

    [Fact]
    public void ToggleImagesOnlyCommandTogglesShowImagesOnly()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var initialValue = viewModel.ShowImagesOnly;
        var expectedTextBeforeToggle = initialValue
            ? LocalizationService.GetString("MenuViewAllFiles.Text")
            : LocalizationService.GetString("MenuViewImagesOnly.Text");

        // Act
        var menuTextBeforeToggle = viewModel.ToggleImagesOnlyMenuText;
        viewModel.ToggleImagesOnlyCommand.Execute(null);

        // Assert
        Assert.Equal(expectedTextBeforeToggle, menuTextBeforeToggle);
        Assert.NotEqual(initialValue, viewModel.ShowImagesOnly);
        var expectedTextAfterToggle = viewModel.ShowImagesOnly
            ? LocalizationService.GetString("MenuViewAllFiles.Text")
            : LocalizationService.GetString("MenuViewImagesOnly.Text");
        Assert.Equal(expectedTextAfterToggle, viewModel.ToggleImagesOnlyMenuText);
    }

    [Fact]
    public void SetViewModeCommandChangesFileViewMode()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);

        // Act
        viewModel.SetViewModeCommand.Execute("Icon");

        // Assert
        Assert.Equal(FileViewMode.Icon, viewModel.FileViewMode);
    }

    [Fact]
    public void SetViewModeCommandIgnoresInvalidParameter()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var original = viewModel.FileViewMode;

        // Act
        viewModel.SetViewModeCommand.Execute("InvalidMode");

        // Assert
        Assert.Equal(original, viewModel.FileViewMode);
    }

    [Fact]
    public void SetViewModeCommandIgnoresNullParameter()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var original = viewModel.FileViewMode;

        // Act
        viewModel.SetViewModeCommand.Execute(null);

        // Assert
        Assert.Equal(original, viewModel.FileViewMode);
    }

    [Fact]
    public void ResolveItemsByFilePathsReturnsOnlyMatchingPhotoItems()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var photo = CreatePhotoListItem("match.jpg");
        var folder = CreateFolderListItem("folder");
        viewModel.Items.Add(photo);
        viewModel.Items.Add(folder);

        // Act
        var requestedPaths = new[] { photo.FilePath.ToUpperInvariant(), folder.FilePath };
        var result = viewModel.ResolveItemsByFilePaths(requestedPaths);

        // Assert
        Assert.Collection(
            result,
            item => Assert.Equal(photo.FilePath, item.FilePath));
    }

    [Fact]
    public void ResolveItemsByFilePathsReturnsEmptyWhenNoMatch()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        viewModel.Items.Add(CreatePhotoListItem("exists.jpg"));

        // Act
        var requestedPaths = new[] { @"C:\test\missing.jpg" };
        var result = viewModel.ResolveItemsByFilePaths(requestedPaths);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task OpenFolderCommandInvokesConfiguredUiAction()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        var invokedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ConfigureUiActionHandlers(
            () =>
            {
                invokedTcs.TrySetResult(true);
                return Task.CompletedTask;
            },
            null,
            null,
            null,
            null,
            null);

        // Act
        viewModel.OpenFolderCommand.Execute(null);

        // Assert
        var completed = await Task.WhenAny(invokedTcs.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(true);
        Assert.Same(invokedTcs.Task, completed);
    }

    [Fact]
    public async Task CreateFolderCommandCanExecuteReflectsCurrentFolder()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        viewModel.ConfigureUiActionHandlers(
            null,
            () => Task.CompletedTask,
            null,
            null,
            null,
            null);

        try
        {
            // Act & Assert
            Assert.False(viewModel.CreateFolderCommand.CanExecute(null));

            await viewModel.LoadFolderAsync(tempDir).ConfigureAwait(true);

            Assert.True(viewModel.CreateFolderCommand.CanExecute(null));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void RenameSelectionCommandCanExecuteReflectsSelectionCount()
    {
        // Arrange
        var service = new FileBrowserPaneService();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(service, workspaceState);
        viewModel.ConfigureUiActionHandlers(
            null,
            null,
            () => Task.CompletedTask,
            null,
            null,
            null);
        var first = CreatePhotoListItem("first.jpg");
        var second = CreatePhotoListItem("second.jpg");

        // Act & Assert
        Assert.False(viewModel.RenameSelectionCommand.CanExecute(null));

        viewModel.UpdateSelection(new[] { first });
        Assert.True(viewModel.RenameSelectionCommand.CanExecute(null));

        viewModel.UpdateSelection(new[] { first, second });
        Assert.False(viewModel.RenameSelectionCommand.CanExecute(null));
    }

    private static string CreateTempTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-filebrowservm-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void CleanupTempDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // ベストエフォート
            }
            catch (DirectoryNotFoundException)
            {
                // ベストエフォート
            }
            catch (PathTooLongException)
            {
                // ベストエフォート
            }
            catch (IOException)
            {
                // ベストエフォート
            }
        }
    }

    // =========================================================
    // ExecuteCreateFolderAsync
    // =========================================================

    [Fact]
    public async Task ExecuteCreateFolderAsync_NullCurrentFolder_ReturnsNoParent()
    {
        using var vm = CreateViewModelWithFakes(out _, out _);

        var result = await vm.ExecuteCreateFolderAsync("NewFolder");

        Assert.False(result.IsSuccess);
        Assert.Equal(FileOperationError.NoParent, result.Error);
    }

    [Fact]
    public async Task ExecuteCreateFolderAsync_InvalidName_ReturnsInvalidName()
    {
        using var vm = CreateViewModelWithFakes(out _, out _);

        var result = await vm.ExecuteCreateFolderAsync("bad:name");

        Assert.False(result.IsSuccess);
        Assert.Equal(FileOperationError.InvalidName, result.Error);
    }

    [Fact]
    public async Task ExecuteCreateFolderAsync_ServiceReturnsAlreadyExists_ReturnsAlreadyExists()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
            stubOp.CreateFolderResult = FileOperationResult.Failure(FileOperationError.AlreadyExists);
            await vm.LoadFolderAsync(tempDir).ConfigureAwait(false);

            var result = await vm.ExecuteCreateFolderAsync("ExistingFolder");

            Assert.False(result.IsSuccess);
            Assert.Equal(FileOperationError.AlreadyExists, result.Error);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task ExecuteCreateFolderAsync_Success_CallsRefresh()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
            stubOp.CreateFolderResult = FileOperationResult.Success(Path.Combine(tempDir, "NewFolder"));
            await vm.LoadFolderAsync(tempDir).ConfigureAwait(false);
            var before = fakePaneService.LoadFolderCallCount;

            var result = await vm.ExecuteCreateFolderAsync("NewFolder");

            Assert.True(result.IsSuccess);
            Assert.Equal(before + 1, fakePaneService.LoadFolderCallCount);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    // =========================================================
    // ExecuteRenameAsync
    // =========================================================

    [Fact]
    public async Task ExecuteRenameAsync_SameName_ReturnsSuccessWithoutRenameCall()
    {
        using var vm = CreateViewModelWithFakes(out _, out var stubOp);
        var item = CreatePhotoListItem("photo.jpg");

        var result = await vm.ExecuteRenameAsync(item, "photo.jpg");

        Assert.True(result.IsSuccess);
        Assert.False(stubOp.RenameItemWasCalled);
    }

    [Fact]
    public async Task ExecuteRenameAsync_InvalidName_ReturnsInvalidName()
    {
        using var vm = CreateViewModelWithFakes(out _, out _);
        var item = CreatePhotoListItem("photo.jpg");

        var result = await vm.ExecuteRenameAsync(item, "bad:name");

        Assert.False(result.IsSuccess);
        Assert.Equal(FileOperationError.InvalidName, result.Error);
    }

    [Fact]
    public async Task ExecuteRenameAsync_Success_CallsRefresh()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
            stubOp.RenameItemResult = FileOperationResult.Success(Path.Combine(tempDir, "renamed.jpg"));
            await vm.LoadFolderAsync(tempDir).ConfigureAwait(false);
            var before = fakePaneService.LoadFolderCallCount;
            var item = CreatePhotoListItem("original.jpg");

            var result = await vm.ExecuteRenameAsync(item, "renamed.jpg");

            Assert.True(result.IsSuccess);
            Assert.Equal(before + 1, fakePaneService.LoadFolderCallCount);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    // =========================================================
    // ExecuteMoveItemsToFolderAsync
    // =========================================================

    [Fact]
    public async Task ExecuteMoveItemsToFolderAsync_AllFail_DoesNotCallRefresh()
    {
        var failure = new FileOperationFailure("path", "photo.jpg", FileOperationError.AlreadyExists);
        using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
        stubOp.MoveItemsResult = new FileOperationSummary(0, new[] { failure });

        var summary = await vm.ExecuteMoveItemsToFolderAsync(new List<PhotoListItem>(), Path.GetTempPath());

        Assert.Equal(0, summary.SuccessCount);
        Assert.True(summary.HasFailures);
        Assert.Equal(0, fakePaneService.LoadFolderCallCount);
    }

    [Fact]
    public async Task ExecuteMoveItemsToFolderAsync_PartialSuccess_CallsRefresh()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            var failure = new FileOperationFailure("path2", "file2.jpg", FileOperationError.AlreadyExists);
            using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
            stubOp.MoveItemsResult = new FileOperationSummary(1, new[] { failure });
            await vm.LoadFolderAsync(tempDir).ConfigureAwait(false);
            var before = fakePaneService.LoadFolderCallCount;

            var summary = await vm.ExecuteMoveItemsToFolderAsync(new List<PhotoListItem>(), Path.GetTempPath());

            Assert.Equal(1, summary.SuccessCount);
            Assert.Equal(before + 1, fakePaneService.LoadFolderCallCount);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    // =========================================================
    // ExecuteDeleteItemsAsync
    // =========================================================

    [Fact]
    public async Task ExecuteDeleteItemsAsync_AllFail_DoesNotCallRefresh()
    {
        var failure = new FileOperationFailure("path", "locked.jpg", FileOperationError.IoError);
        using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
        stubOp.DeleteItemsResult = new FileOperationSummary(0, new[] { failure });

        var summary = await vm.ExecuteDeleteItemsAsync(new List<PhotoListItem>());

        Assert.Equal(0, summary.SuccessCount);
        Assert.True(summary.HasFailures);
        Assert.Equal(0, fakePaneService.LoadFolderCallCount);
    }

    [Fact]
    public async Task ExecuteDeleteItemsAsync_Success_CallsRefresh()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
            stubOp.DeleteItemsResult = new FileOperationSummary(1, Array.Empty<FileOperationFailure>());
            await vm.LoadFolderAsync(tempDir).ConfigureAwait(false);
            var before = fakePaneService.LoadFolderCallCount;

            var summary = await vm.ExecuteDeleteItemsAsync(new List<PhotoListItem>());

            Assert.Equal(1, summary.SuccessCount);
            Assert.Equal(before + 1, fakePaneService.LoadFolderCallCount);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    // =========================================================
    // ExecuteMoveToParentAsync
    // =========================================================

    [Fact]
    public async Task ExecuteMoveToParentAsync_NoCurrentFolder_ReturnsEmptySummary()
    {
        using var vm = CreateViewModelWithFakes(out _, out _);

        var summary = await vm.ExecuteMoveToParentAsync();

        Assert.Equal(0, summary.SuccessCount);
        Assert.False(summary.HasFailures);
    }

    // =========================================================
    // HandleExternalFileDropAsync
    // =========================================================

    [Fact]
    public async Task HandleExternalFileDropAsync_NullDirectory_DoesNotLoadFolder()
    {
        using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
        stubOp.ParentPath = null;

        await vm.HandleExternalFileDropAsync("some_path_without_parent");

        Assert.Equal(0, fakePaneService.LoadFolderCallCount);
    }

    [Fact]
    public async Task HandleExternalFileDropAsync_ValidDirectory_LoadsFolder()
    {
        var tempDir = CreateTempTestDirectory();
        try
        {
            using var vm = CreateViewModelWithFakes(out var fakePaneService, out var stubOp);
            stubOp.ParentPath = tempDir;

            await vm.HandleExternalFileDropAsync(Path.Combine(tempDir, "photo.jpg"));

            Assert.Equal(1, fakePaneService.LoadFolderCallCount);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    // =========================================================
    // Execute* 系テスト用ヘルパー
    // =========================================================

    private static FileBrowserPaneViewModel CreateViewModelWithFakes(
        out FakeFileBrowserPaneService fakePaneService,
        out StubFileOperationService stubOp)
    {
        fakePaneService = new FakeFileBrowserPaneService();
        stubOp = new StubFileOperationService();
        return new FileBrowserPaneViewModel(fakePaneService, new WorkspaceState(), null, null, stubOp);
    }

    private sealed class FakeFileBrowserPaneService : IFileBrowserPaneService
    {
        public int LoadFolderCallCount { get; private set; }

        public Task<List<PhotoListItem>> LoadFolderAsync(string folderPath, bool showImagesOnly, string? searchText)
        {
            LoadFolderCallCount++;
            return Task.FromResult(new List<PhotoListItem>());
        }

        public ObservableCollection<BreadcrumbSegment> GetBreadcrumbs(string folderPath) => [];
        public List<PhotoListItem> ApplySort(IEnumerable<PhotoListItem> items, FileSortColumn column, SortDirection direction) => [.. items];
        public PhotoListItem? FindItemByFilePath(IEnumerable<PhotoListItem> items, string filePath) => null;
        public IReadOnlyList<PhotoListItem> ResolveItemsByFilePaths(IEnumerable<PhotoListItem> items, IReadOnlyList<string> filePaths) => Array.Empty<PhotoListItem>();
        public string? NavigateBack(string currentPath) => null;
        public string? NavigateForward(string currentPath) => null;
        public void PushToBackStack(string path) { }
        public void PushToForwardStack(string path) { }
        public void ClearForwardStack() { }
        public bool CanNavigateBack => false;
        public bool CanNavigateForward => false;
    }

    private sealed class StubFileOperationService : IFileOperationService
    {
        public FileOperationResult CreateFolderResult { get; set; } = FileOperationResult.Success("result");
        public FileOperationResult RenameItemResult { get; set; } = FileOperationResult.Success("result");
        public FileOperationSummary MoveItemsResult { get; set; } = new(1, Array.Empty<FileOperationFailure>());
        public FileOperationSummary DeleteItemsResult { get; set; } = new(1, Array.Empty<FileOperationFailure>());
        public string? ParentPath { get; set; } = Path.GetTempPath();
        public bool RenameItemWasCalled { get; private set; }

        public bool ContainsInvalidFileNameChars(string name) => name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

        public string NormalizeName(PhotoListItem item, string newName)
        {
            var trimmed = newName.Trim();
            if (item.IsFolder)
            {
                return trimmed;
            }

            var originalExt = Path.GetExtension(item.FileName);
            if (string.IsNullOrEmpty(originalExt))
            {
                return trimmed;
            }

            var newExt = Path.GetExtension(trimmed);
            return string.IsNullOrEmpty(newExt) ? $"{trimmed}{originalExt}" : trimmed;
        }

        public bool IsDescendantPath(string root, string candidate) => false;
        public bool IsSamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        public string? GetParentPath(string path) => ParentPath;
        public bool ItemExistsAtPath(string path) => false;
        public FileOperationResult CreateFolder(string parentFolder, string folderName) => CreateFolderResult;

        public FileOperationResult RenameItem(PhotoListItem item, string normalizedName)
        {
            RenameItemWasCalled = true;
            return RenameItemResult;
        }

        public FileOperationSummary MoveItems(IReadOnlyList<PhotoListItem> items, string destinationFolder) => MoveItemsResult;
        public FileOperationSummary DeleteItems(IReadOnlyList<PhotoListItem> items) => DeleteItemsResult;
    }

    private static PhotoListItem CreatePhotoListItem(string fileName)
    {
        var photoItem = new PhotoItem(
            filePath: $"/test/{fileName}",
            sizeBytes: 1000,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: false,
            thumbnailPath: null,
            pixelWidth: 100,
            pixelHeight: 100);

        return new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: null);
    }

    private static PhotoListItem CreateFolderListItem(string folderName)
    {
        var photoItem = new PhotoItem(
            filePath: $"/test/{folderName}",
            sizeBytes: 0,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: true,
            thumbnailPath: null,
            pixelWidth: null,
            pixelHeight: null);

        return new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: null);
    }
}
