using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using PhotoGeoExplorer.Models;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void HelpCommandsAreDisabledBeforeHelpServiceConfiguration()
    {
        var viewModel = new MainViewModel();

        Assert.False(viewModel.ShowGettingStartedCommand.CanExecute(null));
        Assert.False(viewModel.ShowBasicOperationsCommand.CanExecute(null));
        Assert.False(viewModel.ShowDetailedHelpCommand.CanExecute(null));
        Assert.False(viewModel.ShowAboutCommand.CanExecute(null));
    }

    [Fact]
    public void HelpCommandsInvokeConfiguredHelpService()
    {
        var viewModel = new MainViewModel();
        using var helpService = new TestHelpService();
        viewModel.ConfigureHelpService(helpService);

        viewModel.ShowGettingStartedCommand.Execute(null);
        viewModel.ShowBasicOperationsCommand.Execute(null);
        viewModel.ShowDetailedHelpCommand.Execute(null);
        viewModel.ShowAboutCommand.Execute(null);

        Assert.Equal(1, helpService.ShowGettingStartedCallCount);
        Assert.Equal(1, helpService.ShowBasicsCallCount);
        Assert.Equal(1, helpService.ShowHelpHtmlWindowCallCount);
        Assert.Equal(1, helpService.ShowAboutCallCount);
    }

    [Fact]
    public void SettingsCommandsAreDisabledBeforeSettingsCoordinatorConfiguration()
    {
        var viewModel = new MainViewModel();

        Assert.False(viewModel.ChangeLanguageCommand.CanExecute("ja-JP"));
        Assert.False(viewModel.ChangeThemeCommand.CanExecute(nameof(ThemePreference.Dark)));
        Assert.False(viewModel.ChangeMapZoomLevelCommand.CanExecute("16"));
        Assert.False(viewModel.ChangeMapTileSourceCommand.CanExecute(nameof(MapTileSourceType.EsriWorldImagery)));
        Assert.False(viewModel.ExportSettingsCommand.CanExecute(null));
        Assert.False(viewModel.ImportSettingsCommand.CanExecute(null));
        Assert.False(viewModel.PersistLayoutSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void SettingsCommandsInvokeConfiguredSettingsCoordinator()
    {
        var viewModel = new MainViewModel();
        using var settingsCoordinator = new TestSettingsCoordinator();
        viewModel.ConfigureSettingsCoordinator(settingsCoordinator);

        viewModel.ChangeLanguageCommand.Execute("ja");
        viewModel.ChangeThemeCommand.Execute(nameof(ThemePreference.Dark));
        viewModel.ChangeMapZoomLevelCommand.Execute("16");
        viewModel.ChangeMapTileSourceCommand.Execute(nameof(MapTileSourceType.EsriWorldImagery));
        viewModel.ExportSettingsCommand.Execute(null);
        viewModel.ImportSettingsCommand.Execute(null);
        viewModel.PersistLayoutSettingsCommand.Execute(null);

        Assert.Equal(1, settingsCoordinator.ChangeLanguageCallCount);
        Assert.Equal("ja", settingsCoordinator.LastLanguageTag);
        Assert.True(settingsCoordinator.LastShowRestartPrompt);
        Assert.Equal(1, settingsCoordinator.ChangeThemeCallCount);
        Assert.Equal(ThemePreference.Dark, settingsCoordinator.LastTheme);
        Assert.Equal(1, settingsCoordinator.ChangeMapZoomLevelCallCount);
        Assert.Equal(16, settingsCoordinator.LastMapZoomLevel);
        Assert.Equal(1, settingsCoordinator.ChangeMapTileSourceCallCount);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, settingsCoordinator.LastMapTileSource);
        Assert.Equal(1, settingsCoordinator.ExportCallCount);
        Assert.Equal(1, settingsCoordinator.ImportCallCount);
        Assert.Equal(1, settingsCoordinator.ScheduleSaveCallCount);
    }

    [Fact]
    public void ApplySettingsStateUpdatesMenuStateProperties()
    {
        var viewModel = new MainViewModel();

        viewModel.ApplySettingsState(
            language: "en-US",
            theme: ThemePreference.Light,
            mapZoomLevel: 18,
            mapTileSource: MapTileSourceType.EsriWorldImagery);

        Assert.Equal("en-US", viewModel.CurrentLanguage);
        Assert.Equal(ThemePreference.Light, viewModel.CurrentTheme);
        Assert.Equal(18, viewModel.CurrentMapZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, viewModel.CurrentMapTileSource);
        Assert.False(viewModel.IsLanguageSystem);
        Assert.True(viewModel.IsLanguageEnglish);
        Assert.True(viewModel.IsThemeLight);
        Assert.True(viewModel.IsMapZoomLevel18);
        Assert.True(viewModel.IsMapTileSourceEsri);
    }

    private sealed class TestHelpService : IHelpService
    {
        public int ShowGettingStartedCallCount { get; private set; }
        public int ShowBasicsCallCount { get; private set; }
        public int ShowHelpHtmlWindowCallCount { get; private set; }
        public int ShowAboutCallCount { get; private set; }

        public Task ShowGettingStartedAsync()
        {
            ShowGettingStartedCallCount++;
            return Task.CompletedTask;
        }

        public Task ShowBasicsAsync()
        {
            ShowBasicsCallCount++;
            return Task.CompletedTask;
        }

        public Task ShowHelpHtmlWindowAsync()
        {
            ShowHelpHtmlWindowCallCount++;
            return Task.CompletedTask;
        }

        public Task ShowAboutAsync()
        {
            ShowAboutCallCount++;
            return Task.CompletedTask;
        }

        public Task ShowQuickStartIfNeededAsync()
        {
            return Task.CompletedTask;
        }

        public void CloseHelpWindow()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestSettingsCoordinator : ISettingsCoordinator
    {
        public int ChangeLanguageCallCount { get; private set; }
        public int ChangeThemeCallCount { get; private set; }
        public int ChangeMapZoomLevelCallCount { get; private set; }
        public int ChangeMapTileSourceCallCount { get; private set; }
        public int ExportCallCount { get; private set; }
        public int ImportCallCount { get; private set; }
        public int ScheduleSaveCallCount { get; private set; }
        public string? LastLanguageTag { get; private set; }
        public bool LastShowRestartPrompt { get; private set; }
        public ThemePreference LastTheme { get; private set; } = ThemePreference.System;
        public int LastMapZoomLevel { get; private set; }
        public MapTileSourceType LastMapTileSource { get; private set; } = MapTileSourceType.OpenStreetMap;

        public bool SettingsFileExistsAtStartup => false;

        public string? LanguageOverride => null;

        public string? ExternalContentBaseUrl => null;

        public bool ShowQuickStartOnStartup { get; set; }

        public PaneLayoutPreset PaneLayoutPreset => AppSettings.DefaultPaneLayoutPreset;

        public PaneViewType PaneRegion1View => AppSettings.DefaultPaneRegion1View;

        public PaneViewType PaneRegion2View => AppSettings.DefaultPaneRegion2View;

        public PaneViewType PaneRegion3View => AppSettings.DefaultPaneRegion3View;

        public event EventHandler<PaneLayoutChangedEventArgs>? PaneLayoutChanged;

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
            return Task.CompletedTask;
        }

        public Task ChangeLanguageAsync(string? languageTag, bool showRestartPrompt)
        {
            ChangeLanguageCallCount++;
            LastLanguageTag = languageTag;
            LastShowRestartPrompt = showRestartPrompt;
            return Task.CompletedTask;
        }

        public void ChangeTheme(ThemePreference preference)
        {
            ChangeThemeCallCount++;
            LastTheme = preference;
        }

        public void ChangeMapZoomLevel(int level)
        {
            ChangeMapZoomLevelCallCount++;
            LastMapZoomLevel = level;
        }

        public void ChangeMapTileSource(MapTileSourceType sourceType)
        {
            ChangeMapTileSourceCallCount++;
            LastMapTileSource = sourceType;
        }

        public void ChangePaneLayout(PaneLayoutPreset preset, PaneViewType region1View, PaneViewType region2View, PaneViewType region3View)
        {
            PaneLayoutChanged?.Invoke(this, new PaneLayoutChangedEventArgs(preset, region1View, region2View, region3View));
        }

        public Task ExportSettingsAsync()
        {
            ExportCallCount++;
            return Task.CompletedTask;
        }

        public Task ImportSettingsAsync()
        {
            ImportCallCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
