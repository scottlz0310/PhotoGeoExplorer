using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定PaneのViewModel。
/// 実行中アプリの設定状態を編集し、SettingsCoordinatorに反映します。
/// </summary>
internal sealed class SettingsPaneViewModel : PaneViewModelBase
{
    private readonly ISettingsCoordinator _settingsCoordinator;
    private readonly FileBrowserPaneViewModel _fileBrowserPaneViewModel;
    private readonly MainViewModel _shellViewModel;

    private string? _language;
    private ThemePreference _theme = ThemePreference.System;
    private int _mapDefaultZoomLevel = MapZoomLevelCatalog.Default;
    private MapTileSourceType _mapTileSource = MapTileSourceType.OpenStreetMap;
    private bool _showImagesOnly = true;
    private bool _showQuickStartOnStartup;
    private string? _lastFolderPath;
    private bool _suppressDirtyTracking;
    private bool _hasPendingChanges;
    private int _languageChangeVersion;

    internal SettingsPaneViewModel(
        ISettingsCoordinator settingsCoordinator,
        FileBrowserPaneViewModel fileBrowserPaneViewModel,
        MainViewModel shellViewModel)
    {
        _settingsCoordinator = settingsCoordinator ?? throw new ArgumentNullException(nameof(settingsCoordinator));
        _fileBrowserPaneViewModel = fileBrowserPaneViewModel ?? throw new ArgumentNullException(nameof(fileBrowserPaneViewModel));
        _shellViewModel = shellViewModel ?? throw new ArgumentNullException(nameof(shellViewModel));
        Title = LocalizationService.GetString("MenuSettings.Title");

        SaveCommand = new RelayCommand(() => SaveAsync());
        ResetCommand = new RelayCommand(() => ResetAsync());
        ExportCommand = new RelayCommand(() => ExportAsync());
        ImportCommand = new RelayCommand(() => ImportAsync());
    }

