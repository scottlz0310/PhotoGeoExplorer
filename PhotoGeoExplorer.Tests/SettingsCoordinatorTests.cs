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

    [Theory]
    [InlineData("ja", null, true)]
    [InlineData("en", null, false)]
    [InlineData("ja", false, false)]
    [InlineData("en", true, true)]
    public async Task JapanMapSourcesFollowTheUiLanguageUntilExplicitlyConfigured(
        string language,
        bool? enabled,
        bool expected)
    {
        using var context = CreateContext();
        await context.SettingsService.SaveAsync(new AppSettings
        {
            Language = language,
            JapanMapSourcesEnabled = enabled,
            MapTileSource = MapTileSourceType.GsiStandard
        }).ConfigureAwait(true);

        await context.Coordinator.LoadAsync().ConfigureAwait(true);

        Assert.Equal(expected, context.Coordinator.JapanMapSourcesEnabled);
        Assert.Equal(
            expected ? MapTileSourceType.GsiStandard : MapTileSourceType.OpenStreetMap,
            context.MainViewModel.CurrentMapTileSource);

        context.Coordinator.JapanMapSourcesEnabled = false;
        await context.Coordinator.SaveAsync().ConfigureAwait(true);
        var saved = await context.SettingsService.LoadAsync().ConfigureAwait(true);

        Assert.False(saved.JapanMapSourcesEnabled);
        Assert.Equal(MapTileSourceType.OpenStreetMap, saved.MapTileSource);
    }

    [Fact]
    public async Task LoadAsyncAppliesMapHeaderDisplaySettings()
    {
        using var context = CreateContext();
        var persisted = new AppSettings
        {
            ShowMapHeaderAddress = false,
            ShowMapHeaderRoadName = false,
            ShowMapHeaderCoordinates = false,
            ShowMapHeaderZoom = false,
            MapHeaderIncludePrefecture = true
        };
        await context.SettingsService.SaveAsync(persisted).ConfigureAwait(true);

        await context.Coordinator.LoadAsync().ConfigureAwait(true);

        var display = context.MapPaneViewModel.LocationInfo.DisplaySettings;
        Assert.Same(display, context.Coordinator.MapHeaderDisplay);
        Assert.False(display.ShowAddress);
        Assert.False(display.ShowRoadName);
        Assert.False(display.ShowCoordinates);
        Assert.False(display.ShowZoom);
        Assert.True(display.IncludePrefecture);
    }

    [Theory]
    [InlineData(AreaDataDownloadMode.Ask)]
    [InlineData(AreaDataDownloadMode.Auto)]
    [InlineData(AreaDataDownloadMode.Never)]
    public async Task LoadAsyncAppliesTheAreaDataDownloadMode(AreaDataDownloadMode mode)
    {
        using var context = CreateContext();
        await context.SettingsService.SaveAsync(new AppSettings { AreaDataDownloadMode = mode }).ConfigureAwait(true);

        await context.Coordinator.LoadAsync().ConfigureAwait(true);

        Assert.Equal(mode, context.MapPaneViewModel.AreaDataAcquisition.Mode);
    }

    [Fact]
    public async Task LoadAsyncFallsBackToAskForAnUnknownAreaDataDownloadMode()
    {
        using var context = CreateContext();
        await context.SettingsService.SaveAsync(new AppSettings { AreaDataDownloadMode = (AreaDataDownloadMode)99 }).ConfigureAwait(true);

        await context.Coordinator.LoadAsync().ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadMode.Ask, context.MapPaneViewModel.AreaDataAcquisition.Mode);
    }

    [Fact]
    public async Task AreaDataDownloadModeChangesArePersisted()
    {
        using var context = CreateContext();

        context.MapPaneViewModel.AreaDataAcquisition.Mode = AreaDataDownloadMode.Never;
        context.Debounce.Elapse();
        var saved = await WaitForSavedSettingsAsync(context, settings => settings.AreaDataDownloadMode == AreaDataDownloadMode.Never).ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadMode.Never, saved.AreaDataDownloadMode);
    }

    [Fact]
    public async Task AreaDataDownloadModeDefaultsToAsk()
    {
        using var context = CreateContext();

        await context.Coordinator.SaveAsync().ConfigureAwait(true);
        var saved = await context.SettingsService.LoadAsync().ConfigureAwait(true);

        Assert.Equal(AreaDataDownloadMode.Ask, saved.AreaDataDownloadMode);
    }

    [Fact]
    public async Task MapHeaderDisplayDefaultsMatchAppSettingsDefaults()
    {
        using var context = CreateContext();

        await context.Coordinator.SaveAsync().ConfigureAwait(true);
        var saved = await context.SettingsService.LoadAsync().ConfigureAwait(true);

        Assert.True(saved.ShowMapHeaderAddress);
        Assert.True(saved.ShowMapHeaderRoadName);
        Assert.True(saved.ShowMapHeaderCoordinates);
        Assert.True(saved.ShowMapHeaderZoom);
        Assert.False(saved.MapHeaderIncludePrefecture);
    }

    [Fact]
    public async Task MapHeaderDisplayChangesAreDebouncedAndPersisted()
    {
        using var context = CreateContext();

        context.Coordinator.MapHeaderDisplay.ShowAddress = false;
        context.Coordinator.MapHeaderDisplay.ShowRoadName = false;
        context.Coordinator.MapHeaderDisplay.ShowCoordinates = false;
        context.Coordinator.MapHeaderDisplay.ShowZoom = false;
        context.Coordinator.MapHeaderDisplay.IncludePrefecture = true;

        context.Debounce.Elapse();
        var saved = await WaitForSavedSettingsAsync(context, settings => settings.MapHeaderIncludePrefecture).ConfigureAwait(true);

        Assert.False(saved.ShowMapHeaderAddress);
        Assert.False(saved.ShowMapHeaderRoadName);
        Assert.False(saved.ShowMapHeaderCoordinates);
        Assert.False(saved.ShowMapHeaderZoom);
        Assert.True(saved.MapHeaderIncludePrefecture);
    }

    [Fact]
    public async Task ChangeThemeDebouncesAndPersistsLatestValue()
    {
        using var context = CreateContext();

        context.Coordinator.ChangeTheme(ThemePreference.Light);
        context.Coordinator.ChangeTheme(ThemePreference.Dark);

        // 2 回目の変更で、1 回目の保存待ちは取り消される。デバウンス時間が経つまでは保存されない
        Assert.Equal(2, context.Debounce.Tokens.Length);
        Assert.True(context.Debounce.Tokens[0].IsCancellationRequested);
        Assert.False(context.Debounce.Tokens[1].IsCancellationRequested);
        Assert.False(context.SettingsService.SettingsFileExists());

        context.Debounce.Elapse();
        var saved = await WaitForSavedSettingsAsync(context, settings => settings.Theme == ThemePreference.Dark).ConfigureAwait(true);
        Assert.Equal(ThemePreference.Dark, saved.Theme);
    }

    [Fact]
    public async Task FileBrowserPaneSettingChangesAreDebouncedAndPersisted()
    {
        using var context = CreateContext();

        context.FileBrowserPaneViewModel.ShowImagesOnly = false;
        context.FileBrowserPaneViewModel.FileViewMode = FileViewMode.Icon;
        context.FileBrowserPaneViewModel.ShowDetailsTakenAtColumn = true;

        context.Debounce.Elapse();
        var saved = await WaitForSavedSettingsAsync(context, settings => settings.ShowDetailsTakenAtColumn).ConfigureAwait(true);

        Assert.False(saved.ShowImagesOnly);
        Assert.Equal(FileViewMode.Icon, saved.FileViewMode);
        Assert.True(saved.ShowDetailsTakenAtColumn);
    }

    [Fact]
    public async Task SaveAsyncCancelsPendingDebouncedSave()
    {
        using var context = CreateContext();

        context.Coordinator.ChangeTheme(ThemePreference.Light);
        Assert.False(context.Debounce.Tokens[0].IsCancellationRequested);
        await context.Coordinator.SaveAsync().ConfigureAwait(true);
        Assert.True(context.Debounce.Tokens[0].IsCancellationRequested);

        const string sentinel = "manual-marker";
        await File.WriteAllTextAsync(context.SettingsPath, sentinel).ConfigureAwait(true);

        // 取り消された保存待ちは完了しないため、デバウンス時間が経っても保存は走らない
        context.Debounce.Elapse();
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

    private static async Task<AppSettings> WaitForSavedSettingsAsync(TestContext context, Func<AppSettings, bool> predicate)
    {
        // 保存は別タスクで走るため、固定時間ではなく、条件を満たすまで待つ（上限は失敗の検出用）
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var saved = await context.SettingsService.LoadAsync().ConfigureAwait(true);
            if (predicate(saved))
            {
                return saved;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The expected settings were not saved within 10 seconds.");
            }

            await Task.Delay(20).ConfigureAwait(true);
        }
    }

    private TestContext CreateContext()
    {
        var debounce = new ManualDebounce();
        var root = CreateTempDirectory();
        var settingsPath = Path.Combine(root, "settings.json");
        var settingsService = new SettingsService(settingsPath);
        var workspaceState = new WorkspaceState();
        var shellViewModel = new MainViewModel(workspaceState);
        var fileBrowserPaneViewModel = new FileBrowserPaneViewModel(
            new FileBrowserPaneService(),
            workspaceState,
            folderWatcherService: NoOpFolderWatcherService.Shared);
        var mapPaneViewModel = new MapPaneViewModel(new MapPaneService(), workspaceState);
        var dialogService = new FakeDialogService();
        var coordinator = new SettingsCoordinator(
            settingsService,
            dialogService,
            _ => { },
            fileBrowserPaneViewModel,
            mapPaneViewModel,
            shellViewModel,
            () => null,
            debounce.DelayAsync);

        shellViewModel.ConfigureSettingsCoordinator(coordinator);

        return new TestContext(
            coordinator,
            debounce,
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

    /// <summary>保存のデバウンス時間を、実時間ではなく Elapse() で進める。取り消されたものは完了せず、取り消しとして扱う</summary>
    private sealed class ManualDebounce
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource> _delays = [];
        private readonly List<CancellationToken> _tokens = [];

        public CancellationToken[] Tokens
        {
            get
            {
                lock (_gate)
                {
                    return _tokens.ToArray();
                }
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _delays.Add(completion);
                _tokens.Add(cancellationToken);
            }

            return completion.Task.WaitAsync(cancellationToken);
        }

        public void Elapse()
        {
            TaskCompletionSource[] delays;
            lock (_gate)
            {
                delays = _delays.ToArray();
            }

            foreach (var delay in delays)
            {
                delay.TrySetResult();
            }
        }
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            SettingsCoordinator coordinator,
            ManualDebounce debounce,
            string settingsPath,
            SettingsService settingsService,
            MainViewModel mainViewModel,
            FileBrowserPaneViewModel fileBrowserPaneViewModel,
            MapPaneViewModel mapPaneViewModel)
        {
            Coordinator = coordinator;
            Debounce = debounce;
            SettingsPath = settingsPath;
            SettingsService = settingsService;
            MainViewModel = mainViewModel;
            FileBrowserPaneViewModel = fileBrowserPaneViewModel;
            MapPaneViewModel = mapPaneViewModel;
        }

        public SettingsCoordinator Coordinator { get; }

        public ManualDebounce Debounce { get; }

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

        public Task<string?> ShowMapImageSaveFilePickerAsync(
            MapImageSavePickerOptions options,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
