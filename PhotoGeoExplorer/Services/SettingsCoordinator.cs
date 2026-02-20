using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.ViewModels;
using Windows.Storage.Pickers;

namespace PhotoGeoExplorer.Services;

internal sealed class SettingsCoordinator : ISettingsCoordinator
{
    private static readonly IReadOnlyList<string> JsonFileTypeFilter = new[] { ".json" };
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> JsonFileTypeChoices =
        new Dictionary<string, IReadOnlyList<string>>
        {
            { "JSON", JsonFileTypeFilter }
        };
    private readonly SettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly Action<ElementTheme> _setRequestedTheme;
    private readonly FileBrowserPaneViewModel _fileBrowserPaneViewModel;
    private readonly MapPaneViewModel _mapPaneViewModel;
    private readonly MainViewModel _shellViewModel;
    private readonly Func<string?> _startupFilePathProvider;
    private CancellationTokenSource? _settingsCts;
    private bool _isApplyingSettings;
    private string? _languageOverride;
    private ThemePreference _themePreference = ThemePreference.System;
    private int _mapDefaultZoomLevel = MapZoomLevelCatalog.Default;
    private MapTileSourceType _mapTileSource = MapTileSourceType.OpenStreetMap;
    private bool _showQuickStartOnStartup;
    private bool _disposed;

