using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
