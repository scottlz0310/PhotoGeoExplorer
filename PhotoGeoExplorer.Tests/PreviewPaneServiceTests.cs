using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Panes.Preview;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// PreviewPaneService のテスト
/// </summary>
public class PreviewPaneServiceTests
{
    [Fact]
    public async Task LoadImageAsyncNullFilePathReturnsNull()
    {
        // Arrange
        var service = new PreviewPaneService();

        // Act
        var result = await service.LoadImageAsync(null!).ConfigureAwait(true);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadImageAsyncEmptyFilePathReturnsNull()
    {
        // Arrange
        var service = new PreviewPaneService();

        // Act
        var result = await service.LoadImageAsync(string.Empty).ConfigureAwait(true);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadImageAsyncNonExistentFileReturnsNull()
    {
        // Arrange
        var service = new PreviewPaneService();
        var nonExistentPath = "/tmp/nonexistent_image.jpg";

        // Act
        var result = await service.LoadImageAsync(nonExistentPath).ConfigureAwait(true);

        // Assert
        Assert.Null(result);
    }
}

