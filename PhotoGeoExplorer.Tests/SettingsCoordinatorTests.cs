using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PhotoGeoExplorer.Tests;

[Collection("NonParallel")]
public sealed class SettingsCoordinatorTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    [Fact]
    public async Task LoadAsyncAppliesPersistedSettings()
    {
        using var context = CreateContext();
        var persisted = new AppSettings
        {
            Language = "ja",
            Theme = ThemePreference.Dark,
            MapDefaultZoomLevel = 16,
            MapTileSource = MapTileSourceType.EsriWorldImagery,
            ShowImagesOnly = false,
            FileViewMode = FileViewMode.Icon,
            ShowQuickStartOnStartup = true
        };
        await context.SettingsService.SaveAsync(persisted).ConfigureAwait(true);

        await context.Coordinator.LoadAsync().ConfigureAwait(true);

        Assert.Equal("ja-JP", context.MainViewModel.CurrentLanguage);
        Assert.Equal(ThemePreference.Dark, context.MainViewModel.CurrentTheme);
        Assert.Equal(16, context.MainViewModel.CurrentMapZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, context.MainViewModel.CurrentMapTileSource);
        Assert.False(context.FileBrowserPaneViewModel.ShowImagesOnly);
        Assert.Equal(FileViewMode.Icon, context.FileBrowserPaneViewModel.FileViewMode);
        Assert.Equal(16, context.MapPaneViewModel.MapDefaultZoomLevel);
        Assert.True(context.Coordinator.ShowQuickStartOnStartup);
    }

    [Fact]
    public async Task SaveAsyncPersistsCurrentSnapshot()
    {
        using var context = CreateContext();

        await context.Coordinator.ChangeLanguageAsync("en", showRestartPrompt: false).ConfigureAwait(true);
        context.Coordinator.ChangeTheme(ThemePreference.Light);
        context.Coordinator.ChangeMapZoomLevel(18);
        context.Coordinator.ChangeMapTileSource(MapTileSourceType.EsriWorldImagery);
        context.Coordinator.ShowQuickStartOnStartup = true;
        context.FileBrowserPaneViewModel.ShowImagesOnly = false;
        context.FileBrowserPaneViewModel.FileViewMode = FileViewMode.List;

        await context.Coordinator.SaveAsync().ConfigureAwait(true);
        var saved = await context.SettingsService.LoadAsync().ConfigureAwait(true);

        Assert.Equal("en-US", saved.Language);
        Assert.Equal(ThemePreference.Light, saved.Theme);
        Assert.Equal(18, saved.MapDefaultZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, saved.MapTileSource);
        Assert.True(saved.ShowQuickStartOnStartup);
        Assert.False(saved.ShowImagesOnly);
        Assert.Equal(FileViewMode.List, saved.FileViewMode);
    }

    [Fact]
    public async Task ChangeThemeDebouncesAndPersistsLatestValue()
    {
        using var context = CreateContext();

        context.Coordinator.ChangeTheme(ThemePreference.Light);
        await Task.Delay(100).ConfigureAwait(true);
        context.Coordinator.ChangeTheme(ThemePreference.Dark);

        await Task.Delay(180).ConfigureAwait(true);
        Assert.False(context.SettingsService.SettingsFileExists());

        await Task.Delay(260).ConfigureAwait(true);
        var saved = await context.SettingsService.LoadAsync().ConfigureAwait(true);
        Assert.Equal(ThemePreference.Dark, saved.Theme);
    }

    [Fact]
    public async Task SaveAsyncCancelsPendingDebouncedSave()
    {
        using var context = CreateContext();

        context.Coordinator.ChangeTheme(ThemePreference.Light);
        await context.Coordinator.SaveAsync().ConfigureAwait(true);

        const string sentinel = "manual-marker";
        await File.WriteAllTextAsync(context.SettingsPath, sentinel).ConfigureAwait(true);

        await Task.Delay(500).ConfigureAwait(true);
        var persisted = await File.ReadAllTextAsync(context.SettingsPath).ConfigureAwait(true);
        Assert.Equal(sentinel, persisted);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("system", null)]
    [InlineData("ja", "ja-JP")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("en", "en-US")]
    [InlineData("en-us", "en-US")]
    [InlineData("fr-FR", "fr-FR")]
    public void NormalizeLanguageSettingReturnsExpectedValue(string? input, string? expected)
    {
        var actual = SettingsCoordinator.NormalizeLanguageSetting(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(14, 14)]
    [InlineData(18, 18)]
    [InlineData(7, 14)]
    [InlineData(20, 14)]
    public void NormalizeMapZoomLevelReturnsExpectedValue(int input, int expected)
    {
        var actual = SettingsCoordinator.NormalizeMapZoomLevel(input);
        Assert.Equal(expected, actual);
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private TestContext CreateContext()
    {
        var root = CreateTempDirectory();
        var settingsPath = Path.Combine(root, "settings.json");
        var settingsService = new SettingsService(settingsPath);
        var workspaceState = new WorkspaceState();
        var shellViewModel = new MainViewModel(new FileSystemService(), workspaceState);
        var fileBrowserPaneViewModel = new FileBrowserPaneViewModel(new FileBrowserPaneService(), workspaceState);
        var mapPaneViewModel = new MapPaneViewModel(new MapPaneService(), workspaceState);
        var dialogService = new FakeDialogService();
        var coordinator = new SettingsCoordinator(
            settingsService,
            dialogService,
            _ => { },
            fileBrowserPaneViewModel,
            mapPaneViewModel,
            shellViewModel,
            () => null);

        shellViewModel.ConfigureSettingsCoordinator(coordinator);

        return new TestContext(
            coordinator,
            settingsPath,
            settingsService,
            shellViewModel,
            fileBrowserPaneViewModel,
            mapPaneViewModel);
    }

    private string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_SettingsCoordinatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);
        return tempPath;
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            SettingsCoordinator coordinator,
            string settingsPath,
            SettingsService settingsService,
            MainViewModel mainViewModel,
            FileBrowserPaneViewModel fileBrowserPaneViewModel,
            MapPaneViewModel mapPaneViewModel)
        {
            Coordinator = coordinator;
            SettingsPath = settingsPath;
            SettingsService = settingsService;
            MainViewModel = mainViewModel;
            FileBrowserPaneViewModel = fileBrowserPaneViewModel;
            MapPaneViewModel = mapPaneViewModel;
        }

        public SettingsCoordinator Coordinator { get; }

        public string SettingsPath { get; }

        public SettingsService SettingsService { get; }

        public MainViewModel MainViewModel { get; }

        public FileBrowserPaneViewModel FileBrowserPaneViewModel { get; }

        public MapPaneViewModel MapPaneViewModel { get; }

        public void Dispose()
        {
            Coordinator.Dispose();
            MapPaneViewModel.Cleanup();
            FileBrowserPaneViewModel.Dispose();
            MainViewModel.Dispose();
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<ContentDialogResult?> ShowContentDialogAsync(ContentDialog dialog, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ContentDialogResult?>(ContentDialogResult.None);
        }

        public Task ShowMessageDialogAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<StorageFile?> ShowFilePickerAsync(
            PickerLocationId startLocation,
            IReadOnlyList<string>? fileTypeFilter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StorageFile?>(null);
        }

        public Task<StorageFile?> ShowSaveFilePickerAsync(
            PickerLocationId startLocation,
            string suggestedFileName,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? fileTypeChoices = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StorageFile?>(null);
        }
    }
}