    public string? Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
                ApplyLanguageChange();
            }
        }
    }

    public ThemePreference Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                ApplyThemeChange();
                OnPropertyChanged(nameof(ThemeIndex));
            }
        }
    }

    public int ThemeIndex
    {
        get => _theme switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0
        };
        set
        {
            var normalizedTheme = value switch
            {
                1 => ThemePreference.Light,
                2 => ThemePreference.Dark,
                _ => ThemePreference.System
            };

            Theme = normalizedTheme;
        }
    }

    public int MapDefaultZoomLevel
    {
        get => _mapDefaultZoomLevel;
        set
        {
            var normalizedLevel = NormalizeMapZoomLevel(value);
            if (SetProperty(ref _mapDefaultZoomLevel, normalizedLevel))
            {
                ApplyMapZoomLevelChange();
                OnPropertyChanged(nameof(MapDefaultZoomLevelValue));
            }
        }
    }

    public double MapDefaultZoomLevelValue
    {
        get => MapDefaultZoomLevel;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            MapDefaultZoomLevel = (int)Math.Round(value);
        }
    }

    public MapTileSourceType MapTileSource
    {
        get => _mapTileSource;
        set
        {
            if (SetProperty(ref _mapTileSource, value))
            {
                ApplyMapTileSourceChange();
                OnPropertyChanged(nameof(MapTileSourceIndex));
            }
        }
    }

    public int MapTileSourceIndex
    {
        get => _mapTileSource == MapTileSourceType.EsriWorldImagery ? 1 : 0;
        set
        {
            MapTileSource = value == 1
                ? MapTileSourceType.EsriWorldImagery
                : MapTileSourceType.OpenStreetMap;
        }
    }

    public bool ShowImagesOnly
    {
        get => _showImagesOnly;
        set
        {
            if (SetProperty(ref _showImagesOnly, value))
            {
                ApplyShowImagesOnlyChange();
            }
        }
    }

    public bool ShowQuickStartOnStartup
    {
        get => _showQuickStartOnStartup;
        set
        {
            if (SetProperty(ref _showQuickStartOnStartup, value))
            {
                ApplyShowQuickStartOnStartupChange();
            }
        }
    }

    public string? LastFolderPath
    {
        get => _lastFolderPath;
        private set => SetProperty(ref _lastFolderPath, value);
    }

    public ICommand SaveCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand ImportCommand { get; }

    internal Task SaveIfDirtyAsync()
    {
        return _hasPendingChanges ? SaveAsync() : Task.CompletedTask;
    }

    protected override Task OnInitializeAsync()
    {
        RefreshFromCurrentState();
        _hasPendingChanges = false;
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        _languageChangeVersion++;

        try
        {
            var normalizedLanguage = NormalizeLanguageSetting(Language);
            _settingsCoordinator.ChangeTheme(Theme);
            _settingsCoordinator.ChangeMapZoomLevel(MapDefaultZoomLevel);
            _settingsCoordinator.ChangeMapTileSource(MapTileSource);
            _settingsCoordinator.ShowQuickStartOnStartup = ShowQuickStartOnStartup;

            if (_fileBrowserPaneViewModel.ShowImagesOnly != ShowImagesOnly)
            {
                _fileBrowserPaneViewModel.ShowImagesOnly = ShowImagesOnly;
            }

            var languageChanged = !string.Equals(
                _settingsCoordinator.LanguageOverride,
                normalizedLanguage,
                StringComparison.OrdinalIgnoreCase);
            await _settingsCoordinator.ChangeLanguageAsync(normalizedLanguage, showRestartPrompt: true).ConfigureAwait(true);
            if (!languageChanged)
            {
                await _settingsCoordinator.SaveAsync().ConfigureAwait(true);
            }

            RefreshFromCurrentState();
            _hasPendingChanges = false;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            AppLog.Error("Failed to save settings from SettingsPaneViewModel.", ex);
        }
    }

    private async Task ResetAsync()
    {
        _suppressDirtyTracking = true;
        try
        {
            Language = null;
            Theme = ThemePreference.System;
            MapDefaultZoomLevel = MapZoomLevelCatalog.Default;
            MapTileSource = MapTileSourceType.OpenStreetMap;
            ShowImagesOnly = true;
            ShowQuickStartOnStartup = false;
            LastFolderPath = _fileBrowserPaneViewModel.CurrentFolderPath;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        await SaveAsync().ConfigureAwait(true);
    }

    private async Task ExportAsync()
    {
        try
        {
            await _settingsCoordinator.ExportSettingsAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            AppLog.Error("Failed to export settings from SettingsPaneViewModel.", ex);
        }
    }

    private async Task ImportAsync()
    {
        try
        {
            await _settingsCoordinator.ImportSettingsAsync().ConfigureAwait(true);
            RefreshFromCurrentState();
            _hasPendingChanges = false;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            AppLog.Error("Failed to import settings from SettingsPaneViewModel.", ex);
        }
    }

    private void RefreshFromCurrentState()
    {
        _suppressDirtyTracking = true;
        try
        {
            Language = _shellViewModel.CurrentLanguage;
            Theme = _shellViewModel.CurrentTheme;
            MapDefaultZoomLevel = _shellViewModel.CurrentMapZoomLevel;
            MapTileSource = _shellViewModel.CurrentMapTileSource;
            ShowImagesOnly = _fileBrowserPaneViewModel.ShowImagesOnly;
            ShowQuickStartOnStartup = _settingsCoordinator.ShowQuickStartOnStartup;
            LastFolderPath = _fileBrowserPaneViewModel.CurrentFolderPath;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private void ApplyThemeChange()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        _settingsCoordinator.ChangeTheme(Theme);
        _hasPendingChanges = true;
    }

    private void ApplyMapZoomLevelChange()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        _settingsCoordinator.ChangeMapZoomLevel(MapDefaultZoomLevel);
        _hasPendingChanges = true;
    }

    private void ApplyMapTileSourceChange()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        _settingsCoordinator.ChangeMapTileSource(MapTileSource);
        _hasPendingChanges = true;
    }

    private void ApplyShowImagesOnlyChange()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        if (_fileBrowserPaneViewModel.ShowImagesOnly != ShowImagesOnly)
        {
            _fileBrowserPaneViewModel.ShowImagesOnly = ShowImagesOnly;
        }

        _hasPendingChanges = true;
    }

    private void ApplyShowQuickStartOnStartupChange()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        _settingsCoordinator.ShowQuickStartOnStartup = ShowQuickStartOnStartup;
        _settingsCoordinator.ScheduleSave();
        _hasPendingChanges = true;
    }

    private void ApplyLanguageChange()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        _hasPendingChanges = true;
        _languageChangeVersion++;
        _ = ApplyLanguageChangeAsync(Language, _languageChangeVersion);
    }

    private async Task ApplyLanguageChangeAsync(string? language, int changeVersion)
    {
        try
        {
            if (changeVersion != _languageChangeVersion)
            {
                return;
            }

            var normalizedLanguage = NormalizeLanguageSetting(language);
            await _settingsCoordinator.ChangeLanguageAsync(normalizedLanguage, showRestartPrompt: false).ConfigureAwait(true);

            if (changeVersion != _languageChangeVersion)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            AppLog.Error("Failed to apply language setting from SettingsPaneViewModel.", ex);
        }
    }

    private static string? NormalizeLanguageSetting(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        return language.Trim();
    }

    private static int NormalizeMapZoomLevel(int level)
    {
        if (MapZoomLevelCatalog.Options.Contains(level))
        {
            return level;
        }

        var nearest = MapZoomLevelCatalog.Options
            .OrderBy(candidate => Math.Abs(candidate - level))
            .FirstOrDefault();

        return nearest == 0 ? MapZoomLevelCatalog.Default : nearest;
    }
}
