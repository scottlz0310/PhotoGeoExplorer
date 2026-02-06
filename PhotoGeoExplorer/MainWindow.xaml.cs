using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.Globalization;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Panes.Preview;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using Windows.Graphics;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed partial class MainWindow : Window, IDisposable
{
    private const int DefaultMapZoomLevel = 14;
    private static readonly int[] MapZoomLevelOptions = { 8, 10, 12, 14, 16, 18 };
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly FileBrowserPaneViewModel _fileBrowserPaneViewModel;
    private readonly PreviewPaneViewModel _previewPaneViewModel;
    private readonly MapPaneViewModel _mapPaneViewModel;
    private bool _layoutStored;
    private bool _mapInitialized;
    private bool _previewMaximized;
    private bool _windowSized;
    private bool _windowIconSet;
    private CancellationTokenSource? _settingsCts;
    private GridLength _storedDetailWidth;
    private GridLength _storedFileBrowserWidth;
    private GridLength _storedPreviewRowHeight;
    private GridLength _storedMapRowHeight;
    private GridLength _storedMapSplitterHeight;
    private GridLength _storedSplitterWidth;
    private double _storedMapRowMinHeight;
    private bool _isApplyingSettings;
    private string? _languageOverride;
    private string? _startupFilePath;
    private ThemePreference _themePreference = ThemePreference.System;
    private int _mapDefaultZoomLevel = DefaultMapZoomLevel;
    private MapTileSourceType _mapTileSource = MapTileSourceType.OpenStreetMap;
    private Window? _helpHtmlWindow;
    private WebView2? _helpHtmlWebView;
    private readonly bool _settingsFileExistsAtStartup;
    private bool _showQuickStartOnStartup;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new FileSystemService());
        _settingsService = new SettingsService();
        _fileBrowserPaneViewModel = new FileBrowserPaneViewModel(new FileBrowserPaneService(), _viewModel.WorkspaceState);
        _previewPaneViewModel = new PreviewPaneViewModel(new PreviewPaneService(), _viewModel.WorkspaceState);
        _mapPaneViewModel = new MapPaneViewModel();
        _settingsFileExistsAtStartup = _settingsService.SettingsFileExists();
        RootGrid.DataContext = _viewModel;
        FileBrowserPaneControl.DataContext = _fileBrowserPaneViewModel;
        FileBrowserPaneControl.HostWindow = this;
        FileBrowserPaneControl.EditExifRequested += OnEditExifRequested;
        PreviewPaneControl.DataContext = _previewPaneViewModel;
        PreviewPaneControl.MaximizeChanged += OnPreviewMaximizeChanged;
        MapPaneControl.DataContext = _mapPaneViewModel;
        MapPaneControl.PhotoFocusRequested += OnMapPanePhotoFocusRequested;
        MapPaneControl.RectangleSelectionCompleted += OnMapPaneRectangleSelectionCompleted;
        MapPaneControl.NotificationRequested += OnMapPaneNotificationRequested;
        Title = LocalizationService.GetString("MainWindow.Title");
        AppLog.Info("MainWindow constructed.");
        Activated += OnActivated;
        Closed += OnClosed;
        _fileBrowserPaneViewModel.PropertyChanged += OnFileBrowserPanePropertyChanged;
        _viewModel.WorkspaceState.PropertyChanged += OnWorkspaceStatePropertyChanged;
    }

    private void OnPreviewMaximizeChanged(object? sender, bool maximize)
    {
        TogglePreviewMaximize(maximize);
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_mapInitialized)
        {
            return;
        }

        EnsureWindowSize();
        EnsureWindowIcon();
        _mapInitialized = true;
        AppLog.Info("MainWindow activated.");

        // MapPaneViewModel を初期化
        await _mapPaneViewModel.InitializeAsync().ConfigureAwait(true);

        await LoadSettingsAsync().ConfigureAwait(true);
        await ApplyStartupFolderOverrideAsync().ConfigureAwait(true);
        await ApplyStartupFileActivationAsync().ConfigureAwait(true);
        await _fileBrowserPaneViewModel.InitializeAsync().ConfigureAwait(true);

        // XamlRoot が確定するまでワンテンポ遅らせてからダイアログを表示
        DispatcherQueue.TryEnqueue(async () =>
        {
            await ShowQuickStartIfNeededAsync().ConfigureAwait(true);
        });

    }

    private void EnsureWindowSize()
    {
        if (_windowSized)
        {
            return;
        }

        _windowSized = true;

        try
        {
            AppWindow.Resize(new SizeInt32(1200, 800));
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
    }

    private void EnsureWindowIcon()
    {
        if (_windowIconSet)
        {
            return;
        }

        _windowIconSet = true;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (!File.Exists(iconPath))
        {
            AppLog.Error($"Window icon not found: {iconPath}");
            return;
        }

        try
        {
            AppWindow.SetIcon(iconPath);
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to set window icon.", ex);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to set window icon.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to set window icon.", ex);
        }
    }


    private void OnMapTileSourceMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item || item.Tag is not string tag)
        {
            return;
        }

        if (!Enum.TryParse(tag, ignoreCase: true, out MapTileSourceType sourceType))
        {
            return;
        }

        _mapPaneViewModel.SwitchTileSource(sourceType);
        _mapTileSource = sourceType;
        UpdateMapTileSourceMenuChecks(sourceType);
        ScheduleSettingsSave();
    }

    private void UpdateMapTileSourceMenuChecks(MapTileSourceType source)
    {
        if (MapTileSourceOsmMenuItem is not null)
        {
            MapTileSourceOsmMenuItem.IsChecked = source == MapTileSourceType.OpenStreetMap;
        }

        if (MapTileSourceEsriMenuItem is not null)
        {
            MapTileSourceEsriMenuItem.IsChecked = source == MapTileSourceType.EsriWorldImagery;
        }
    }

    private async Task LoadSettingsAsync()
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

    private async Task ApplyStartupFolderOverrideAsync()
    {
        var folderPath = GetStartupFolderOverride();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            AppLog.Error($"Startup folder not found: {folderPath}");
            return;
        }

        await _fileBrowserPaneViewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);
    }

    private static string? GetStartupFolderOverride()
    {
        var envPath = Environment.GetEnvironmentVariable("PHOTO_GEO_EXPLORER_E2E_FOLDER");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath;
        }

        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryGetOptionValue(arg, "--folder", out var value)
                || TryGetOptionValue(arg, "/folder", out value)
                || TryGetOptionValue(arg, "--e2e-folder", out value))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                if (i + 1 < args.Length)
                {
                    return args[i + 1].Trim('"');
                }
            }
        }

        return null;
    }

    public void SetStartupFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _startupFilePath = filePath;
    }

    private async Task ApplyStartupFileActivationAsync()
    {
        if (string.IsNullOrWhiteSpace(_startupFilePath))
        {
            return;
        }

        var filePath = _startupFilePath;
        _startupFilePath = null;

        if (!File.Exists(filePath))
        {
            AppLog.Error($"Startup file not found: {filePath}");
            return;
        }

        var folderPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            AppLog.Error($"Failed to resolve startup file folder: {filePath}");
            return;
        }

        await _fileBrowserPaneViewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);

        var item = _fileBrowserPaneViewModel.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item is null || item.IsFolder)
        {
            AppLog.Error($"Startup file not listed in folder view: {filePath}");
            return;
        }

        _fileBrowserPaneViewModel.UpdateSelection(new[] { item });
        _fileBrowserPaneViewModel.SelectedItem = item;
    }

    private static bool TryGetOptionValue(string argument, string option, out string? value)
    {
        value = null;
        if (!argument.StartsWith(option, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (argument.Length == option.Length)
        {
            return true;
        }

        var separator = argument[option.Length];
        if (separator is not '=' and not ':')
        {
            return false;
        }

        value = argument[(option.Length + 1)..].Trim('"');
        return true;
    }

    private async Task ApplySettingsAsync(AppSettings settings, bool showLanguagePrompt = false)
    {
        if (settings is null)
        {
            return;
        }

        await ApplyLanguageSettingAsync(settings.Language, showLanguagePrompt).ConfigureAwait(true);
        ApplyThemePreference(settings.Theme, saveSettings: false);

        // MapPaneViewModel に設定を適用
        _mapDefaultZoomLevel = NormalizeMapZoomLevel(settings.MapDefaultZoomLevel);
        _mapPaneViewModel.MapDefaultZoomLevel = _mapDefaultZoomLevel;
        _showQuickStartOnStartup = settings.ShowQuickStartOnStartup;
        UpdateMapZoomMenuChecks(_mapDefaultZoomLevel);

        var savedTileSource = Enum.IsDefined(settings.MapTileSource) ? settings.MapTileSource : MapTileSourceType.OpenStreetMap;
        if (savedTileSource != _mapTileSource)
        {
            // 設定で保存されたタイルソースが現在のものと異なる場合、切り替えを実行
            _mapPaneViewModel.SwitchTileSource(savedTileSource);
            _mapTileSource = savedTileSource;
        }
        else
        {
            _mapTileSource = savedTileSource;
        }
        UpdateMapTileSourceMenuChecks(_mapTileSource);

        _fileBrowserPaneViewModel.ShowImagesOnly = settings.ShowImagesOnly;
        _fileBrowserPaneViewModel.FileViewMode = Enum.IsDefined<FileViewMode>(settings.FileViewMode)
            ? settings.FileViewMode
            : FileViewMode.Details;

        // Skip LastFolderPath restoration when file activation path is specified.
        // File activation should take priority over saved folder restoration.
        if (!string.IsNullOrWhiteSpace(_startupFilePath) && File.Exists(_startupFilePath))
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

                    // Update settings to persist the recovered path for next startup
                    settings.LastFolderPath = validPath;
                    await _settingsService.SaveAsync(settings).ConfigureAwait(true);
                }
            }
        }
    }

    private static string? FindValidAncestorPath(string? path)
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

    private void ScheduleSettingsSave()
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

        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private Task SaveSettingsAsync()
    {
        var settings = BuildSettingsSnapshot();
        return _settingsService.SaveAsync(settings);
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

    private void OnFileBrowserPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 地図の更新は WorkspaceState 経由で MapPaneViewModel が行うため、ここでは不要

        if (e.PropertyName is nameof(FileBrowserPaneViewModel.ShowImagesOnly)
            or nameof(FileBrowserPaneViewModel.FileViewMode)
            or nameof(FileBrowserPaneViewModel.CurrentFolderPath))
        {
            ScheduleSettingsSave();
        }
    }

    private async void OnWorkspaceStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(State.WorkspaceState.SelectedPhotos))
        {
            var selectedPhotos = _viewModel.WorkspaceState.SelectedPhotos ?? Array.Empty<PhotoListItem>();
            await _mapPaneViewModel.UpdateMarkersFromSelectionAsync(selectedPhotos).ConfigureAwait(true);
        }
    }


    private void TogglePreviewMaximize(bool maximize)
    {
        if (maximize == _previewMaximized)
        {
            return;
        }

        if (!_layoutStored)
        {
            _storedFileBrowserWidth = FileBrowserColumn.Width;
            _storedSplitterWidth = SplitterColumn.Width;
            _storedDetailWidth = DetailColumn.Width;
            _storedPreviewRowHeight = PreviewRow.Height;
            _storedMapRowHeight = MapRow.Height;
            _storedMapSplitterHeight = MapSplitterRow.Height;
            _storedMapRowMinHeight = MapRow.MinHeight;
            _layoutStored = true;
        }

        _previewMaximized = maximize;
        if (maximize)
        {
            FileBrowserColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            PreviewRow.Height = new GridLength(1, GridUnitType.Star);
            MapRow.Height = new GridLength(0);
            MapSplitterRow.Height = new GridLength(0);
            MapRow.MinHeight = 0;
            FileBrowserPane.Visibility = Visibility.Collapsed;
            MapPane.Visibility = Visibility.Collapsed;
            MainSplitter.Visibility = Visibility.Collapsed;
            MapRowSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            FileBrowserColumn.Width = _storedFileBrowserWidth;
            SplitterColumn.Width = _storedSplitterWidth;
            DetailColumn.Width = _storedDetailWidth;
            PreviewRow.Height = _storedPreviewRowHeight;
            MapRow.Height = _storedMapRowHeight;
            MapSplitterRow.Height = _storedMapSplitterHeight;
            MapRow.MinHeight = _storedMapRowMinHeight;
            FileBrowserPane.Visibility = Visibility.Visible;
            MapPane.Visibility = Visibility.Visible;
            MainSplitter.Visibility = Visibility.Visible;
            MapRowSplitter.Visibility = Visibility.Visible;
        }

        // PreviewPaneViewModel の FitToWindow を設定
        _previewPaneViewModel.FitToWindow = true;
    }

    private void OnMainSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        var totalWidth = MainContentGrid.ActualWidth - SplitterColumn.ActualWidth;
        if (totalWidth <= 0)
        {
            return;
        }

        const double minLeft = 220;
        const double minRight = 320;
        var targetLeft = FileBrowserColumn.ActualWidth + e.HorizontalChange;
        var maxLeft = totalWidth - minRight;
        var clampedLeft = Math.Clamp(targetLeft, minLeft, maxLeft);

        FileBrowserColumn.Width = new GridLength(clampedLeft, GridUnitType.Pixel);
        DetailColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    private void OnMapSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        var totalHeight = DetailPane.ActualHeight - MapSplitterRow.ActualHeight;
        if (totalHeight <= 0)
        {
            return;
        }

        const double minPreview = 200;
        const double minMap = 200;
        var targetPreview = PreviewRow.ActualHeight + e.VerticalChange;
        var maxPreview = totalHeight - minMap;
        var clampedPreview = Math.Clamp(targetPreview, minPreview, maxPreview);

        PreviewRow.Height = new GridLength(clampedPreview, GridUnitType.Pixel);
        MapRow.Height = new GridLength(1, GridUnitType.Star);
    }

    private async void OnNavigateHomeClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.NavigateHomeAsync().ConfigureAwait(true);
    }

    private async void OnNavigateBackClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await _fileBrowserPaneViewModel.NavigateBackAsync().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Navigation back failed", ex);
            // ユーザーへの通知は ViewModel 内で SetStatus により既に行われている
        }
        catch (DirectoryNotFoundException ex)
        {
            AppLog.Error("Navigation back failed", ex);
            // ユーザーへの通知は ViewModel 内で SetStatus により既に行われている
        }
        catch (PathTooLongException ex)
        {
            AppLog.Error("Navigation back failed", ex);
            // ユーザーへの通知は ViewModel 内で SetStatus により既に行われている
        }
        catch (IOException ex)
        {
            AppLog.Error("Navigation back failed", ex);
            // ユーザーへの通知は ViewModel 内で SetStatus により既に行われている
        }
    }

    private async void OnNavigateForwardClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await _fileBrowserPaneViewModel.NavigateForwardAsync().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Navigation forward failed", ex);
            // ユーザーへの通知は ViewModel 内で SetStatus により既に行われている
        }
        catch (DirectoryNotFoundException ex)
        {
            AppLog.Error("Navigation forward failed", ex);
            // ユーザーへの通知は ViewModel 内で SetStatus により既に行われている
        }
        catch (PathTooLongException ex)
        {
            AppLog.Error("Navigation forward failed", ex);
            // ユーザーへの通知は ViewModel 内で SetStatus により既に行われている
        }
        catch (IOException ex)
        {
            AppLog.Error("Navigation forward failed", ex);
            // ユーザーへの通知は ViewModel 内で SetStatus により既に行われている
        }
    }

    private async void OnNavigateUpClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.NavigateUpAsync().ConfigureAwait(true);
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.OpenFolderAsync().ConfigureAwait(true);
    }

    private async void OnOpenSettingsPaneClicked(object sender, RoutedEventArgs e)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return;
        }

        if (Application.Current.Resources["SettingsPaneTemplate"] is not DataTemplate template)
        {
            AppLog.Error("Settings pane template not found.");
            return;
        }

        var viewModel = new SettingsPaneViewModel();
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.IsActive = true;

        var content = new ContentControl
        {
            Content = viewModel,
            ContentTemplate = template
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("MenuSettingsOpenPaneDev.Text"),
            Content = content,
            CloseButtonText = LocalizationService.GetString("Common.Ok"),
            XamlRoot = RootGrid.XamlRoot
        };

        try
        {
            await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        }
        finally
        {
            viewModel.IsActive = false;
            viewModel.Cleanup();
        }
    }

    private async void OnResetFiltersClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.ResetFiltersAsync().ConfigureAwait(true);
    }

    private async void OnNotificationActionClicked(object sender, RoutedEventArgs e)
    {
        var url = _viewModel.NotificationActionUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private async void OnLanguageMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item)
        {
            return;
        }

        var languageTag = item.Tag as string;
        await ApplyLanguageSettingAsync(languageTag, showRestartPrompt: true).ConfigureAwait(true);
    }

    private void OnThemeMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item || item.Tag is not string tag)
        {
            return;
        }

        if (!Enum.TryParse(tag, ignoreCase: true, out ThemePreference preference))
        {
            return;
        }

        ApplyThemePreference(preference, saveSettings: true);
    }

    private void OnMapZoomMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item || item.Tag is not string tag)
        {
            return;
        }

        if (!int.TryParse(tag, out var level))
        {
            return;
        }

        _mapDefaultZoomLevel = NormalizeMapZoomLevel(level);
        _mapPaneViewModel.MapDefaultZoomLevel = _mapDefaultZoomLevel;
        UpdateMapZoomMenuChecks(_mapDefaultZoomLevel);
        ScheduleSettingsSave();
    }

    private async void OnExportSettingsClicked(object sender, RoutedEventArgs e)
    {
        var file = await PickSettingsSaveFileAsync().ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        var settings = BuildSettingsSnapshot();
        await SettingsService.ExportAsync(settings, file.Path).ConfigureAwait(true);
    }

    private async void OnImportSettingsClicked(object sender, RoutedEventArgs e)
    {
        var file = await PickSettingsFileAsync().ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        var settings = await SettingsService.ImportAsync(file.Path).ConfigureAwait(true);
        if (settings is null)
        {
            await ShowMessageDialogAsync(
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

    private async Task<StorageFile?> PickSettingsFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        try
        {
            return await picker.PickSingleFileAsync().AsTask().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Settings import picker failed.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Settings import picker failed.", ex);
        }

        return null;
    }

    private async Task<StorageFile?> PickSettingsSaveFileAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "PhotoGeoExplorer.settings"
        };
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        try
        {
            return await picker.PickSaveFileAsync().AsTask().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Settings export picker failed.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Settings export picker failed.", ex);
        }

        return null;
    }

    private async void OnToggleImagesOnlyClicked(object sender, RoutedEventArgs e)
    {
        _fileBrowserPaneViewModel.ShowImagesOnly = !_fileBrowserPaneViewModel.ShowImagesOnly;
        await _fileBrowserPaneViewModel.RefreshAsync().ConfigureAwait(true);
    }

    private void OnViewModeMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string tag)
        {
            return;
        }

        if (Enum.TryParse(tag, out FileViewMode mode))
        {
            _fileBrowserPaneViewModel.FileViewMode = mode;
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        CloseHelpHtmlWindow();

        // WorkspaceState イベントをアンサブスクライブ
        _viewModel.WorkspaceState.PropertyChanged -= OnWorkspaceStatePropertyChanged;
        _fileBrowserPaneViewModel.PropertyChanged -= OnFileBrowserPanePropertyChanged;

        // MapPaneViewModel のクリーンアップ
        _mapPaneViewModel.Cleanup();

        _settingsCts?.Cancel();
        _settingsCts?.Dispose();
        _settingsCts = null;

        if (PreviewPaneControl is not null)
        {
            PreviewPaneControl.MaximizeChanged -= OnPreviewMaximizeChanged;
            PreviewPaneControl.DataContext = null;
        }

        if (FileBrowserPaneControl is not null)
        {
            FileBrowserPaneControl.EditExifRequested -= OnEditExifRequested;
            FileBrowserPaneControl.DataContext = null;
            FileBrowserPaneControl.HostWindow = null;
        }

        if (MapPaneControl is not null)
        {
            MapPaneControl.PhotoFocusRequested -= OnMapPanePhotoFocusRequested;
            MapPaneControl.RectangleSelectionCompleted -= OnMapPaneRectangleSelectionCompleted;
            MapPaneControl.NotificationRequested -= OnMapPaneNotificationRequested;
            MapPaneControl.DataContext = null;
        }

        _previewPaneViewModel?.Cleanup();
        _fileBrowserPaneViewModel.Dispose();

        _viewModel?.Dispose();

        GC.SuppressFinalize(this);
    }

    private async void OnOpenLogFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var logDirectory = Path.GetDirectoryName(AppLog.LogFilePath);
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                AppLog.Error("Log directory path is null or empty");
                return;
            }

            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
                AppLog.Info($"Created log directory: {logDirectory}");
            }

            _ = await Windows.System.Launcher.LaunchFolderPathAsync(logDirectory);
            AppLog.Info($"Opened log folder: {logDirectory}");
        }
        catch (UnauthorizedAccessException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (IOException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (ArgumentException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (InvalidOperationException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
    }

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLog.Info("Manual update check triggered");
            var currentVersion = typeof(App).Assembly.GetName().Version;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var updateResult = await UpdateService.CheckForUpdatesAsync(currentVersion, cts.Token).ConfigureAwait(true);

            if (updateResult.IsUpdateAvailable)
            {
                var message = LocalizationService.Format("Dialog.UpdateCheck.UpdateAvailableDetail", updateResult.LatestVersion?.ToString() ?? "Unknown");
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.UpdateCheck.Title"),
                    message).ConfigureAwait(true);
            }
            else
            {
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.UpdateCheck.Title"),
                    LocalizationService.GetString("Dialog.UpdateCheck.NoUpdateDetail")).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Update check was cancelled (timeout or user action)");
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.UpdateCheck.Title"),
                LocalizationService.GetString("Dialog.UpdateCheck.ErrorDetail")).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            await HandleUpdateCheckFailureAsync(ex).ConfigureAwait(true);
        }
        catch (ArgumentException ex)
        {
            await HandleUpdateCheckFailureAsync(ex).ConfigureAwait(true);
        }
    }

    private void HandleOpenLogFolderFailure(Exception ex)
    {
        AppLog.Error("Failed to open log folder", ex);
        _viewModel.ShowNotificationMessage(
            LocalizationService.GetString("Message.FailedOpenLogFolder"),
            InfoBarSeverity.Error);
    }

    private async Task HandleUpdateCheckFailureAsync(Exception ex)
    {
        AppLog.Error("Failed to check for updates", ex);
        await ShowMessageDialogAsync(
            LocalizationService.GetString("Dialog.UpdateCheck.Title"),
            LocalizationService.GetString("Dialog.UpdateCheck.ErrorDetail")).ConfigureAwait(true);
    }

    private async void OnHelpGettingStartedClicked(object sender, RoutedEventArgs e)
    {
        await ShowHelpDialogAsync(
            "Dialog.Help.GettingStarted.Title",
            "Dialog.Help.GettingStarted.Detail",
            includeQuickStartToggle: true).ConfigureAwait(true);
    }

    private async void OnHelpBasicsClicked(object sender, RoutedEventArgs e)
    {
        await ShowHelpDialogAsync(
            "Dialog.Help.Basics.Title",
            "Dialog.Help.Basics.Detail").ConfigureAwait(true);
    }

    private async void OnHelpHtmlWindowClicked(object sender, RoutedEventArgs e)
    {
        await OpenHelpHtmlWindowAsync().ConfigureAwait(true);
    }

    private async void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString()
            ?? LocalizationService.GetString("Common.Unknown");
        await ShowMessageDialogAsync(
            LocalizationService.GetString("Dialog.About.Title"),
            LocalizationService.Format("Dialog.About.Detail", version)).ConfigureAwait(true);
    }

    private async Task ApplyLanguageSettingAsync(string? languageTag, bool showRestartPrompt)
    {
        var normalized = NormalizeLanguageSetting(languageTag);
        var changed = !string.Equals(_languageOverride, normalized, StringComparison.OrdinalIgnoreCase);
        _languageOverride = normalized;
        UpdateLanguageMenuChecks(normalized);
        ApplyLanguageOverride(normalized);

        if (!showRestartPrompt || !changed)
        {
            return;
        }

        if (!_isApplyingSettings)
        {
            await SaveSettingsAsync().ConfigureAwait(true);
        }
        await ShowMessageDialogAsync(
            LocalizationService.GetString("Dialog.LanguageChanged.Title"),
            LocalizationService.GetString("Dialog.LanguageChanged.Detail")).ConfigureAwait(true);
    }

    private void ApplyThemePreference(ThemePreference preference, bool saveSettings)
    {
        var changed = _themePreference != preference;
        _themePreference = preference;
        ApplyTheme(preference);
        UpdateThemeMenuChecks(preference);

        if (saveSettings && changed && !_isApplyingSettings)
        {
            ScheduleSettingsSave();
        }
    }

    private void ApplyTheme(ThemePreference preference)
    {
        if (RootGrid is null)
        {
            return;
        }

        RootGrid.RequestedTheme = preference switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private static string? NormalizeLanguageSetting(string? languageTag)
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

    private void UpdateLanguageMenuChecks(string? normalized)
    {
        if (LanguageSystemMenuItem is not null)
        {
            LanguageSystemMenuItem.IsChecked = string.IsNullOrWhiteSpace(normalized);
        }

        if (LanguageJapaneseMenuItem is not null)
        {
            LanguageJapaneseMenuItem.IsChecked = string.Equals(normalized, "ja-JP", StringComparison.OrdinalIgnoreCase);
        }

        if (LanguageEnglishMenuItem is not null)
        {
            LanguageEnglishMenuItem.IsChecked = string.Equals(normalized, "en-US", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateThemeMenuChecks(ThemePreference preference)
    {
        if (ThemeSystemMenuItem is not null)
        {
            ThemeSystemMenuItem.IsChecked = preference == ThemePreference.System;
        }

        if (ThemeLightMenuItem is not null)
        {
            ThemeLightMenuItem.IsChecked = preference == ThemePreference.Light;
        }

        if (ThemeDarkMenuItem is not null)
        {
            ThemeDarkMenuItem.IsChecked = preference == ThemePreference.Dark;
        }
    }

    private static int NormalizeMapZoomLevel(int level)
    {
        if (MapZoomLevelOptions.Contains(level))
        {
            return level;
        }

        return DefaultMapZoomLevel;
    }

    private void UpdateMapZoomMenuChecks(int level)
    {
        if (MapZoomLevel8MenuItem is not null)
        {
            MapZoomLevel8MenuItem.IsChecked = level == 8;
        }

        if (MapZoomLevel10MenuItem is not null)
        {
            MapZoomLevel10MenuItem.IsChecked = level == 10;
        }

        if (MapZoomLevel12MenuItem is not null)
        {
            MapZoomLevel12MenuItem.IsChecked = level == 12;
        }

        if (MapZoomLevel14MenuItem is not null)
        {
            MapZoomLevel14MenuItem.IsChecked = level == 14;
        }

        if (MapZoomLevel16MenuItem is not null)
        {
            MapZoomLevel16MenuItem.IsChecked = level == 16;
        }

        if (MapZoomLevel18MenuItem is not null)
        {
            MapZoomLevel18MenuItem.IsChecked = level == 18;
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

    private async void OnCreateFolderClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.CreateFolderAsync().ConfigureAwait(true);
    }

    private async void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.RenameSelectionAsync().ConfigureAwait(true);
    }

    private async void OnMoveClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.MoveSelectionAsync().ConfigureAwait(true);
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.DeleteSelectionAsync().ConfigureAwait(true);
    }

    private async Task ShowMessageDialogAsync(string title, string message)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = LocalizationService.GetString("Common.Ok"),
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync().AsTask().ConfigureAwait(true);
    }

    private async Task ShowHelpDialogAsync(string titleKey, string detailKey, bool includeQuickStartToggle = false)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            AppLog.Info($"ShowHelpDialogAsync: XamlRoot unavailable after waiting, skipping dialog '{titleKey}'");
            return;
        }

        CheckBox? quickStartToggle = null;
        UIElement content = CreateHelpDialogContent(LocalizationService.GetString(detailKey));
        if (includeQuickStartToggle)
        {
            quickStartToggle = new CheckBox
            {
                Content = LocalizationService.GetString("Dialog.Help.QuickStartToggle"),
                IsChecked = _showQuickStartOnStartup
            };

            var stack = new StackPanel
            {
                Spacing = 12
            };
            stack.Children.Add(content);
            stack.Children.Add(quickStartToggle);
            content = stack;
        }

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString(titleKey),
            Content = content,
            CloseButtonText = LocalizationService.GetString("Common.Ok"),
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync().AsTask().ConfigureAwait(true);

        if (includeQuickStartToggle && quickStartToggle is not null)
        {
            _showQuickStartOnStartup = quickStartToggle.IsChecked ?? false;
            await SaveSettingsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// XamlRoot が利用可能になるまで待機します。
    /// WinUI 3 では OnActivated 直後に XamlRoot が null になる環境があるため、
    /// Loaded イベントまたは DispatcherQueue で待機してから ContentDialog を表示します。
    /// </summary>
    /// <returns>XamlRoot が利用可能になった場合は true、タイムアウトした場合は false。</returns>
    private async Task<bool> EnsureXamlRootAsync()
    {
        const int maxWaitMs = 3000;
        const int intervalMs = 50;

        if (RootGrid.XamlRoot is not null)
        {
            return true;
        }

        AppLog.Info("EnsureXamlRootAsync: XamlRoot is null, waiting for it to become available...");

        // RootGrid.Loaded を待つ（まだ Loaded されていない場合）
        var tcs = new TaskCompletionSource<bool>();
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= OnLoaded;
            tcs.TrySetResult(true);
        }

        RootGrid.Loaded += OnLoaded;

        // 既に Loaded 済みの場合や、イベントが発火しない場合に備えてポーリングも併用
        var elapsed = 0;
        while (RootGrid.XamlRoot is null && elapsed < maxWaitMs)
        {
            await Task.Delay(intervalMs).ConfigureAwait(true);
            elapsed += intervalMs;

            // Loaded イベントが発火していたら終了
            if (tcs.Task.IsCompleted)
            {
                break;
            }
        }

        RootGrid.Loaded -= OnLoaded;

        if (RootGrid.XamlRoot is not null)
        {
            AppLog.Info($"EnsureXamlRootAsync: XamlRoot became available after {elapsed}ms.");
            return true;
        }

        AppLog.Info($"EnsureXamlRootAsync: XamlRoot still null after {elapsed}ms, giving up.");
        return false;
    }

    private static ScrollViewer CreateHelpDialogContent(string message)
    {
        return new ScrollViewer
        {
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private async Task ShowQuickStartIfNeededAsync()
    {
        if (_settingsFileExistsAtStartup)
        {
            if (!_showQuickStartOnStartup)
            {
                return;
            }
        }

        await ShowHelpDialogAsync(
            "Dialog.Help.GettingStarted.Title",
            "Dialog.Help.GettingStarted.Detail",
            includeQuickStartToggle: true).ConfigureAwait(true);
    }

    private async Task OpenHelpHtmlWindowAsync()
    {
        var uri = TryGetHelpHtmlUri();
        if (uri is null)
        {
            await ShowHelpHtmlMissingDialogAsync().ConfigureAwait(true);
            return;
        }

        if (_helpHtmlWindow is not null)
        {
            if (_helpHtmlWebView is not null)
            {
                _helpHtmlWebView.Source = uri;
            }

            _helpHtmlWindow.Activate();
            return;
        }

        var webView = CreateHelpHtmlWebView(uri);
        _helpHtmlWebView = webView;
        var container = new Grid();
        container.Children.Add(webView);

        var window = new Window
        {
            Title = LocalizationService.GetString("Dialog.Help.Html.Title"),
            Content = container
        };
        window.Closed += (_, _) => CleanupHelpHtmlWindow();
        _helpHtmlWindow = window;
        window.Activate();
        TryResizeHelpWindow(window, 980, 720);
    }

    private WebView2 CreateHelpHtmlWebView(Uri uri)
    {
        var webView = new WebView2
        {
            Source = uri,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        webView.NavigationStarting += OnHelpWebViewNavigationStarting;
        webView.CoreWebView2Initialized += OnHelpWebViewInitialized;
        return webView;
    }

    private Uri? TryGetHelpHtmlUri()
    {
        var helpDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "help");
        var preferredFileName = GetHelpHtmlFileName();
        var preferredPath = Path.Combine(helpDirectory, preferredFileName);
        if (File.Exists(preferredPath))
        {
            return new Uri(preferredPath);
        }

        var fallbackPath = Path.Combine(helpDirectory, "index.html");
        if (File.Exists(fallbackPath))
        {
            if (!string.Equals(preferredFileName, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Info($"Help HTML fallback to {fallbackPath}");
            }

            return new Uri(fallbackPath);
        }

        AppLog.Error($"Help HTML not found: {preferredPath}");
        return null;
    }

    private string GetHelpHtmlFileName()
    {
        var language = _languageOverride;
        if (string.IsNullOrWhiteSpace(language))
        {
            language = ApplicationLanguages.Languages.Count > 0
                ? ApplicationLanguages.Languages[0]
                : CultureInfo.CurrentUICulture.Name;
        }

        if (!string.IsNullOrWhiteSpace(language)
            && language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "index.en.html";
        }

        return "index.html";
    }

    private async Task ShowHelpHtmlMissingDialogAsync()
    {
        await ShowMessageDialogAsync(
            LocalizationService.GetString("Dialog.Help.HtmlMissing.Title"),
            LocalizationService.GetString("Dialog.Help.HtmlMissing.Detail")).ConfigureAwait(true);
    }

    private void CleanupHelpHtmlWindow()
    {
        CloseHelpHtmlWebView();
        _helpHtmlWindow = null;
    }

    private void CloseHelpHtmlWindow()
    {
        if (_helpHtmlWindow is null)
        {
            CleanupHelpHtmlWindow();
            return;
        }

        try
        {
            _helpHtmlWindow.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException)
        {
            AppLog.Error("Failed to close help window.", ex);
            CleanupHelpHtmlWindow();
        }
    }

    private void CloseHelpHtmlWebView()
    {
        if (_helpHtmlWebView is null)
        {
            return;
        }

        try
        {
            _helpHtmlWebView.NavigationStarting -= OnHelpWebViewNavigationStarting;
            _helpHtmlWebView.CoreWebView2Initialized -= OnHelpWebViewInitialized;
            if (_helpHtmlWebView.CoreWebView2 is not null)
            {
                _helpHtmlWebView.CoreWebView2.NewWindowRequested -= OnHelpWebViewNewWindowRequested;
            }
            _helpHtmlWebView.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            AppLog.Error("Failed to close help WebView2.", ex);
        }
        finally
        {
            _helpHtmlWebView = null;
        }
    }

    private void OnHelpWebViewInitialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (args.Exception is not null)
        {
            AppLog.Error("Help WebView2 initialization failed.", args.Exception);
            return;
        }

        sender.CoreWebView2.NewWindowRequested += OnHelpWebViewNewWindowRequested;
    }

    private void OnHelpWebViewNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (TryGetExternalUri(args.Uri, out var uri) && uri is not null)
        {
            args.Handled = true;
            _ = OpenExternalUriAsync(uri);
        }
    }

    private void OnHelpWebViewNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (TryGetExternalUri(args.Uri, out var uri) && uri is not null)
        {
            args.Cancel = true;
            _ = OpenExternalUriAsync(uri);
        }
    }

    private static bool TryGetExternalUri(string? uriString, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(uriString))
        {
            return false;
        }

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        return false;
    }

    private async Task OpenExternalUriAsync(Uri uri)
    {
        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException
            or ArgumentException)
        {
            AppLog.Error("Failed to launch help link.", ex);
            _viewModel.ShowNotificationMessage(
                LocalizationService.GetString("Message.LaunchBrowserFailed"),
                InfoBarSeverity.Error);
        }
    }

    private static void TryResizeHelpWindow(Window window, int width, int height)
    {
        try
        {
            var appWindow = GetAppWindow(window);
            appWindow.Resize(new SizeInt32(width, height));
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            AppLog.Error("Failed to resize help window.", ex);
        }
    }

    private static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private async void OnEditExifRequested(object? sender, EventArgs e)
    {
        await EditExifAsync().ConfigureAwait(true);
    }

    private async Task EditExifAsync()
    {
        // Validate selection
        if (_fileBrowserPaneViewModel.SelectedItems.Count != 1)
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("ExifEditor.Title"),
                LocalizationService.GetString("Message.ExifEditorMultipleFiles")).ConfigureAwait(true);
            return;
        }

        var item = _fileBrowserPaneViewModel.SelectedItems[0];
        if (item.IsFolder)
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("ExifEditor.Title"),
                LocalizationService.GetString("Message.ExifEditorFolderSelected")).ConfigureAwait(true);
            return;
        }

        // Load current metadata
        var metadata = await PhotoGeoExplorer.Services.ExifService.GetMetadataAsync(item.FilePath, CancellationToken.None).ConfigureAwait(true);

        var state = new ExifEditState
        {
            UpdateDate = metadata?.TakenAt.HasValue ?? false,
            TakenAtDate = metadata?.TakenAt?.Date ?? DateTimeOffset.Now.Date,
            TakenAtTime = metadata?.TakenAt?.TimeOfDay ?? TimeSpan.Zero,
            LatitudeText = metadata?.Latitude?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            LongitudeText = metadata?.Longitude?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            UpdateFileDate = false
        };

        while (true)
        {
            var result = await ShowExifEditDialogAsync(state).ConfigureAwait(true);
            state = result.State;

            if (result.Action == ExifDialogAction.Cancel)
            {
                return;
            }

            if (result.Action == ExifDialogAction.PickLocation)
            {
                var pickedLocation = await PickExifLocationAsync().ConfigureAwait(true);
                if (pickedLocation is not null)
                {
                    state.LatitudeText = pickedLocation.Value.Latitude.ToString("F6", CultureInfo.InvariantCulture);
                    state.LongitudeText = pickedLocation.Value.Longitude.ToString("F6", CultureInfo.InvariantCulture);
                }

                continue;
            }

            break;
        }

        // Parse input values
        DateTimeOffset? newTakenAt = null;
        if (state.UpdateDate)
        {
            newTakenAt = new DateTimeOffset(
                state.TakenAtDate.Date.Add(state.TakenAtTime),
                DateTimeOffset.Now.Offset);
        }

        double? newLatitude = null;
        if (!string.IsNullOrWhiteSpace(state.LatitudeText) &&
            double.TryParse(state.LatitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
        {
            newLatitude = lat;
        }

        double? newLongitude = null;
        if (!string.IsNullOrWhiteSpace(state.LongitudeText) &&
            double.TryParse(state.LongitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            newLongitude = lon;
        }

        var updateFileDate = state.UpdateFileDate;

        // Update EXIF metadata
        var success = await PhotoGeoExplorer.Services.ExifService.UpdateMetadataAsync(
            item.FilePath,
            newTakenAt,
            newLatitude,
            newLongitude,
            updateFileDate,
            CancellationToken.None).ConfigureAwait(true);

        if (success)
        {
            _viewModel.ShowNotificationMessage(
                LocalizationService.GetString("Message.ExifUpdateSuccess"),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);

            // Refresh the file list to show updated info
            await _fileBrowserPaneViewModel.RefreshAsync().ConfigureAwait(true);
        }
        else
        {
            _viewModel.ShowNotificationMessage(
                LocalizationService.GetString("Message.ExifUpdateFailed"),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    private async Task<(ExifDialogAction Action, ExifEditState State)> ShowExifEditDialogAsync(ExifEditState state)
    {
        var pickLocationRequested = false;

        var dialogContent = new StackPanel
        {
            Spacing = 12,
            MinWidth = 400
        };

        // Update Date checkbox
        var updateDateCheckBox = new CheckBox
        {
            Content = LocalizationService.GetString("ExifEditor.UpdateDateCheckbox"),
            IsChecked = state.UpdateDate
        };
        dialogContent.Children.Add(updateDateCheckBox);

        var updateFileDateCheckBox = new CheckBox
        {
            Content = LocalizationService.GetString("ExifEditor.UpdateFileDate"),
            IsChecked = state.UpdateDate && state.UpdateFileDate,
            IsEnabled = state.UpdateDate
        };

        // Date Taken
        var takenAtLabel = new TextBlock
        {
            Text = LocalizationService.GetString("ExifEditor.TakenAtLabel"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var takenAtPicker = new DatePicker
        {
            Date = state.TakenAtDate,
            IsEnabled = state.UpdateDate
        };
        var takenAtTimePicker = new TimePicker
        {
            Time = state.TakenAtTime,
            IsEnabled = state.UpdateDate
        };

        // Enable/disable date pickers based on checkbox
        updateDateCheckBox.Checked += (s, e) =>
        {
            takenAtPicker.IsEnabled = true;
            takenAtTimePicker.IsEnabled = true;
            updateFileDateCheckBox.IsEnabled = true;
        };
        updateDateCheckBox.Unchecked += (s, e) =>
        {
            takenAtPicker.IsEnabled = false;
            takenAtTimePicker.IsEnabled = false;
            updateFileDateCheckBox.IsChecked = false;
            updateFileDateCheckBox.IsEnabled = false;
        };

        dialogContent.Children.Add(takenAtLabel);
        dialogContent.Children.Add(takenAtPicker);
        dialogContent.Children.Add(takenAtTimePicker);

        // Latitude
        var latitudeLabel = new TextBlock
        {
            Text = LocalizationService.GetString("ExifEditor.LatitudeLabel"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var latitudeBox = new TextBox
        {
            PlaceholderText = "0.0",
            Text = state.LatitudeText ?? string.Empty
        };

        dialogContent.Children.Add(latitudeLabel);
        dialogContent.Children.Add(latitudeBox);

        // Longitude
        var longitudeLabel = new TextBlock
        {
            Text = LocalizationService.GetString("ExifEditor.LongitudeLabel"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var longitudeBox = new TextBox
        {
            PlaceholderText = "0.0",
            Text = state.LongitudeText ?? string.Empty
        };

        dialogContent.Children.Add(longitudeLabel);
        dialogContent.Children.Add(longitudeBox);

        ContentDialog dialog = null!;

        // Get location from map button
        var getLocationButton = new Button
        {
            Content = LocalizationService.GetString("ExifEditor.GetLocationFromMap"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        getLocationButton.Click += (s, args) =>
        {
            pickLocationRequested = true;
            CaptureState();
            dialog.Hide();
        };
        dialogContent.Children.Add(getLocationButton);

        // Clear location button
        var clearLocationButton = new Button
        {
            Content = LocalizationService.GetString("ExifEditor.ClearLocation"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        clearLocationButton.Click += (s, args) =>
        {
            latitudeBox.Text = string.Empty;
            longitudeBox.Text = string.Empty;
        };
        dialogContent.Children.Add(clearLocationButton);

        // Update file date checkbox
        dialogContent.Children.Add(updateFileDateCheckBox);

        // Create and show dialog
        dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("ExifEditor.Title"),
            Content = dialogContent,
            PrimaryButtonText = LocalizationService.GetString("ExifEditor.SaveButton"),
            SecondaryButtonText = LocalizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        CaptureState();

        if (pickLocationRequested)
        {
            return (ExifDialogAction.PickLocation, state);
        }

        return result == ContentDialogResult.Primary
            ? (ExifDialogAction.Save, state)
            : (ExifDialogAction.Cancel, state);

        void CaptureState()
        {
            state.UpdateDate = updateDateCheckBox.IsChecked ?? false;
            state.TakenAtDate = takenAtPicker.Date;
            state.TakenAtTime = takenAtTimePicker.Time;
            state.LatitudeText = latitudeBox.Text ?? string.Empty;
            state.LongitudeText = longitudeBox.Text ?? string.Empty;
            state.UpdateFileDate = updateFileDateCheckBox.IsChecked ?? false;
        }
    }

    private async Task<(double Latitude, double Longitude)?> PickExifLocationAsync()
    {
        if (MapPaneControl is null || !MapPaneControl.CanPickExifLocation)
        {
            _viewModel.ShowNotificationMessage(
                LocalizationService.GetString("Message.ExifPickLocationUnavailable"),
                InfoBarSeverity.Warning);
            return null;
        }

        _viewModel.ShowNotificationMessage(
            LocalizationService.GetString("Message.ExifPickLocationInstruction"),
            InfoBarSeverity.Informational);

        var pickedLocation = await MapPaneControl.PickExifLocationAsync().ConfigureAwait(true);
        if (pickedLocation is null)
        {
            _viewModel.ShowNotificationMessage(
                LocalizationService.GetString("Message.ExifPickLocationCanceled"),
                InfoBarSeverity.Informational);
            return null;
        }

        _viewModel.ShowNotificationMessage(string.Empty, InfoBarSeverity.Informational);
        return pickedLocation;
    }

    private sealed class ExifEditState
    {
        public bool UpdateDate { get; set; }
        public DateTimeOffset TakenAtDate { get; set; }
        public TimeSpan TakenAtTime { get; set; }
        public string LatitudeText { get; set; } = string.Empty;
        public string LongitudeText { get; set; } = string.Empty;
        public bool UpdateFileDate { get; set; }
    }

    private enum ExifDialogAction
    {
        Save,
        Cancel,
        PickLocation
    }

    private async void OnMoveToParentClicked(object sender, RoutedEventArgs e)
    {
        await FileBrowserPaneControl.MoveSelectionToParentAsync().ConfigureAwait(true);
    }

    private void OnMapPanePhotoFocusRequested(object? sender, MapPanePhotoFocusRequestedEventArgs e)
    {
        FileBrowserPaneControl.FocusPhotoItem(e.PhotoItem);
    }

    private void OnMapPaneRectangleSelectionCompleted(object? sender, MapPaneRectangleSelectionEventArgs e)
    {
        var selectedItems = new List<PhotoListItem>();
        foreach (var photoItem in e.PhotoItems)
        {
            var listItem = _fileBrowserPaneViewModel.Items.FirstOrDefault(item =>
                !item.IsFolder && string.Equals(item.FilePath, photoItem.FilePath, StringComparison.OrdinalIgnoreCase));
            if (listItem is not null)
            {
                selectedItems.Add(listItem);
            }
        }

        FileBrowserPaneControl.SelectItems(selectedItems);
    }

    private void OnMapPaneNotificationRequested(object? sender, MapPaneNotificationEventArgs e)
    {
        _viewModel.ShowNotificationMessage(e.Message, e.Severity);
    }

    private static bool IsJpegFile(PhotoListItem? item)
    {
        if (item is null || item.IsFolder)
        {
            return false;
        }

        var extension = Path.GetExtension(item.FilePath);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }
}
