using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// MapPaneViewModel のテスト
/// </summary>
public class MapPaneViewModelTests
{
    [Fact]
    public void ConstructorThrowsWhenServiceIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MapPaneViewModel(null!));
    }

    [Fact]
    public void ConstructorSetsTitle()
    {
        // Arrange & Act
        var viewModel = new MapPaneViewModel();

        // Assert
        Assert.Equal("Map", viewModel.Title);
    }

    [Fact]
    public void InitialStateIsCorrect()
    {
        // Arrange & Act
        var viewModel = new MapPaneViewModel();

        // Assert
        Assert.False(viewModel.IsMapInitialized);
        Assert.Null(viewModel.Map);
        Assert.Equal(MapTileSourceType.OpenStreetMap, viewModel.CurrentTileSource);
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);
    }

    [Fact]
    public async Task InitializeAsyncCompletesWithoutError()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act & Assert (テスト環境では UI スレッドがないため、初期化はスキップされる)
        await viewModel.InitializeAsync().ConfigureAwait(true);
        Assert.True(viewModel.IsMapInitialized);
    }

    [Fact]
    public void MapDefaultZoomLevelNormalizesInvalidValues()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act
        viewModel.MapDefaultZoomLevel = 15; // 無効な値（8, 10, 12, 14, 16, 18 のみ有効）

        // Assert
        Assert.Equal(14, viewModel.MapDefaultZoomLevel); // デフォルト値に戻る
    }

    [Fact]
    public void MapDefaultZoomLevelAcceptsValidValues()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act & Assert
        viewModel.MapDefaultZoomLevel = 8;
        Assert.Equal(8, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 10;
        Assert.Equal(10, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 12;
        Assert.Equal(12, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 14;
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 16;
        Assert.Equal(16, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevel = 18;
        Assert.Equal(18, viewModel.MapDefaultZoomLevel);
    }

    [Fact]
    public async Task UpdateMarkersFromSelectionAsyncThrowsWhenSelectedItemsIsNull()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await viewModel.UpdateMarkersFromSelectionAsync(null!).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public void CleanupDisposesResources()
    {
        // Arrange
        var viewModel = new MapPaneViewModel();

        // Act
        viewModel.Cleanup();

        // Assert
        Assert.Null(viewModel.Map);
    }
}
