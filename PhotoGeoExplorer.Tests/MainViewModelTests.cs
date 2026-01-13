using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();

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
        var testFolder1 = CreateTempDirectory();
        var testFolder2 = CreateTempDirectory();

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
        
        // パスを正規化して比較
        var normalizedCurrent = NormalizePath(viewModel.CurrentFolderPath!);
        var normalizedExpected = NormalizePath(testFolder1);
        Assert.Equal(normalizedExpected, normalizedCurrent);

        // Navigate forward
        await viewModel.NavigateForwardAsync();
        Assert.True(viewModel.CanNavigateBack, "進んだ後は戻れる");
        Assert.False(viewModel.CanNavigateForward, "進んだ後は進めない");
        
        normalizedCurrent = NormalizePath(viewModel.CurrentFolderPath!);
        normalizedExpected = NormalizePath(testFolder2);
        Assert.Equal(normalizedExpected, normalizedCurrent);
    }

    [Fact]
    public async Task LoadFolderAsync_ClearsForwardHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var viewModel = new MainViewModel(fileSystemService);
        var testFolder1 = CreateTempDirectory();
        var testFolder2 = CreateTempDirectory();
        var testFolder3 = CreateTempDirectory();

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

    [Fact]
    public async Task LoadFolderAsync_SameFolderDoesNotAddToHistory()
    {
        // Arrange
        var fileSystemService = new FileSystemService();
        var viewModel = new MainViewModel(fileSystemService);
        var testFolder = CreateTempDirectory();

        // Act
        await viewModel.LoadFolderAsync(testFolder);
        Assert.False(viewModel.CanNavigateBack, "初回読み込み後は戻れない");

        // 同じフォルダを再度読み込み
        await viewModel.LoadFolderAsync(testFolder);
        
        // Assert
        Assert.False(viewModel.CanNavigateBack, "同じフォルダの再読み込みでは履歴に追加されない");
    }

    private string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);
        return tempPath;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
