using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void Constructor_InitializesNavigationProperties()
    {
        // Arrange & Act
        var fileSystemService = new FileSystemService();
        var viewModel = new MainViewModel(fileSystemService);

        // Assert
        Assert.False(viewModel.CanNavigateBack);
        Assert.False(viewModel.CanNavigateForward);
    }

    [Fact]
    public async Task NavigateBackAsync_WithoutHistory_DoesNothing()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var viewModel = new MainViewModel(fileSystemService);

        // Act
        await viewModel.NavigateBackAsync();

        // Assert
        Assert.False(viewModel.CanNavigateBack);
        Assert.False(viewModel.CanNavigateForward);
    }

    [Fact]
    public async Task NavigateForwardAsync_WithoutHistory_DoesNothing()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var viewModel = new MainViewModel(fileSystemService);

        // Act
        await viewModel.NavigateForwardAsync();

        // Assert
        Assert.False(viewModel.CanNavigateBack);
        Assert.False(viewModel.CanNavigateForward);
    }

    [Fact]
    public async Task LoadFolderAsync_UpdatesNavigationHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var viewModel = new MainViewModel(fileSystemService);
        var testFolder1 = Path.GetTempPath();
        var testFolder2 = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (!Directory.Exists(testFolder1) || !Directory.Exists(testFolder2))
        {
            // Skip if test folders don't exist
            return;
        }

        // Act
        await viewModel.LoadFolderAsync(testFolder1);
        Assert.False(viewModel.CanNavigateBack, "初回読み込み後は戻れない");
        Assert.False(viewModel.CanNavigateForward, "初回読み込み後は進めない");

        await viewModel.LoadFolderAsync(testFolder2);
        Assert.True(viewModel.CanNavigateBack, "2回目の読み込み後は戻れる");
        Assert.False(viewModel.CanNavigateForward, "2回目の読み込み後は進めない");

        // Navigate back
        await viewModel.NavigateBackAsync();
        Assert.False(viewModel.CanNavigateBack, "戻った後は戻れない");
        Assert.True(viewModel.CanNavigateForward, "戻った後は進める");
        Assert.Equal(testFolder1, viewModel.CurrentFolderPath);

        // Navigate forward
        await viewModel.NavigateForwardAsync();
        Assert.True(viewModel.CanNavigateBack, "進んだ後は戻れる");
        Assert.False(viewModel.CanNavigateForward, "進んだ後は進めない");
        Assert.Equal(testFolder2, viewModel.CurrentFolderPath);
    }

    [Fact]
    public async Task LoadFolderAsync_ClearsForwardHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var viewModel = new MainViewModel(fileSystemService);
        var testFolder1 = Path.GetTempPath();
        var testFolder2 = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var testFolder3 = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        if (!Directory.Exists(testFolder1) || !Directory.Exists(testFolder2) || !Directory.Exists(testFolder3))
        {
            // Skip if test folders don't exist
            return;
        }

        // Act
        await viewModel.LoadFolderAsync(testFolder1);
        await viewModel.LoadFolderAsync(testFolder2);
        await viewModel.NavigateBackAsync();
        
        Assert.True(viewModel.CanNavigateForward, "戻った後は進める");

        // 新しいフォルダに移動すると進む履歴がクリアされる
        await viewModel.LoadFolderAsync(testFolder3);
        
        // Assert
        Assert.True(viewModel.CanNavigateBack, "新しいフォルダに移動後は戻れる");
        Assert.False(viewModel.CanNavigateForward, "新しいフォルダに移動後は進む履歴がクリアされる");
    }
}
