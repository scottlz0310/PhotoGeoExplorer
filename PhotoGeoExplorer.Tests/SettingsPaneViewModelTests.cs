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
            ShowQuickStartOnStartup = true,
            PaneLayoutPreset = PaneLayoutPreset.LeftCenterRight,
            PaneRegion1View = PaneViewType.Preview,
            PaneRegion2View = PaneViewType.File,
            PaneRegion3View = PaneViewType.Map
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
        Assert.Equal(0, viewModel.PaneLayoutPresetIndex);
        Assert.Equal(1, viewModel.PaneRegion1ViewIndex);
        Assert.Equal(0, viewModel.PaneRegion2ViewIndex);
        Assert.Equal(2, viewModel.PaneRegion3ViewIndex);
    }

    [Fact]
    public async Task InitializeAsyncWithSystemLanguageShowsSystemOptionValue()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        _mainViewModel.ApplySettingsState(
            language: null,
            theme: ThemePreference.System,
            mapZoomLevel: 14,
            mapTileSource: MapTileSourceType.OpenStreetMap);

        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal("system", viewModel.Language);
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
        viewModel.PaneLayoutPresetIndex = 2;
        viewModel.PaneRegion1ViewIndex = 2;
        viewModel.PaneRegion2ViewIndex = 1;
        viewModel.PaneRegion3ViewIndex = 0;

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
        Assert.Equal(PaneLayoutPreset.LeftSplitAndRight, coordinator.PaneLayoutPreset);
        Assert.Equal(PaneViewType.Map, coordinator.PaneRegion1View);
        Assert.Equal(PaneViewType.Preview, coordinator.PaneRegion2View);
        Assert.Equal(PaneViewType.File, coordinator.PaneRegion3View);
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
    public async Task PaneRegionViewSelectionSwapsDuplicateAssignments()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.PaneRegion1ViewIndex = (int)PaneViewType.Map;

        Assert.Equal(PaneViewType.Map, (PaneViewType)viewModel.PaneRegion1ViewIndex);
        Assert.Equal(PaneViewType.Preview, (PaneViewType)viewModel.PaneRegion2ViewIndex);
        Assert.Equal(PaneViewType.File, (PaneViewType)viewModel.PaneRegion3ViewIndex);
        Assert.Equal(PaneViewType.Map, coordinator.PaneRegion1View);
        Assert.Equal(PaneViewType.Preview, coordinator.PaneRegion2View);
        Assert.Equal(PaneViewType.File, coordinator.PaneRegion3View);
    }

    [Fact]
    public async Task PaneLayoutPresetIndexMapsKnownAndDefaultValues()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.PaneLayoutPresetIndex = 0;
        Assert.Equal(PaneLayoutPreset.LeftCenterRight, viewModel.SelectedPaneLayoutPreset);

        viewModel.PaneLayoutPresetIndex = 1;
        Assert.Equal(PaneLayoutPreset.LeftAndRightSplit, viewModel.SelectedPaneLayoutPreset);
    }

    [Fact]
    public async Task RegionLabelsFollowSelectedLayoutPreset()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionLeft"), viewModel.Region1Label);
        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionTopRight"), viewModel.Region2Label);
        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionBottomRight"), viewModel.Region3Label);

        viewModel.PaneLayoutPresetIndex = 0;
        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionLeft"), viewModel.Region1Label);
        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionCenter"), viewModel.Region2Label);
        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionRight"), viewModel.Region3Label);

        viewModel.PaneLayoutPresetIndex = 2;
        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionTopLeft"), viewModel.Region1Label);
        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionBottomLeft"), viewModel.Region2Label);
        Assert.Equal(LocalizationService.GetString("SettingsPaneLayoutRegionRight"), viewModel.Region3Label);
    }

    [Fact]
    public async Task PaneRegion2SelectionSwapsWithRegion1WhenDuplicate()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.PaneRegion2ViewIndex = (int)PaneViewType.File;

        Assert.Equal(PaneViewType.Preview, (PaneViewType)viewModel.PaneRegion1ViewIndex);
        Assert.Equal(PaneViewType.File, (PaneViewType)viewModel.PaneRegion2ViewIndex);
        Assert.Equal(PaneViewType.Map, (PaneViewType)viewModel.PaneRegion3ViewIndex);
    }

    [Fact]
    public async Task PaneRegion2SelectionSwapsWithRegion3WhenDuplicate()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.PaneRegion2ViewIndex = (int)PaneViewType.Map;

        Assert.Equal(PaneViewType.File, (PaneViewType)viewModel.PaneRegion1ViewIndex);
        Assert.Equal(PaneViewType.Map, (PaneViewType)viewModel.PaneRegion2ViewIndex);
        Assert.Equal(PaneViewType.Preview, (PaneViewType)viewModel.PaneRegion3ViewIndex);
    }

    [Fact]
    public async Task PaneRegion3SelectionSwapsWithRegion1OrRegion2WhenDuplicate()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.PaneRegion3ViewIndex = (int)PaneViewType.Preview;
        Assert.Equal(PaneViewType.File, (PaneViewType)viewModel.PaneRegion1ViewIndex);
        Assert.Equal(PaneViewType.Map, (PaneViewType)viewModel.PaneRegion2ViewIndex);
        Assert.Equal(PaneViewType.Preview, (PaneViewType)viewModel.PaneRegion3ViewIndex);

        viewModel.PaneRegion3ViewIndex = (int)PaneViewType.File;
        Assert.Equal(PaneViewType.Preview, (PaneViewType)viewModel.PaneRegion1ViewIndex);
        Assert.Equal(PaneViewType.Map, (PaneViewType)viewModel.PaneRegion2ViewIndex);
        Assert.Equal(PaneViewType.File, (PaneViewType)viewModel.PaneRegion3ViewIndex);
    }

    [Fact]
    public async Task PaneRegionSelectionAcceptsUnexpectedEnumWithoutSwapping()
    {
        using var coordinator = new FakeSettingsCoordinator(_mainViewModel);
        var viewModel = new SettingsPaneViewModel(coordinator, _fileBrowserPaneViewModel, _mainViewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);

        viewModel.PaneRegion2View = (PaneViewType)99;

        Assert.Equal(PaneViewType.File, (PaneViewType)viewModel.PaneRegion1ViewIndex);
        Assert.Equal(PaneViewType.Map, (PaneViewType)viewModel.PaneRegion3ViewIndex);
        Assert.Equal((PaneViewType)99, coordinator.PaneRegion2View);
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

        Assert.Equal("system", viewModel.Language);
        Assert.Equal(ThemePreference.System, viewModel.Theme);
        Assert.Equal(14, viewModel.MapDefaultZoomLevel);
        Assert.Equal(MapTileSourceType.OpenStreetMap, viewModel.MapTileSource);
        Assert.True(viewModel.ShowImagesOnly);
        Assert.False(viewModel.ShowQuickStartOnStartup);
        Assert.Equal((int)AppSettings.DefaultPaneLayoutPreset, viewModel.PaneLayoutPresetIndex);
        Assert.Equal((int)AppSettings.DefaultPaneRegion1View, viewModel.PaneRegion1ViewIndex);
        Assert.Equal((int)AppSettings.DefaultPaneRegion2View, viewModel.PaneRegion2ViewIndex);
        Assert.Equal((int)AppSettings.DefaultPaneRegion3View, viewModel.PaneRegion3ViewIndex);
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
            PaneLayoutPreset = AppSettings.DefaultPaneLayoutPreset;
            PaneRegion1View = AppSettings.DefaultPaneRegion1View;
            PaneRegion2View = AppSettings.DefaultPaneRegion2View;
            PaneRegion3View = AppSettings.DefaultPaneRegion3View;
        }

        public bool SettingsFileExistsAtStartup => false;

        public string? LanguageOverride => LastLanguageTag;

        public string? ExternalContentBaseUrl => null;

        public bool ShowQuickStartOnStartup { get; set; }

        public PaneLayoutPreset PaneLayoutPreset { get; set; }

        public PaneViewType PaneRegion1View { get; set; }

        public PaneViewType PaneRegion2View { get; set; }

        public PaneViewType PaneRegion3View { get; set; }

        public event EventHandler<PaneLayoutChangedEventArgs>? PaneLayoutChanged;

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

        public void ChangePaneLayout(PaneLayoutPreset preset, PaneViewType region1View, PaneViewType region2View, PaneViewType region3View)
        {
            PaneLayoutPreset = preset;
            PaneRegion1View = region1View;
            PaneRegion2View = region2View;
            PaneRegion3View = region3View;
            PaneLayoutChanged?.Invoke(this, new PaneLayoutChangedEventArgs(preset, region1View, region2View, region3View));
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
