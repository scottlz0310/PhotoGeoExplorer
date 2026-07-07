using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// FileBrowserPaneService のテスト
/// </summary>
public class FileBrowserPaneServiceTests
{
    [Fact]
    public void ConstructorThrowsWhenFileSystemServiceIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FileBrowserPaneService(null!));
    }

    [Fact]
    public async Task LoadFolderAsyncReturnsItems()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        CreateTestFile(tempDir, "test1.jpg");
        CreateTestFile(tempDir, "test2.png");
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        try
        {
            // Act
            var items = await service.LoadFolderAsync(tempDir, showImagesOnly: true, searchText: null).ConfigureAwait(true);

            // Assert
            Assert.NotNull(items);
            Assert.Equal(2, items.Count);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoadFolderAsyncLeavesTakenAtAndLocationColumnsUnresolvedInitially()
    {
        var tempDir = CreateTempTestDirectory();
        var jpgPath = Path.Combine(tempDir, "test.jpg");
        using (var image = new Image<Rgba32>(100, 100))
        {
            await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
        }

        var takenAt = new DateTimeOffset(2024, 1, 15, 12, 30, 0, TimeSpan.Zero);
        await ExifWriter.UpdateMetadataAsync(
            jpgPath,
            takenAt,
            35.6762,
            139.6503,
            updateFileModifiedDate: false,
            CancellationToken.None).ConfigureAwait(true);

        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        try
        {
            var items = await service.LoadFolderAsync(tempDir, showImagesOnly: true, searchText: null).ConfigureAwait(true);

            var item = Assert.Single(items);
            Assert.True(string.IsNullOrWhiteSpace(item.TakenAtText));
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, item.LocationStatusVisibility);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoadFolderAsyncLeavesColumnsUnresolvedWhenExifIsMissing()
    {
        var tempDir = CreateTempTestDirectory();
        var jpgPath = Path.Combine(tempDir, "test.jpg");
        using (var image = new Image<Rgba32>(100, 100))
        {
            await image.SaveAsync(jpgPath, new JpegEncoder()).ConfigureAwait(true);
        }

        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        try
        {
            var items = await service.LoadFolderAsync(tempDir, showImagesOnly: true, searchText: null).ConfigureAwait(true);

            var item = Assert.Single(items);
            Assert.True(string.IsNullOrWhiteSpace(item.TakenAtText));
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, item.LocationStatusVisibility);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoadFolderAsyncThrowsWhenFolderPathIsNull()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.LoadFolderAsync(null!, showImagesOnly: true, searchText: null).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public void GetBreadcrumbsReturnsSegments()
    {
        // Arrange
        var tempDir = CreateTempTestDirectory();
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        try
        {
            // Act
            var breadcrumbs = service.GetBreadcrumbs(tempDir);

            // Assert
            Assert.NotNull(breadcrumbs);
            Assert.True(breadcrumbs.Count > 0);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void GetBreadcrumbsThrowsWhenFolderPathIsNull()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.GetBreadcrumbs(null!));
    }

    [Fact]
    public void ApplySortSortsByName()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);
        var items = new List<ViewModels.PhotoListItem>
        {
            CreatePhotoListItem("file3.jpg"),
            CreatePhotoListItem("file1.jpg"),
            CreatePhotoListItem("file2.jpg")
        };

        // Act
        var sorted = service.ApplySort(items, FileSortColumn.Name, SortDirection.Ascending);

        // Assert
        Assert.Equal("file1.jpg", sorted[0].FileName);
        Assert.Equal("file2.jpg", sorted[1].FileName);
        Assert.Equal("file3.jpg", sorted[2].FileName);
    }

    [Fact]
    public void ApplySortSortsByTakenAt()
    {
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);
        var older = CreatePhotoListItem("older.jpg", new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = CreatePhotoListItem("newer.jpg", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var sorted = service.ApplySort(
            new List<PhotoListItem> { newer, older },
            FileSortColumn.TakenAt,
            SortDirection.Ascending);

        Assert.Equal("older.jpg", sorted[0].FileName);
        Assert.Equal("newer.jpg", sorted[1].FileName);
    }

    [Fact]
    public void ApplySortSortsByLocation()
    {
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);
        var withLocation = CreatePhotoListItem("with-location.jpg", null, true);
        var withoutLocation = CreatePhotoListItem("without-location.jpg", null, false);

        var sorted = service.ApplySort(
            new List<PhotoListItem> { withoutLocation, withLocation },
            FileSortColumn.Location,
            SortDirection.Descending);

        Assert.Equal("with-location.jpg", sorted[0].FileName);
        Assert.Equal("without-location.jpg", sorted[1].FileName);
    }

    [Fact]
    public void ApplySortThrowsWhenItemsIsNull()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.ApplySort(null!, FileSortColumn.Name, SortDirection.Ascending));
    }

    [Fact]
    public void FindItemByFilePathReturnsMatchingItem()
    {
        // Arrange
        var service = new FileBrowserPaneService(new FileSystemService());
        var target = CreatePhotoListItemWithPath(@"C:\Photos\B.jpg");
        var items = new List<PhotoListItem>
        {
            CreatePhotoListItemWithPath(@"C:\Photos\A.jpg"),
            target
        };

        // Act
        var result = service.FindItemByFilePath(items, @"C:\Photos\B.jpg");

        // Assert
        Assert.Same(target, result);
    }

    [Fact]
    public void ResolveItemsByFilePathsReturnsItemsInRequestedOrder()
    {
        // Arrange
        var service = new FileBrowserPaneService(new FileSystemService());
        var first = CreatePhotoListItemWithPath(@"C:\Photos\A.jpg");
        var second = CreatePhotoListItemWithPath(@"C:\Photos\B.jpg");
        var items = new List<PhotoListItem> { first, second };
        var requestedPaths = new List<string> { @"C:\Photos\B.jpg", @"C:\Photos\A.jpg", @"C:\Photos\Unknown.jpg" };

        // Act
        var resolved = service.ResolveItemsByFilePaths(
            items,
            requestedPaths);

        // Assert
        Assert.Equal(2, resolved.Count);
        Assert.Same(second, resolved[0]);
        Assert.Same(first, resolved[1]);
    }

    [Fact]
    public void NavigateBackReturnsNullWhenNoHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        // Act
        var result = service.NavigateBack("/current/path");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void NavigateBackReturnsPathWhenHistoryExists()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);
        service.PushToBackStack("/previous/path");

        // Act
        var result = service.NavigateBack("/current/path");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("previous", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigateForwardReturnsNullWhenNoHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        // Act
        var result = service.NavigateForward("/current/path");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void PushToBackStackThrowsWhenPathIsNull()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.PushToBackStack(null!));
    }

    [Fact]
    public void PushToForwardStackThrowsWhenPathIsNull()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.PushToForwardStack(null!));
    }

    [Fact]
    public void ClearForwardStackRemovesHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);
        service.PushToForwardStack("/forward/path");

        // Act
        service.ClearForwardStack();

        // Assert
        Assert.False(service.CanNavigateForward);
    }

    [Fact]
    public void CanNavigateBackReturnsTrueWhenHistoryExists()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);
        service.PushToBackStack("/previous/path");

        // Act & Assert
        Assert.True(service.CanNavigateBack);
    }

    [Fact]
    public void CanNavigateForwardReturnsTrueWhenHistoryExists()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var service = new FileBrowserPaneService(fileSystemService);
        service.PushToForwardStack("/forward/path");

        // Act & Assert
        Assert.True(service.CanNavigateForward);
    }

    private static string CreateTempTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-filebrowser-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void CreateTestFile(string directory, string fileName)
    {
        var filePath = Path.Combine(directory, fileName);
        File.WriteAllText(filePath, "test content");
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

    private static ViewModels.PhotoListItem CreatePhotoListItem(
        string fileName,
        DateTimeOffset? takenAt = null,
        bool? hasLocation = null)
    {
        var photoItem = new PhotoItem(
            filePath: $"/test/{fileName}",
            sizeBytes: 1000,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: false,
            thumbnailPath: null,
            pixelWidth: 100,
            pixelHeight: 100,
            takenAt: takenAt,
            hasLocation: hasLocation);

        return new ViewModels.PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: null);
    }

    private static ViewModels.PhotoListItem CreatePhotoListItemWithPath(string filePath)
    {
        var photoItem = new PhotoItem(
            filePath: filePath,
            sizeBytes: 1000,
            modifiedAt: DateTimeOffset.UtcNow,
            isFolder: false,
            thumbnailPath: null,
            pixelWidth: 100,
            pixelHeight: 100);

        return new ViewModels.PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: null);
    }
}
