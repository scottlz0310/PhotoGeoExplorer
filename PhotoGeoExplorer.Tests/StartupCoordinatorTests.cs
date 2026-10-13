using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.State;

namespace PhotoGeoExplorer.Tests;

[Collection("NonParallel")]
public sealed class StartupCoordinatorTests : IDisposable
{
    private static readonly string[] DefaultCommandLineArgs = new[] { "PhotoGeoExplorer.exe" };
    private readonly List<string> _tempDirectories = new();

    [Fact]
    public void ResolveStartupFolderOverrideUsesEnvironmentValueFirst()
    {
        var envPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var args = new[] { "PhotoGeoExplorer.exe", "--folder", @"C:\ignored" };

        var actual = StartupCoordinator.ResolveStartupFolderOverride(args, envPath);

        Assert.Equal(envPath, actual);
    }

    [Fact]
    public void ResolveStartupFolderOverrideParsesOptionValue()
    {
        var expected = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var args = new[] { "PhotoGeoExplorer.exe", "--folder", expected };

        var actual = StartupCoordinator.ResolveStartupFolderOverride(args, null);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryGetOptionValueParsesEqualsValue()
    {
        var expected = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var parsed = StartupCoordinator.TryGetOptionValue($"--folder={expected}", "--folder", out var value);

        Assert.True(parsed);
        Assert.Equal(expected, value);
    }

    [Fact]
    public async Task ApplyStartupAsyncLoadsStartupFolderOverride()
    {
        var tempDir = CreateTempDirectory();
        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(
            new FileBrowserPaneService(),
            workspaceState,
            folderWatcherService: NoOpFolderWatcherService.Shared);
        using var coordinator = new StartupCoordinator(
            viewModel,
            commandLineArgsProvider: () => new[] { "PhotoGeoExplorer.exe", "--folder", tempDir },
            startupFolderOverrideProvider: () => null);

        await coordinator.ApplyStartupAsync().ConfigureAwait(true);

        Assert.Equal(tempDir, viewModel.CurrentFolderPath);
    }

    [Fact]
    public async Task ApplyStartupAsyncSelectsActivatedFile()
    {
        var tempDir = CreateTempDirectory();
        var startupFilePath = Path.Combine(tempDir, "startup.jpg");
        await File.WriteAllTextAsync(startupFilePath, "test").ConfigureAwait(true);

        var workspaceState = new WorkspaceState();
        using var viewModel = new FileBrowserPaneViewModel(
            new FileBrowserPaneService(),
            workspaceState,
            folderWatcherService: NoOpFolderWatcherService.Shared);
        using var coordinator = new StartupCoordinator(
            viewModel,
            commandLineArgsProvider: () => DefaultCommandLineArgs,
            startupFolderOverrideProvider: () => null);
        coordinator.SetStartupFilePath(startupFilePath);

        await coordinator.ApplyStartupAsync().ConfigureAwait(true);

        Assert.Equal(tempDir, viewModel.CurrentFolderPath);
        Assert.NotNull(viewModel.SelectedItem);
        Assert.Equal(startupFilePath, viewModel.SelectedItem!.FilePath);
    }

    [Fact]
    public async Task NavigateToFileAsyncWaitsForInitializationCompleted()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, "redirected.jpg");
        await File.WriteAllTextAsync(filePath, "test").ConfigureAwait(true);

        using var viewModel = CreateViewModel();
        using var coordinator = CreateCoordinator(viewModel);

        var navigation = coordinator.NavigateToFileAsync(filePath);

        Assert.False(navigation.IsCompleted);
        Assert.Null(viewModel.SelectedItem);

        coordinator.NotifyInitializationCompleted();
        await navigation.ConfigureAwait(true);

        Assert.Equal(tempDir, viewModel.CurrentFolderPath);
        Assert.Equal(filePath, viewModel.SelectedItem!.FilePath);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task NavigateToFileAsyncSelectsLastRequestedFileWhenRequestsOverlap(int requestCount)
    {
        var tempDir = CreateTempDirectory();
        var filePaths = new List<string>();
        for (var i = 0; i < requestCount; i++)
        {
            var filePath = Path.Combine(tempDir, $"redirected_{i}.jpg");
            await File.WriteAllTextAsync(filePath, "test").ConfigureAwait(true);
            filePaths.Add(filePath);
        }

        using var viewModel = CreateViewModel();
        using var coordinator = CreateCoordinator(viewModel);
        coordinator.NotifyInitializationCompleted();

        // 完了を待たずに連続して要求し、リダイレクトが短時間に重なる状況を再現する。
        var navigations = filePaths.Select(coordinator.NavigateToFileAsync).ToList();
        await Task.WhenAll(navigations).ConfigureAwait(true);

        Assert.Equal(filePaths[^1], viewModel.SelectedItem!.FilePath);
    }

    [Fact]
    public async Task NavigateToFileAsyncAppliesRedirectedFileAfterInitialStartupFile()
    {
        var tempDir = CreateTempDirectory();
        var startupFilePath = Path.Combine(tempDir, "startup.jpg");
        var redirectedFilePath = Path.Combine(tempDir, "redirected.jpg");
        await File.WriteAllTextAsync(startupFilePath, "test").ConfigureAwait(true);
        await File.WriteAllTextAsync(redirectedFilePath, "test").ConfigureAwait(true);

        using var viewModel = CreateViewModel();
        using var coordinator = CreateCoordinator(viewModel);
        coordinator.SetStartupFilePath(startupFilePath);
        var redirected = coordinator.NavigateToFileAsync(redirectedFilePath);

        await coordinator.ApplyStartupAsync().ConfigureAwait(true);
        coordinator.NotifyInitializationCompleted();
        await redirected.ConfigureAwait(true);

        Assert.Equal(redirectedFilePath, viewModel.SelectedItem!.FilePath);
    }

    private static FileBrowserPaneViewModel CreateViewModel()
    {
        return new FileBrowserPaneViewModel(
            new FileBrowserPaneService(),
            new WorkspaceState(),
            folderWatcherService: NoOpFolderWatcherService.Shared);
    }

    private static StartupCoordinator CreateCoordinator(FileBrowserPaneViewModel viewModel)
    {
        return new StartupCoordinator(
            viewModel,
            commandLineArgsProvider: () => DefaultCommandLineArgs,
            startupFolderOverrideProvider: () => null);
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_StartupCoordinatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