    public SettingsCoordinator(
        SettingsService settingsService,
        IDialogService dialogService,
        Action<ElementTheme> setRequestedTheme,
        FileBrowserPaneViewModel fileBrowserPaneViewModel,
        MapPaneViewModel mapPaneViewModel,
        MainViewModel shellViewModel,
        Func<string?> startupFilePathProvider)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _setRequestedTheme = setRequestedTheme ?? throw new ArgumentNullException(nameof(setRequestedTheme));
        _fileBrowserPaneViewModel = fileBrowserPaneViewModel ?? throw new ArgumentNullException(nameof(fileBrowserPaneViewModel));
        _mapPaneViewModel = mapPaneViewModel ?? throw new ArgumentNullException(nameof(mapPaneViewModel));
        _shellViewModel = shellViewModel ?? throw new ArgumentNullException(nameof(shellViewModel));
        _startupFilePathProvider = startupFilePathProvider ?? throw new ArgumentNullException(nameof(startupFilePathProvider));
        SettingsFileExistsAtStartup = _settingsService.SettingsFileExists();
        UpdateShellSettingsState();
        _fileBrowserPaneViewModel.PropertyChanged += OnFileBrowserPanePropertyChanged;
    }

    public bool SettingsFileExistsAtStartup { get; }

    public string? LanguageOverride => _languageOverride;

    public bool ShowQuickStartOnStartup
    {
        get => _showQuickStartOnStartup;
        set => _showQuickStartOnStartup = value;
    }

    public async Task LoadAsync()
    {
        _isApplyingSettings = true;
        try
        {
            var settings = await _settingsService.LoadAsync().ConfigureAwait(true);
            await ApplySettingsAsync(settings).ConfigureAwait(true);
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    public void ScheduleSave()
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var previous = _settingsCts;
        _settingsCts = new CancellationTokenSource();
        previous?.Cancel();
        previous?.Dispose();

        var token = _settingsCts.Token;
        _ = SaveSettingsDelayedAsync(token);
    }

    public Task SaveAsync()
    {
        CancelPendingSave();
        var settings = BuildSettingsSnapshot();
        return _settingsService.SaveAsync(settings);
    }

    public Task ChangeLanguageAsync(string? languageTag, bool showRestartPrompt)
    {
        return ApplyLanguageSettingAsync(languageTag, showRestartPrompt);
    }

    public void ChangeTheme(ThemePreference preference)
    {
        ApplyThemePreference(preference, saveSettings: true);
    }

    public void ChangeMapZoomLevel(int level)
    {
        var normalized = NormalizeMapZoomLevel(level);
        var changed = _mapDefaultZoomLevel != normalized;
        _mapDefaultZoomLevel = normalized;
        _mapPaneViewModel.MapDefaultZoomLevel = normalized;
        UpdateShellSettingsState();

        if (changed && !_isApplyingSettings)
        {
            ScheduleSave();
        }
    }

    public void ChangeMapTileSource(MapTileSourceType sourceType)
    {
        if (!Enum.IsDefined(sourceType))
        {
            return;
        }

        var changed = _mapTileSource != sourceType;
        _mapTileSource = sourceType;
        _mapPaneViewModel.SwitchTileSource(sourceType);
        UpdateShellSettingsState();

        if (changed && !_isApplyingSettings)
        {
            ScheduleSave();
        }
    }

    public async Task ExportSettingsAsync()
    {
        var file = await _dialogService.ShowSaveFilePickerAsync(
            PickerLocationId.DocumentsLibrary,
            "PhotoGeoExplorer.settings",
            JsonFileTypeChoices).ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        var settings = BuildSettingsSnapshot();
        await SettingsService.ExportAsync(settings, file.Path).ConfigureAwait(true);
    }

    public async Task ImportSettingsAsync()
    {
        var file = await _dialogService.ShowFilePickerAsync(
            PickerLocationId.DocumentsLibrary,
            JsonFileTypeFilter).ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        var settings = await SettingsService.ImportAsync(file.Path).ConfigureAwait(true);
        if (settings is null)
        {
            await _dialogService.ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.ImportFailed.Title"),
                LocalizationService.GetString("Dialog.ImportFailed.Detail")).ConfigureAwait(true);
            return;
        }

        _isApplyingSettings = true;
        try
        {
            await ApplySettingsAsync(settings, showLanguagePrompt: true).ConfigureAwait(true);
        }
        finally
        {
            _isApplyingSettings = false;
        }

        await _settingsService.SaveAsync(settings).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fileBrowserPaneViewModel.PropertyChanged -= OnFileBrowserPanePropertyChanged;
        _settingsCts?.Cancel();
        _settingsCts?.Dispose();
        _settingsCts = null;
        GC.SuppressFinalize(this);
    }

    internal static string? NormalizeLanguageSetting(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return null;
        }

        var trimmed = languageTag.Trim();
        if (string.Equals(trimmed, "system", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(trimmed, "ja", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "ja-jp", StringComparison.OrdinalIgnoreCase))
        {
            return "ja-JP";
        }

        if (string.Equals(trimmed, "en", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "en-us", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        return trimmed;
    }

    internal static int NormalizeMapZoomLevel(int level)
    {
        if (MapZoomLevelCatalog.Options.Contains(level))
        {
            return level;
        }

        return MapZoomLevelCatalog.Default;
    }

    internal static string? FindValidAncestorPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var current = Path.GetFullPath(path);

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current))
                {
                    return current;
                }

                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }
        catch (Exception ex) when (ex is ArgumentException
            or PathTooLongException
            or System.Security.SecurityException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            AppLog.Error($"Failed to find valid ancestor path for '{path}'", ex);
        }

        return null;
    }

    private async Task ApplySettingsAsync(AppSettings settings, bool showLanguagePrompt = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await ApplyLanguageSettingAsync(settings.Language, showLanguagePrompt).ConfigureAwait(true);
        ApplyThemePreference(settings.Theme, saveSettings: false);

        _mapDefaultZoomLevel = NormalizeMapZoomLevel(settings.MapDefaultZoomLevel);
        _mapPaneViewModel.MapDefaultZoomLevel = _mapDefaultZoomLevel;
        _showQuickStartOnStartup = settings.ShowQuickStartOnStartup;

        var savedTileSource = Enum.IsDefined(settings.MapTileSource) ? settings.MapTileSource : MapTileSourceType.OpenStreetMap;
        if (savedTileSource != _mapTileSource)
        {
            _mapPaneViewModel.SwitchTileSource(savedTileSource);
            _mapTileSource = savedTileSource;
        }
        else
        {
            _mapTileSource = savedTileSource;
        }

        _fileBrowserPaneViewModel.ShowImagesOnly = settings.ShowImagesOnly;
        _fileBrowserPaneViewModel.FileViewMode = Enum.IsDefined<FileViewMode>(settings.FileViewMode)
            ? settings.FileViewMode
            : FileViewMode.Details;

        UpdateShellSettingsState();

        var startupFilePath = _startupFilePathProvider();
        if (!string.IsNullOrWhiteSpace(startupFilePath) && File.Exists(startupFilePath))
        {
            AppLog.Info("Skipping LastFolderPath restoration because a valid startup file path is specified.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.LastFolderPath))
        {
            var validPath = FindValidAncestorPath(settings.LastFolderPath);
            if (!string.IsNullOrWhiteSpace(validPath))
            {
                await _fileBrowserPaneViewModel.LoadFolderAsync(validPath).ConfigureAwait(true);

                if (!string.Equals(validPath, settings.LastFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Info($"LastFolderPath recovered from '{settings.LastFolderPath}' to ancestor '{validPath}'");
                    settings.LastFolderPath = validPath;
                    await _settingsService.SaveAsync(settings).ConfigureAwait(true);
                }
            }
        }
    }

    private async Task ApplyLanguageSettingAsync(string? languageTag, bool showRestartPrompt)
    {
        var normalized = NormalizeLanguageSetting(languageTag);
        var changed = !string.Equals(_languageOverride, normalized, StringComparison.OrdinalIgnoreCase);
        _languageOverride = normalized;
        ApplyLanguageOverride(normalized);
        UpdateShellSettingsState();

        if (!showRestartPrompt || !changed)
        {
            return;
        }

        if (!_isApplyingSettings)
        {
            await SaveAsync().ConfigureAwait(true);
        }

        await _dialogService.ShowMessageDialogAsync(
            LocalizationService.GetString("Dialog.LanguageChanged.Title"),
            LocalizationService.GetString("Dialog.LanguageChanged.Detail")).ConfigureAwait(true);
    }

    private void ApplyThemePreference(ThemePreference preference, bool saveSettings)
    {
        var changed = _themePreference != preference;
        _themePreference = preference;
        ApplyTheme(preference);
        UpdateShellSettingsState();

        if (saveSettings && changed && !_isApplyingSettings)
        {
            ScheduleSave();
        }
    }

    private void ApplyTheme(ThemePreference preference)
    {
        var requestedTheme = preference switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        _setRequestedTheme(requestedTheme);
    }

    private void OnFileBrowserPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileBrowserPaneViewModel.ShowImagesOnly)
            or nameof(FileBrowserPaneViewModel.FileViewMode)
            or nameof(FileBrowserPaneViewModel.CurrentFolderPath))
        {
            ScheduleSave();
        }
    }

    private static void ApplyLanguageOverride(string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = normalized;
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to apply language override.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to apply language override.", ex);
        }
    }

    private async Task SaveSettingsDelayedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SaveAsync().ConfigureAwait(true);
    }

    private void CancelPendingSave()
    {
        var pending = Interlocked.Exchange(ref _settingsCts, null);
        pending?.Cancel();
        pending?.Dispose();
    }

    private AppSettings BuildSettingsSnapshot()
    {
        return new AppSettings
        {
            LastFolderPath = _fileBrowserPaneViewModel.CurrentFolderPath,
            ShowImagesOnly = _fileBrowserPaneViewModel.ShowImagesOnly,
            FileViewMode = _fileBrowserPaneViewModel.FileViewMode,
            Language = _languageOverride,
            Theme = _themePreference,
            MapDefaultZoomLevel = _mapDefaultZoomLevel,
            MapTileSource = _mapTileSource,
            ShowQuickStartOnStartup = _showQuickStartOnStartup
        };
    }

    private void UpdateShellSettingsState()
    {
        _shellViewModel.ApplySettingsState(
            _languageOverride,
            _themePreference,
            _mapDefaultZoomLevel,
            _mapTileSource);
    }
}
