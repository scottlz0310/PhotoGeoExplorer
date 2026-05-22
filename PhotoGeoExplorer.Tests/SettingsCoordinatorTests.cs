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
            ShowDetailsModifiedColumn = false,
            ShowDetailsResolutionColumn = false,
            ShowDetailsSizeColumn = false,
            ShowDetailsTakenAtColumn = true,
            ShowDetailsLocationColumn = true,
            ShowQuickStartOnStartup = true,
            ExternalContentBaseUrl = "https://example.com/help",
            PaneLayoutPreset = PaneLayoutPreset.LeftCenterRight,
            PaneRegion1View = PaneViewType.Preview,
            PaneRegion2View = PaneViewType.Map,
            PaneRegion3View = PaneViewType.File
        };
        await context.SettingsService.SaveAsync(persisted).ConfigureAwait(true);

        await context.Coordinator.LoadAsync().ConfigureAwait(true);

        Assert.Equal("ja-JP", context.MainViewModel.CurrentLanguage);
        Assert.Equal(ThemePreference.Dark, context.MainViewModel.CurrentTheme);
        Assert.Equal(16, context.MainViewModel.CurrentMapZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, context.MainViewModel.CurrentMapTileSource);
        Assert.False(context.FileBrowserPaneViewModel.ShowImagesOnly);
        Assert.Equal(FileViewMode.Icon, context.FileBrowserPaneViewModel.FileViewMode);
        Assert.False(context.FileBrowserPaneViewModel.ShowDetailsModifiedColumn);
        Assert.False(context.FileBrowserPaneViewModel.ShowDetailsResolutionColumn);
        Assert.False(context.FileBrowserPaneViewModel.ShowDetailsSizeColumn);
        Assert.True(context.FileBrowserPaneViewModel.ShowDetailsTakenAtColumn);
        Assert.True(context.FileBrowserPaneViewModel.ShowDetailsLocationColumn);
        Assert.Equal(16, context.MapPaneViewModel.MapDefaultZoomLevel);
        Assert.True(context.Coordinator.ShowQuickStartOnStartup);
        Assert.Equal("https://example.com/help", context.Coordinator.ExternalContentBaseUrl);
        Assert.Equal(PaneLayoutPreset.LeftCenterRight, context.Coordinator.PaneLayoutPreset);
        Assert.Equal(PaneViewType.Preview, context.Coordinator.PaneRegion1View);
        Assert.Equal(PaneViewType.Map, context.Coordinator.PaneRegion2View);
        Assert.Equal(PaneViewType.File, context.Coordinator.PaneRegion3View);
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
        context.FileBrowserPaneViewModel.ShowDetailsModifiedColumn = false;
        context.FileBrowserPaneViewModel.ShowDetailsResolutionColumn = false;
        context.FileBrowserPaneViewModel.ShowDetailsSizeColumn = false;
        context.FileBrowserPaneViewModel.ShowDetailsTakenAtColumn = true;
        context.FileBrowserPaneViewModel.ShowDetailsLocationColumn = true;
        context.Coordinator.ChangePaneLayout(
            PaneLayoutPreset.LeftSplitAndRight,
            PaneViewType.Map,
            PaneViewType.File,
            PaneViewType.Preview);

        await context.Coordinator.SaveAsync().ConfigureAwait(true);
        var saved = await context.SettingsService.LoadAsync().ConfigureAwait(true);

        Assert.Equal("en-US", saved.Language);
        Assert.Equal(ThemePreference.Light, saved.Theme);
        Assert.Equal(18, saved.MapDefaultZoomLevel);
        Assert.Equal(MapTileSourceType.EsriWorldImagery, saved.MapTileSource);
        Assert.True(saved.ShowQuickStartOnStartup);
        Assert.False(saved.ShowImagesOnly);
        Assert.Equal(FileViewMode.List, saved.FileViewMode);
        Assert.False(saved.ShowDetailsModifiedColumn);
        Assert.False(saved.ShowDetailsResolutionColumn);
        Assert.False(saved.ShowDetailsSizeColumn);
        Assert.True(saved.ShowDetailsTakenAtColumn);
        Assert.True(saved.ShowDetailsLocationColumn);
        Assert.Equal(AppSettings.DefaultExternalContentBaseUrl, saved.ExternalContentBaseUrl);
        Assert.Equal(PaneLayoutPreset.LeftSplitAndRight, saved.PaneLayoutPreset);
        Assert.Equal(PaneViewType.Map, saved.PaneRegion1View);
        Assert.Equal(PaneViewType.File, saved.PaneRegion2View);
        Assert.Equal(PaneViewType.Preview, saved.PaneRegion3View);
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
    public async Task FileBrowserPaneSettingChangesAreDebouncedAndPersisted()
    {
        using var context = CreateContext();

        context.FileBrowserPaneViewModel.ShowImagesOnly = false;
        context.FileBrowserPaneViewModel.FileViewMode = FileViewMode.Icon;
        context.FileBrowserPaneViewModel.ShowDetailsTakenAtColumn = true;

        await Task.Delay(450).ConfigureAwait(true);
        var saved = await context.SettingsService.LoadAsync().ConfigureAwait(true);

        Assert.False(saved.ShowImagesOnly);
        Assert.Equal(FileViewMode.Icon, saved.FileViewMode);
        Assert.True(saved.ShowDetailsTakenAtColumn);
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

    [Fact]
    public void ChangePaneLayoutDoesNotRaiseEventWhenValuesUnchanged()
    {
        using var context = CreateContext();
        var eventCount = 0;
        context.Coordinator.PaneLayoutChanged += (_, _) => eventCount++;

        context.Coordinator.ChangePaneLayout(
            PaneLayoutPreset.LeftSplitAndRight,
            PaneViewType.Map,
            PaneViewType.File,
            PaneViewType.Preview);
        context.Coordinator.ChangePaneLayout(
            PaneLayoutPreset.LeftSplitAndRight,
            PaneViewType.Map,
            PaneViewType.File,
            PaneViewType.Preview);

        Assert.Equal(1, eventCount);
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

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("https://photogeoexplorer.pages.dev", "https://photogeoexplorer.pages.dev")]
    [InlineData("https://photogeoexplorer.pages.dev/", "https://photogeoexplorer.pages.dev")]
    [InlineData("http://example.com/help", "http://example.com/help")]
    [InlineData("ftp://example.com", null)]
    public void NormalizeExternalContentBaseUrlReturnsExpectedValue(string? input, string? expected)
    {
        var actual = SettingsCoordinator.NormalizeExternalContentBaseUrl(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NormalizePaneLayoutPresetReturnsInputWhenDefined(int input)
    {
        var preset = (PaneLayoutPreset)input;
        var actual = SettingsCoordinator.NormalizePaneLayoutPreset(preset);
        Assert.Equal(preset, actual);
    }

    [Fact]
    public void NormalizePaneLayoutPresetReturnsDefaultForInvalidValue()
    {
        var actual = SettingsCoordinator.NormalizePaneLayoutPreset((PaneLayoutPreset)999);
        Assert.Equal(AppSettings.DefaultPaneLayoutPreset, actual);
    }

    [Fact]
    public void NormalizePaneRegionViewsReturnsSameValuesWhenDistinct()
    {
        var actual = SettingsCoordinator.NormalizePaneRegionViews(
            PaneViewType.Map,
            PaneViewType.Preview,
            PaneViewType.File);

        Assert.Equal(PaneViewType.Map, actual.Region1View);
        Assert.Equal(PaneViewType.Preview, actual.Region2View);
        Assert.Equal(PaneViewType.File, actual.Region3View);
    }

    [Fact]
    public void NormalizePaneRegionViewsReplacesDuplicateWithUnusedValue()
    {
        var actual = SettingsCoordinator.NormalizePaneRegionViews(
            PaneViewType.File,
            PaneViewType.File,
            PaneViewType.Map);

        Assert.Equal(PaneViewType.File, actual.Region1View);
        Assert.Equal(PaneViewType.Preview, actual.Region2View);
        Assert.Equal(PaneViewType.Map, actual.Region3View);
    }

    [Fact]
    public void NormalizePaneRegionViewsNormalizesInvalidAndRemovesDuplicates()
    {
        var actual = SettingsCoordinator.NormalizePaneRegionViews(
            (PaneViewType)99,
            (PaneViewType)100,
            PaneViewType.File);

        Assert.Equal(PaneViewType.File, actual.Region1View);
        Assert.Equal(PaneViewType.Preview, actual.Region2View);
        Assert.Equal(PaneViewType.Map, actual.Region3View);
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
        var shellViewModel = new MainViewModel(workspaceState);
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
