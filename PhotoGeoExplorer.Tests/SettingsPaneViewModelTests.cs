using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class SettingsPaneViewModelTests : IDisposable
{
    private readonly WorkspaceState _workspaceState = new();
    private readonly MainViewModel _mainViewModel;
    private readonly FileBrowserPaneViewModel _fileBrowserPaneViewModel;

    public SettingsPaneViewModelTests()
    {
        _mainViewModel = new MainViewModel(new FileSystemService(), _workspaceState);
        _fileBrowserPaneViewModel = new FileBrowserPaneViewModel(new FileBrowserPaneService(), _workspaceState);
    }

    [Fact]
    public async Task InitializeAsyncReflectsCurrentRuntimeState()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel)
        {
            ShowQuickStartOnStartup = true
        };

        _mainViewModel.ApplySettingsState(
            language: "ja-JP",
            theme: ThemePreference.Dark,
            mapZoomLevel: 16,
            mapTileSource: MapTileSourceType.EsriWorldImagery);
        _fileBrowserPaneViewModel.ShowImagesOnly = false;

        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal("ja-JP", viewModel.Language);
        Assert.Equal(ThemePreference.Dark, viewModel.Theme);
        Assert.Equal(16, viewModel.MapDefaultZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, viewModel.MapTileSource);
        Assert.False(viewModel.ShowImagesOnly);
        Assert.True(viewModel.ShowQuickStartOnStartup);
    }

    [Fact]
    public async Task SaveCommandPersistsCurrentState()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.Language = "en-US";
        viewModel.Theme = ThemePreference.Light;
        viewModel.MapDefaultZoomLevel = 18;
        viewModel.MapTileSource = MapTileSourceType.EsriWorldImagery;
        viewModel.ShowImagesOnly = false;
        viewModel.ShowQuickStartOnStartup = true;

        Assert.True(viewModel.SaveCommand.CanExecute(null));

        var saveCallCountBefore = coordinator.SaveCallCount;
        viewModel.SaveCommand.Execute(null);
        await WaitForAsync(() => coordinator.SaveCallCount == saveCallCountBefore + 1).ConfigureAwait(true);

        Assert.Equal("en-US", coordinator.LastLanguageTag);
        Assert.Equal(ThemePreference.Light, coordinator.LastTheme);
        Assert.Equal(18, coordinator.LastMapZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, coordinator.LastMapTileSource);
        Assert.True(coordinator.ShowQuickStartOnStartup);
        Assert.False(_fileBrowserPaneViewModel.ShowImagesOnly);
    }

    [Fact]
    public async Task PropertyChangesApplyImmediately()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.ThemeIndex = 2;
        viewModel.MapDefaultZoomLevelValue = 17;
        viewModel.MapTileSourceIndex = 1;
        viewModel.ShowImagesOnly = false;
        viewModel.ShowQuickStartOnStartup = true;

        Assert.Equal(ThemePreference.Dark, coordinator.LastTheme);
        Assert.Equal(16, coordinator.LastMapZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, coordinator.LastMapTileSource);
        Assert.False(_fileBrowserPaneViewModel.ShowImagesOnly);
        Assert.Equal(1, coordinator.ScheduleSaveCallCount);
    }

    [Fact]
    public async Task MapDefaultZoomLevelValueIgnoresNaNAndInfinity()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.MapDefaultZoomLevel = 14;

        viewModel.MapDefaultZoomLevelValue = double.NaN;
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevelValue = double.PositiveInfinity;
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);

        viewModel.MapDefaultZoomLevelValue = double.NegativeInfinity;
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);
    }

    [Fact]
    public async Task SaveIfDirtyAsyncPersistsCurrentState()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.ThemeIndex = 2;
        viewModel.MapDefaultZoomLevelValue = 17;
        viewModel.MapTileSourceIndex = 1;

        await viewModel.SaveIfDirtyAsync().ConfigureAwait(true);

        Assert.Equal(ThemePreference.Dark, coordinator.LastTheme);
        Assert.Equal(16, coordinator.LastMapZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, coordinator.LastMapTileSource);
        Assert.Equal(1, coordinator.SaveCallCount);
    }

    [Fact]
    public async Task SaveIfDirtyAsyncSkipsWhenNoChanges()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        await viewModel.SaveIfDirtyAsync().ConfigureAwait(true);

        Assert.Equal(0, coordinator.SaveCallCount);
    }

    [Fact]
    public async Task ResetCommandAppliesDefaultsImmediately()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.Theme = ThemePreference.Dark;
        viewModel.MapDefaultZoomLevel = 16;

        viewModel.ResetCommand.Execute(null);
        await WaitForAsync(() => coordinator.SaveCallCount == 1).ConfigureAwait(true);

        Assert.Equal(string.Empty, viewModel.Language);
        Assert.Equal(ThemePreference.System, viewModel.Theme);
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);
        Assert.Equal(MapTileSourceType.OpenStreetMap, viewModel.MapTileSource);
        Assert.True(viewModel.ShowImagesOnly);
        Assert.False(viewModel.ShowQuickStartOnStartup);
    }

    [Fact]
    public async Task ImportCommandRefreshesStateFromCoordinator()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel)
        {
            ShowQuickStartOnStartup = false
        };
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.Theme = ThemePreference.Dark;

        coordinator.OnImport = () =>
        {
            _mainViewModel.ApplySettingsState(
                language: "en-US",
                theme: ThemePreference.Light,
                mapZoomLevel: 12,
                mapTileSource: MapTileSourceType.EsriWorldImagery);
            _fileBrowserPaneViewModel.ShowImagesOnly = false;
            coordinator.ShowQuickStartOnStartup = true;
            return Task.CompletedTask;
        };

        viewModel.ImportCommand.Execute(null);
        await WaitForAsync(() => coordinator.ImportCallCount == 1).ConfigureAwait(true);

        Assert.Equal("en-US", viewModel.Language);
        Assert.Equal(ThemePreference.Light, viewModel.Theme);
        Assert.Equal(12, viewModel.MapDefaultZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, viewModel.MapTileSource);
        Assert.False(viewModel.ShowImagesOnly);
        Assert.True(viewModel.ShowQuickStartOnStartup);
    }

    public void Dispose()
    {
        _fileBrowserPaneViewModel.Dispose();
        _mainViewModel.Dispose();
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var elapsed = 0;
        while (!condition() && elapsed < timeoutMs)
        {
            await Task.Delay(20).ConfigureAwait(true);
            elapsed += 20;
        }

        Assert.True(condition());
    }

    private sealed class FakeSettingsCoordinator : ISettingsCoordinator
    {
        private readonly MainViewModel _mainViewModel;

        public FakeSettingsCoordinator(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            LastTheme = ThemePreference.System;
            LastMapZoomLevel = 14;
            LastMapTileSource = MapTileSourceType.OpenStreetMap;
        }

        public bool SettingsFileExistsAtStartup => false;

        public string? LanguageOverride => LastLanguageTag;

        public bool ShowQuickStartOnStartup { get; set; }

        public int SaveCallCount { get; private set; }

        public int ImportCallCount { get; private set; }
        public int ScheduleSaveCallCount { get; private set; }

        public string? LastLanguageTag { get; private set; }

        public ThemePreference LastTheme { get; private set; }

        public int LastMapZoomLevel { get; private set; }

        public MapTileSourceType LastMapTileSource { get; private set; }

        public Func<Task>? OnImport { get; set; }

        public Task LoadAsync()
        {
            return Task.CompletedTask;
        }

        public void ScheduleSave()
        {
            ScheduleSaveCallCount++;
        }

        public Task SaveAsync()
        {
            SaveCallCount++;
            return Task.CompletedTask;
        }

        public Task ChangeLanguageAsync(string? languageTag, bool showRestartPrompt)
        {
            var changed = !string.Equals(LastLanguageTag, languageTag, StringComparison.OrdinalIgnoreCase);
            LastLanguageTag = languageTag;
            _mainViewModel.ApplySettingsState(LastLanguageTag, LastTheme, LastMapZoomLevel, LastMapTileSource);
            if (showRestartPrompt && changed)
            {
                SaveCallCount++;
            }

            return Task.CompletedTask;
        }

        public void ChangeTheme(ThemePreference preference)
        {
            LastTheme = preference;
            _mainViewModel.ApplySettingsState(LastLanguageTag, LastTheme, LastMapZoomLevel, LastMapTileSource);
        }

        public void ChangeMapZoomLevel(int level)
        {
            LastMapZoomLevel = level;
            _mainViewModel.ApplySettingsState(LastLanguageTag, LastTheme, LastMapZoomLevel, LastMapTileSource);
        }

        public void ChangeMapTileSource(MapTileSourceType sourceType)
        {
            LastMapTileSource = sourceType;
            _mainViewModel.ApplySettingsState(LastLanguageTag, LastTheme, LastMapZoomLevel, LastMapTileSource);
        }

        public Task ExportSettingsAsync()
        {
            return Task.CompletedTask;
        }

        public async Task ImportSettingsAsync()
        {
            ImportCallCount++;
            if (OnImport is not null)
            {
                await OnImport().ConfigureAwait(true);
            }
        }

        public void Dispose()
        {
        }
    }
}
