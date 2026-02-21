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
    private const string SystemLanguageOptionValue = "system";

    private string? _language;
    private ThemePreference _theme = ThemePreference.System;
    private int _mapDefaultZoomLevel = MapZoomLevelCatalog.Default;
    private MapTileSourceType _mapTileSource = MapTileSourceType.OpenStreetMap;
    private bool _showImagesOnly = true;
    private bool _showQuickStartOnStartup;
    private string? _lastFolderPath;
    private PaneLayoutPreset _paneLayoutPreset = AppSettings.DefaultPaneLayoutPreset;
    private PaneViewType _paneRegion1View = AppSettings.DefaultPaneRegion1View;
    private PaneViewType _paneRegion2View = AppSettings.DefaultPaneRegion2View;
    private PaneViewType _paneRegion3View = AppSettings.DefaultPaneRegion3View;
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

    public PaneLayoutPreset SelectedPaneLayoutPreset
    {
        get => _paneLayoutPreset;
        set
        {
            if (SetProperty(ref _paneLayoutPreset, value))
            {
                ApplyPaneLayoutPresetChange();
                OnPropertyChanged(nameof(PaneLayoutPresetIndex));
                OnPropertyChanged(nameof(Region1Label));
                OnPropertyChanged(nameof(Region2Label));
                OnPropertyChanged(nameof(Region3Label));
            }
        }
    }

    public int PaneLayoutPresetIndex
    {
        get => (int)_paneLayoutPreset;
        set
        {
            SelectedPaneLayoutPreset = value switch
            {
                0 => PhotoGeoExplorer.Models.PaneLayoutPreset.LeftCenterRight,
                2 => PhotoGeoExplorer.Models.PaneLayoutPreset.LeftSplitAndRight,
                _ => PhotoGeoExplorer.Models.PaneLayoutPreset.LeftAndRightSplit
            };
        }
    }

    public PaneViewType PaneRegion1View
    {
        get => _paneRegion1View;
        set
        {
            var previous = _paneRegion1View;
            if (SetProperty(ref _paneRegion1View, value))
            {
                ApplyPaneRegionViewChange(regionIndex: 0, previousValue: previous);
                OnPropertyChanged(nameof(PaneRegion1ViewIndex));
            }
        }
    }

    public int PaneRegion1ViewIndex
    {
        get => ToPaneViewIndex(_paneRegion1View);
        set => PaneRegion1View = FromPaneViewIndex(value);
    }

    public PaneViewType PaneRegion2View
    {
        get => _paneRegion2View;
        set
        {
            var previous = _paneRegion2View;
            if (SetProperty(ref _paneRegion2View, value))
            {
                ApplyPaneRegionViewChange(regionIndex: 1, previousValue: previous);
                OnPropertyChanged(nameof(PaneRegion2ViewIndex));
            }
        }
    }

    public int PaneRegion2ViewIndex
    {
        get => ToPaneViewIndex(_paneRegion2View);
        set => PaneRegion2View = FromPaneViewIndex(value);
    }

    public PaneViewType PaneRegion3View
    {
        get => _paneRegion3View;
        set
        {
            var previous = _paneRegion3View;
            if (SetProperty(ref _paneRegion3View, value))
            {
                ApplyPaneRegionViewChange(regionIndex: 2, previousValue: previous);
                OnPropertyChanged(nameof(PaneRegion3ViewIndex));
            }
        }
    }

    public int PaneRegion3ViewIndex
    {
        get => ToPaneViewIndex(_paneRegion3View);
        set => PaneRegion3View = FromPaneViewIndex(value);
    }

    public string Region1Label => GetRegionLabel(regionIndex: 0);

    public string Region2Label => GetRegionLabel(regionIndex: 1);

    public string Region3Label => GetRegionLabel(regionIndex: 2);

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
            _settingsCoordinator.ChangePaneLayout(
                SelectedPaneLayoutPreset,
                PaneRegion1View,
                PaneRegion2View,
                PaneRegion3View);

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
            Language = SystemLanguageOptionValue;
            Theme = ThemePreference.System;
            MapDefaultZoomLevel = MapZoomLevelCatalog.Default;
            MapTileSource = MapTileSourceType.OpenStreetMap;
            ShowImagesOnly = true;
            ShowQuickStartOnStartup = false;
            SelectedPaneLayoutPreset = AppSettings.DefaultPaneLayoutPreset;
            PaneRegion1View = AppSettings.DefaultPaneRegion1View;
            PaneRegion2View = AppSettings.DefaultPaneRegion2View;
            PaneRegion3View = AppSettings.DefaultPaneRegion3View;
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
            Language = _shellViewModel.CurrentLanguage ?? SystemLanguageOptionValue;
            Theme = _shellViewModel.CurrentTheme;
            MapDefaultZoomLevel = _shellViewModel.CurrentMapZoomLevel;
            MapTileSource = _shellViewModel.CurrentMapTileSource;
            ShowImagesOnly = _fileBrowserPaneViewModel.ShowImagesOnly;
            ShowQuickStartOnStartup = _settingsCoordinator.ShowQuickStartOnStartup;
            SelectedPaneLayoutPreset = _settingsCoordinator.PaneLayoutPreset;
            PaneRegion1View = _settingsCoordinator.PaneRegion1View;
            PaneRegion2View = _settingsCoordinator.PaneRegion2View;
            PaneRegion3View = _settingsCoordinator.PaneRegion3View;
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

    private void ApplyPaneLayoutPresetChange()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        ApplyPaneLayoutChange();
    }

    private void ApplyPaneRegionViewChange(int regionIndex, PaneViewType previousValue)
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        var selected = regionIndex switch
        {
            0 => PaneRegion1View,
            1 => PaneRegion2View,
            _ => PaneRegion3View
        };

        var duplicateRegionIndex = regionIndex switch
        {
            0 when PaneRegion2View == selected => 1,
            0 when PaneRegion3View == selected => 2,
            1 when PaneRegion1View == selected => 0,
            1 when PaneRegion3View == selected => 2,
            2 when PaneRegion1View == selected => 0,
            2 when PaneRegion2View == selected => 1,
            _ => -1
        };

        if (duplicateRegionIndex >= 0)
        {
            ReplacePaneRegionView(duplicateRegionIndex, previousValue);
        }

        ApplyPaneLayoutChange();
    }

    private void ApplyPaneLayoutChange()
    {
        _settingsCoordinator.ChangePaneLayout(
            SelectedPaneLayoutPreset,
            PaneRegion1View,
            PaneRegion2View,
            PaneRegion3View);
        _hasPendingChanges = true;
    }

    private void ReplacePaneRegionView(int regionIndex, PaneViewType value)
    {
        _suppressDirtyTracking = true;
        try
        {
            switch (regionIndex)
            {
                case 0:
                    PaneRegion1View = value;
                    break;
                case 1:
                    PaneRegion2View = value;
                    break;
                default:
                    PaneRegion3View = value;
                    break;
            }
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private string GetRegionLabel(int regionIndex)
    {
        var key = SelectedPaneLayoutPreset switch
        {
            PhotoGeoExplorer.Models.PaneLayoutPreset.LeftCenterRight => regionIndex switch
            {
                0 => "SettingsPaneLayoutRegionLeft",
                1 => "SettingsPaneLayoutRegionCenter",
                _ => "SettingsPaneLayoutRegionRight"
            },
            PhotoGeoExplorer.Models.PaneLayoutPreset.LeftSplitAndRight => regionIndex switch
            {
                0 => "SettingsPaneLayoutRegionTopLeft",
                1 => "SettingsPaneLayoutRegionBottomLeft",
                _ => "SettingsPaneLayoutRegionRight"
            },
            _ => regionIndex switch
            {
                0 => "SettingsPaneLayoutRegionLeft",
                1 => "SettingsPaneLayoutRegionTopRight",
                _ => "SettingsPaneLayoutRegionBottomRight"
            }
        };

        return LocalizationService.GetString(key);
    }

    private static int ToPaneViewIndex(PaneViewType value)
    {
        return value switch
        {
            PaneViewType.Preview => 1,
            PaneViewType.Map => 2,
            _ => 0
        };
    }

    private static PaneViewType FromPaneViewIndex(int value)
    {
        return value switch
        {
            1 => PaneViewType.Preview,
            2 => PaneViewType.Map,
            _ => PaneViewType.File
        };
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

        var trimmed = language.Trim();
        return string.Equals(trimmed, SystemLanguageOptionValue, StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
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
