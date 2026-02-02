using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BruTile.Cache;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Mapsui.UI.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.Globalization;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Preview;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;
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
    private const string InternalDragKey = "PhotoGeoExplorer.InternalDrag";
    private const string PhotoItemKey = "PhotoItem";
    private const string PhotoMetadataKey = "PhotoMetadata";
    private const int DefaultMapZoomLevel = 14;
    private static readonly int[] MapZoomLevelOptions = { 8, 10, 12, 14, 16, 18 };
    private static readonly Color SelectionFillColor = Color.FromArgb(64, 0, 120, 215);
    private static readonly Color SelectionOutlineColor = Color.FromArgb(255, 0, 120, 215);
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly PreviewPaneViewModel _previewPaneViewModel;
    private bool _layoutStored;
    private bool _mapInitialized;
    private Map? _map;
    private Mapsui.Tiling.Layers.TileLayer? _baseTileLayer;
    private MemoryLayer? _markerLayer;
    private bool _previewMaximized;
    private bool _windowSized;
    private bool _windowIconSet;
    private bool _suppressBreadcrumbNavigation;
    private CancellationTokenSource? _mapUpdateCts;
    private CancellationTokenSource? _settingsCts;
    private GridLength _storedDetailWidth;
    private GridLength _storedFileBrowserWidth;
    private GridLength _storedPreviewRowHeight;
    private GridLength _storedMapRowHeight;
    private GridLength _storedMapSplitterHeight;
    private GridLength _storedSplitterWidth;
    private double _storedMapRowMinHeight;
    private List<PhotoListItem>? _dragItems;
    private bool _isApplyingSettings;
    private string? _languageOverride;
    private string? _startupFilePath;
    private ThemePreference _themePreference = ThemePreference.System;
    private int _mapDefaultZoomLevel = DefaultMapZoomLevel;
    private MapTileSourceType _mapTileSource = MapTileSourceType.OpenStreetMap;
    private PhotoMetadata? _flyoutMetadata;
    private bool _mapRectangleSelecting;
    private MPoint? _mapRectangleStart;
    private MemoryLayer? _rectangleSelectionLayer;
    private bool _mapPanLockBeforeSelection;
    private bool _mapPanLockActive;
    private TaskCompletionSource<(double Latitude, double Longitude)?>? _exifLocationPicker;
    private bool _isPickingExifLocation;
    private bool _isExifPickPointerActive;
    private Windows.Foundation.Point? _exifPickPointerStart;
    private bool _restoreMapStatusAfterExifPick;
    private Visibility _exifPickPreviousStatusOverlayVisibility = Visibility.Collapsed;
    private Visibility _exifPickPreviousStatusPanelVisibility = Visibility.Collapsed;
    private Window? _helpHtmlWindow;
    private WebView2? _helpHtmlWebView;
    private readonly bool _settingsFileExistsAtStartup;
    private bool _showQuickStartOnStartup;
    private double _lastRasterizationScale = 1.0;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new FileSystemService());
        _settingsService = new SettingsService();
        _previewPaneViewModel = new PreviewPaneViewModel(new PreviewPaneService(), _viewModel.WorkspaceState);
        _settingsFileExistsAtStartup = _settingsService.SettingsFileExists();
        RootGrid.DataContext = _viewModel;
        PreviewPaneControl.DataContext = _previewPaneViewModel;
        PreviewPaneControl.MaximizeChanged += OnPreviewMaximizeChanged;
        Title = LocalizationService.GetString("MainWindow.Title");
        AppLog.Info("MainWindow constructed.");
        Activated += OnActivated;
        Closed += OnClosed;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        await InitializeMapAsync().ConfigureAwait(true);
        await LoadSettingsAsync().ConfigureAwait(true);
        await ApplyStartupFolderOverrideAsync().ConfigureAwait(true);
        await ApplyStartupFileActivationAsync().ConfigureAwait(true);
        await _viewModel.InitializeAsync().ConfigureAwait(true);
        await UpdateMapFromSelectionAsync().ConfigureAwait(true);

        // XamlRoot が確定するまでワンテンポ遅らせてからダイアログを表示
        DispatcherQueue.TryEnqueue(async () =>
        {
            await ShowQuickStartIfNeededAsync().ConfigureAwait(true);
        });

        // マルチモニターDPI変更を検出するために XamlRoot.Changed をサブスクライブ
        await SubscribeToXamlRootChangedAsync().ConfigureAwait(true);
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

    private async Task SubscribeToXamlRootChangedAsync()
    {
        // XamlRoot が利用可能になるまで待機してから XamlRoot.Changed をサブスクライブ
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            AppLog.Error("Failed to subscribe to XamlRoot.Changed: XamlRoot is null.");
            return;
        }

        // XamlRoot をローカル変数に格納（EnsureXamlRootAsync と使用の間に null になる可能性への防御）
        var xamlRoot = RootGrid.XamlRoot;
        if (xamlRoot is null)
        {
            AppLog.Error("XamlRoot became null after EnsureXamlRootAsync succeeded.");
            return;
        }

        _lastRasterizationScale = xamlRoot.RasterizationScale;
        xamlRoot.Changed += OnXamlRootChanged;
        AppLog.Info($"Subscribed to XamlRoot.Changed. Initial RasterizationScale: {_lastRasterizationScale}");
    }

    private Task InitializeMapAsync()
    {
        if (_map is not null)
        {
            return Task.CompletedTask;
        }

        if (MapControl is null)
        {
            AppLog.Error("Map control is missing.");
            ShowMapStatus(
                LocalizationService.GetString("MapStatus.ControlMissingTitle"),
                LocalizationService.GetString("MapStatus.SeeLogDetail"),
                Symbol.Map);
            return Task.CompletedTask;
        }

        Map? map = null;
        try
        {
            map = new Map();
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoGeoExplorer",
                "Cache",
                "Tiles");
            var userAgent = UserAgentProvider.UserAgent;
            var tileLayer = CreateTileLayer(_mapTileSource, cacheRoot, userAgent);
            _baseTileLayer = tileLayer;
            map.Layers.Add(tileLayer);

            var markerLayer = new MemoryLayer
            {
                Name = "PhotoMarkers",
                Features = Array.Empty<IFeature>(),
                Style = null
            };
            map.Layers.Add(markerLayer);

            _map = map;
            _markerLayer = markerLayer;
            MapControl.Map = map;
            map = null; // 所有権が _map に移ったので null に設定
            HideMapStatus();
            AppLog.Info("Map initialized.");
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Map init failed.", ex);
            ShowMapStatus(
                LocalizationService.GetString("MapStatus.InitFailedTitle"),
                LocalizationService.GetString("MapStatus.SeeLogDetail"),
                Symbol.Map);
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error("Map init failed.", ex);
            ShowMapStatus(
                LocalizationService.GetString("MapStatus.InitFailedTitle"),
                LocalizationService.GetString("MapStatus.SeeLogDetail"),
                Symbol.Map);
        }
        finally
        {
            map?.Dispose();
        }

        return Task.CompletedTask;
    }

    private static TileLayer CreateOpenStreetMapLayer(string userAgent, IPersistentCache<byte[]>? persistentCache = null)
    {
        var tileSource = new BruTile.Web.HttpTileSource(
            new BruTile.Predefined.GlobalSphericalMercator(),
            "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            name: "OpenStreetMap",
            attribution: new BruTile.Attribution("© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright"),
            configureHttpRequestMessage: (r) => r.Headers.TryAddWithoutValidation("User-Agent", userAgent),
            persistentCache: persistentCache);

        return new TileLayer(tileSource) { Name = "OpenStreetMap" };
    }

    private static TileLayer CreateEsriWorldImageryLayer(string userAgent, IPersistentCache<byte[]>? persistentCache = null)
    {
        var tileSource = new BruTile.Web.HttpTileSource(
            new BruTile.Predefined.GlobalSphericalMercator(),
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            name: "Esri WorldImagery",
            attribution: new BruTile.Attribution("Esri, i-cubed, USDA, USGS, AEX, GeoEye, Getmapping, Aerogrid, IGN, IGP, UPR-EGP, and the GIS User Community"),
            configureHttpRequestMessage: (r) => r.Headers.TryAddWithoutValidation("User-Agent", userAgent),
            persistentCache: persistentCache);

        return new TileLayer(tileSource) { Name = "Esri WorldImagery" };
    }

    private static TileLayer CreateTileLayer(MapTileSourceType sourceType, string cacheDirectory, string userAgent)
    {
        var sourceDirectory = Path.Combine(cacheDirectory, sourceType.ToString());
        Directory.CreateDirectory(sourceDirectory);
        var persistentCache = new FileCache(sourceDirectory, "png");

        return sourceType switch
        {
            MapTileSourceType.EsriWorldImagery => CreateEsriWorldImageryLayer(userAgent, persistentCache),
            _ => CreateOpenStreetMapLayer(userAgent, persistentCache)
        };
    }

    private void SwitchTileLayer(MapTileSourceType newSource)
    {
        if (_map is null || MapControl is null)
        {
            return;
        }

        try
        {
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoGeoExplorer",
                "Cache",
                "Tiles");
            var userAgent = UserAgentProvider.UserAgent;
            var newTileLayer = CreateTileLayer(newSource, cacheRoot, userAgent);

            if (_baseTileLayer is not null)
            {
                _map.Layers.Remove(_baseTileLayer);
                _baseTileLayer.Dispose();
            }

            _map.Layers.Insert(0, newTileLayer);
            _baseTileLayer = newTileLayer;
            _mapTileSource = newSource;

            MapControl.Refresh();
            AppLog.Info($"Switched map tile source to {newSource}");
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Map tile switch failed.", ex);
            _viewModel.ShowNotificationMessage(
                LocalizationService.GetString("Message.OperationFailed"),
                InfoBarSeverity.Error);
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error("Map tile switch failed.", ex);
            _viewModel.ShowNotificationMessage(
                LocalizationService.GetString("Message.OperationFailed"),
                InfoBarSeverity.Error);
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

        SwitchTileLayer(sourceType);
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

        await _viewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);
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

        await _viewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);

        var item = _viewModel.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item is null || item.IsFolder)
        {
            AppLog.Error($"Startup file not listed in folder view: {filePath}");
            return;
        }

        _viewModel.UpdateSelection(new[] { item });
        _viewModel.SelectedItem = item;
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
        _mapDefaultZoomLevel = NormalizeMapZoomLevel(settings.MapDefaultZoomLevel);
        _showQuickStartOnStartup = settings.ShowQuickStartOnStartup;
        UpdateMapZoomMenuChecks(_mapDefaultZoomLevel);

        var savedTileSource = Enum.IsDefined(settings.MapTileSource) ? settings.MapTileSource : MapTileSourceType.OpenStreetMap;
        if (savedTileSource != _mapTileSource)
        {
            // 設定で保存されたタイルソースが現在のものと異なる場合、切り替えを実行
            SwitchTileLayer(savedTileSource);
        }
        else
        {
            _mapTileSource = savedTileSource;
        }
        UpdateMapTileSourceMenuChecks(_mapTileSource);

        _viewModel.ShowImagesOnly = settings.ShowImagesOnly;
        _viewModel.FileViewMode = Enum.IsDefined<FileViewMode>(settings.FileViewMode)
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
                await _viewModel.LoadFolderAsync(validPath).ConfigureAwait(true);

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
            LastFolderPath = _viewModel.CurrentFolderPath,
            ShowImagesOnly = _viewModel.ShowImagesOnly,
            FileViewMode = _viewModel.FileViewMode,
            Language = _languageOverride,
            Theme = _themePreference,
            MapDefaultZoomLevel = _mapDefaultZoomLevel,
            MapTileSource = _mapTileSource,
            ShowQuickStartOnStartup = _showQuickStartOnStartup
        };
    }

    private void ShowMapStatus(string title, string? description, Symbol symbol)
    {
        MapStatusTitle.Text = title;
        MapStatusDescription.Text = description ?? string.Empty;
        MapStatusDescription.Visibility = string.IsNullOrWhiteSpace(description) ? Visibility.Collapsed : Visibility.Visible;
        MapStatusIcon.Symbol = symbol;
        MapStatusOverlay.Visibility = Visibility.Visible;
        MapStatusPanel.Visibility = Visibility.Visible;
    }

    private void HideMapStatus()
    {
        MapStatusOverlay.Visibility = Visibility.Collapsed;
        MapStatusPanel.Visibility = Visibility.Collapsed;
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedMetadata) or nameof(MainViewModel.SelectedItem))
        {
            await UpdateMapFromSelectionAsync().ConfigureAwait(true);
        }

        if (e.PropertyName is nameof(MainViewModel.ShowImagesOnly)
            or nameof(MainViewModel.FileViewMode)
            or nameof(MainViewModel.CurrentFolderPath))
        {
            ScheduleSettingsSave();
        }
    }

    private async Task UpdateMapFromSelectionAsync()
    {
        if (_map is null || _markerLayer is null)
        {
            return;
        }

        var previousCts = _mapUpdateCts;
        _mapUpdateCts = null;
        if (previousCts is not null)
        {
            await previousCts.CancelAsync().ConfigureAwait(true);
            previousCts.Dispose();
        }

        var selectedItems = _viewModel.SelectedItems;
        var imageItems = selectedItems.Where(item => !item.IsFolder).ToList();
        if (imageItems.Count == 0)
        {
            ClearMapMarkers();
            ShowMapStatus(
                LocalizationService.GetString("MapStatus.SelectPhotoTitle"),
                LocalizationService.GetString("MapStatus.SelectPhotoDetail"),
                Symbol.Map);
            return;
        }

        if (imageItems.Count == 1
            && ReferenceEquals(imageItems[0], _viewModel.SelectedItem)
            && _viewModel.SelectedMetadata is PhotoMetadata selectedMetadata)
        {
            if (TryGetValidLocation(selectedMetadata, out var latitude, out var longitude))
            {
                SetMapMarker(latitude, longitude, selectedMetadata, imageItems[0].Item);
                HideMapStatus();
            }
            else
            {
                ClearMapMarkers();
                ShowMapStatus(
                    LocalizationService.GetString("MapStatus.LocationMissingTitle"),
                    LocalizationService.GetString("MapStatus.LocationMissingDetail"),
                    Symbol.Map);
            }

            return;
        }

        var cts = new CancellationTokenSource();
        _mapUpdateCts = cts;

        IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)> metadataItems;
        try
        {
            metadataItems = await LoadSelectionMetadataAsync(imageItems, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        var points = new List<(double Latitude, double Longitude, PhotoMetadata Metadata, PhotoItem Item)>();
        foreach (var (item, metadata) in metadataItems)
        {
            if (metadata is null || !TryGetValidLocation(metadata, out var latitude, out var longitude))
            {
                continue;
            }

            points.Add((latitude, longitude, metadata, item.Item));
        }

        if (points.Count == 0)
        {
            ClearMapMarkers();
            ShowMapStatus(
                LocalizationService.GetString("MapStatus.LocationMissingTitle"),
                LocalizationService.GetString("MapStatus.LocationMissingSelectionDetail"),
                Symbol.Map);
            return;
        }

        if (points.Count == 1)
        {
            var single = points[0];
            SetMapMarker(single.Latitude, single.Longitude, single.Metadata, single.Item);
            HideMapStatus();
            return;
        }

        SetMapMarkers(points);
        HideMapStatus();
    }

    private static bool TryGetValidLocation(PhotoMetadata metadata, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        if (!metadata.HasLocation
            || metadata.Latitude is not double lat
            || metadata.Longitude is not double lon)
        {
            return false;
        }

        if (Math.Abs(lat) < 0.000001 && Math.Abs(lon) < 0.000001)
        {
            return false;
        }

        latitude = lat;
        longitude = lon;
        return true;
    }

    private void ClearMapMarkers()
    {
        if (_markerLayer is null)
        {
            return;
        }

        _markerLayer.Features = Array.Empty<IFeature>();
        _map?.Refresh();
    }

    private void SetMapMarker(double latitude, double longitude, PhotoMetadata metadata, PhotoItem photoItem)
    {
        if (_map is null || _markerLayer is null)
        {
            return;
        }

        var position = SphericalMercator.FromLonLat(new MPoint(longitude, latitude));
        var feature = new PointFeature(position);
        feature.Styles.Clear();
        foreach (var style in CreatePinStyles(metadata))
        {
            feature.Styles.Add(style);
        }
        feature[PhotoMetadataKey] = metadata;
        feature[PhotoItemKey] = photoItem;
        _markerLayer.Features = new[] { feature };
        _map.Refresh();

        var navigator = _map.Navigator;
        navigator.CenterOn(position, 0, Mapsui.Animations.Easing.CubicOut);
        if (navigator.Resolutions.Count > 0)
        {
            var targetLevel = Math.Clamp(_mapDefaultZoomLevel, 0, navigator.Resolutions.Count - 1);
            navigator.ZoomToLevel(targetLevel);
        }
    }

    private static IStyle[] CreatePinStyles(PhotoMetadata metadata)
    {
        var pinPath = GetPinPath(metadata);
        if (TryCreatePinStyle(pinPath, out var pinStyle))
        {
            return new IStyle[] { pinStyle };
        }

        return new IStyle[] { CreateFallbackMarkerStyle() };
    }

    private static bool TryCreatePinStyle(string imagePath, out ImageStyle pinStyle)
    {
        pinStyle = null!;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                AppLog.Info($"Pin image missing: {imagePath}");
            }
            return false;
        }

        var imageUri = new Uri(imagePath).AbsoluteUri;
        pinStyle = new ImageStyle
        {
            Image = new Mapsui.Styles.Image { Source = imageUri },
            SymbolScale = 1,
            RelativeOffset = new RelativeOffset(0, 0.5)
        };
        return true;
    }

    private static SymbolStyle CreateFallbackMarkerStyle()
    {
        return new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.8,
            Fill = new Brush(Color.FromArgb(255, 32, 128, 255)),
            Outline = new Pen(Color.White, 2)
        };
    }

    private static string GetPinPath(PhotoMetadata metadata)
    {
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins");
        if (metadata.TakenAt is DateTimeOffset takenAt)
        {
            var age = DateTimeOffset.Now - takenAt;
            if (age <= TimeSpan.FromDays(30))
            {
                return Path.Combine(assetsRoot, "green_pin.png");
            }

            if (age <= TimeSpan.FromDays(365))
            {
                return Path.Combine(assetsRoot, "blue_pin.png");
            }
        }

        return Path.Combine(assetsRoot, "red_pin.png");
    }

    private static async Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadSelectionMetadataAsync(
        IReadOnlyList<PhotoListItem> items,
        CancellationToken cancellationToken)
    {
        var tasks = items.Select(async item =>
        {
            var metadata = await ExifService.GetMetadataAsync(item.FilePath, cancellationToken).ConfigureAwait(true);
            return (Item: item, Metadata: metadata);
        });

        return await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    /// <summary>
    /// マルチモニター環境でウィンドウを異なるDPIのモニターに移動した際に呼ばれます。
    /// 画像プレビューのズームファクターを補正して、視覚的なサイズを維持します。
    /// </summary>
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        var newScale = sender.RasterizationScale;
        if (Math.Abs(newScale - _lastRasterizationScale) < 0.0001)
        {
            return;
        }

        AppLog.Info($"RasterizationScale changed: {_lastRasterizationScale} -> {newScale}");

        // PreviewPaneViewModel にDPIスケーリング変更を通知
        _previewPaneViewModel.OnRasterizationScaleChanged(newScale);

        _lastRasterizationScale = newScale;
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
        await _viewModel.OpenHomeAsync().ConfigureAwait(true);
    }

    private async void OnNavigateBackClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.NavigateBackAsync().ConfigureAwait(true);
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
            await _viewModel.NavigateForwardAsync().ConfigureAwait(true);
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
        await _viewModel.NavigateUpAsync().ConfigureAwait(true);
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnApplyFiltersClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnFiltersChanged(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        await OpenFolderPickerAsync().ConfigureAwait(true);
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
        _viewModel.ResetFilters();
        await _viewModel.RefreshAsync().ConfigureAwait(true);
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
        _viewModel.ShowImagesOnly = !_viewModel.ShowImagesOnly;
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private void OnViewModeMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string tag)
        {
            return;
        }

        if (Enum.TryParse(tag, out FileViewMode mode))
        {
            _viewModel.FileViewMode = mode;
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

        // XamlRoot.Changed イベントをアンサブスクライブ
        if (RootGrid?.XamlRoot is not null)
        {
            RootGrid.XamlRoot.Changed -= OnXamlRootChanged;
        }

        _rectangleSelectionLayer?.Dispose();
        _rectangleSelectionLayer = null;

        _markerLayer?.Dispose();
        _markerLayer = null;

        _baseTileLayer?.Dispose();
        _baseTileLayer = null;

        _map?.Dispose();
        _map = null;

        _settingsCts?.Cancel();
        _settingsCts?.Dispose();
        _settingsCts = null;

        _mapUpdateCts?.Cancel();
        _mapUpdateCts?.Dispose();
        _mapUpdateCts = null;

        if (PreviewPaneControl is not null)
        {
            PreviewPaneControl.MaximizeChanged -= OnPreviewMaximizeChanged;
            PreviewPaneControl.DataContext = null;
        }

        _previewPaneViewModel?.Cleanup();

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

    private async Task OpenFolderPickerAsync()
    {
        var folder = await PickFolderAsync(PickerLocationId.PicturesLibrary).ConfigureAwait(true);

        if (folder is null)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(folder.Path).ConfigureAwait(true);
    }

    private async Task<StorageFolder?> PickFolderAsync(PickerLocationId startLocation)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = startLocation
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        try
        {
            return await picker.PickSingleFolderAsync().AsTask().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
        }

        return null;
    }

    private async void OnStatusPrimaryActionClicked(object sender, RoutedEventArgs e)
    {
        await PerformStatusActionAsync(_viewModel.StatusPrimaryAction).ConfigureAwait(true);
    }

    private async void OnStatusSecondaryActionClicked(object sender, RoutedEventArgs e)
    {
        await PerformStatusActionAsync(_viewModel.StatusSecondaryAction).ConfigureAwait(true);
    }

    private async Task PerformStatusActionAsync(StatusAction action)
    {
        switch (action)
        {
            case StatusAction.OpenFolder:
                await OpenFolderPickerAsync().ConfigureAwait(true);
                break;
            case StatusAction.GoHome:
                await _viewModel.OpenHomeAsync().ConfigureAwait(true);
                break;
            case StatusAction.ResetFilters:
                _viewModel.ResetFilters();
                await _viewModel.RefreshAsync().ConfigureAwait(true);
                break;
        }
    }

    private async void OnBreadcrumbItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (_suppressBreadcrumbNavigation)
        {
            return;
        }

        if (args.Item is not BreadcrumbSegment segment)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(segment.FullPath).ConfigureAwait(true);
    }

    private void OnBreadcrumbPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var container = FindAncestor<BreadcrumbBarItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not BreadcrumbSegment segment)
        {
            return;
        }

        if (segment.Children.Count == 0 || container.ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetCurrentPoint(container);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = point.Position;
        const double separatorHitWidth = 18;
        if (position.X < container.ActualWidth - separatorHitWidth)
        {
            return;
        }

        ShowBreadcrumbChildrenFlyout(container, segment);
        e.Handled = true;
    }

    private async void OnBreadcrumbChildClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string folderPath)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);
    }

    private void ShowBreadcrumbChildrenFlyout(FrameworkElement anchor, BreadcrumbSegment segment)
    {
        if (segment.Children.Count == 0)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var child in segment.Children)
        {
            var item = new MenuFlyoutItem
            {
                Text = child.Name,
                Tag = child.FullPath
            };
            item.Click += OnBreadcrumbChildClicked;
            flyout.Items.Add(item);
        }

        _suppressBreadcrumbNavigation = true;
        flyout.Closed += (_, _) => _suppressBreadcrumbNavigation = false;
        flyout.ShowAt(anchor);
    }

    private void OnBreadcrumbDragOver(object sender, DragEventArgs e)
    {
        if (!IsInternalDrag(e))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        if (sender is not BreadcrumbBar breadcrumbBar)
        {
            return;
        }

        if (TryGetBreadcrumbTarget(breadcrumbBar, e, out _))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }

        e.Handled = true;
    }

    private async void OnBreadcrumbDrop(object sender, DragEventArgs e)
    {
        if (!IsInternalDrag(e))
        {
            return;
        }

        if (sender is not BreadcrumbBar breadcrumbBar)
        {
            return;
        }

        if (!TryGetBreadcrumbTarget(breadcrumbBar, e, out var target))
        {
            return;
        }

        await MoveItemsToFolderAsync(_dragItems ?? _viewModel.SelectedItems, target.FullPath)
            .ConfigureAwait(true);
    }

    private void OnFileListDragOver(object sender, DragEventArgs e)
    {
        if (IsInternalDrag(e))
        {
            if (sender is not ListViewBase)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.Handled = true;
                return;
            }

            if (sender is ListViewBase listView
                && TryGetDropTargetFolder(listView, RootGrid, e, out _))
            {
                e.AcceptedOperation = DataPackageOperation.Move;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }

            e.Handled = true;
            return;
        }

        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private async void OnFileListDrop(object sender, DragEventArgs e)
    {
        if (IsInternalDrag(e))
        {
            if (sender is not ListViewBase)
            {
                return;
            }

            if (sender is ListViewBase listView
                && TryGetDropTargetFolder(listView, RootGrid, e, out var targetFolder))
            {
                await MoveItemsToFolderAsync(_dragItems ?? _viewModel.SelectedItems, targetFolder.FilePath)
                    .ConfigureAwait(true);
            }

            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        if (items is null || items.Count == 0)
        {
            return;
        }

        StorageFolder? folder = null;
        StorageFile? firstFile = null;
        foreach (var item in items)
        {
            if (item is StorageFolder droppedFolder)
            {
                folder = droppedFolder;
                break;
            }

            if (firstFile is null && item is StorageFile droppedFile)
            {
                firstFile = droppedFile;
            }
        }

        if (folder is not null)
        {
            await _viewModel.LoadFolderAsync(folder.Path).ConfigureAwait(true);
            return;
        }

        if (firstFile is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(firstFile.Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        await _viewModel.LoadFolderAsync(directory).ConfigureAwait(true);
        _viewModel.SelectItemByPath(firstFile.Path);
    }

    private void OnFileItemsDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragItems = e.Items.OfType<PhotoListItem>().ToList();
        if (_dragItems.Count == 0 && _viewModel.SelectedItems.Count > 0)
        {
            _dragItems = _viewModel.SelectedItems.ToList();
        }
        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.Properties[InternalDragKey] = true;
    }

    private void OnFileItemsDragCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        _dragItems = null;
    }

    private void OnFileListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not ListViewBase listView)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (source is not null)
        {
            var container = FindAncestor<SelectorItem>(source);
            if (container is not null
                && listView.ItemFromContainer(container) is PhotoListItem item)
            {
                _viewModel.SelectedItem = item;
            }
            else
            {
                listView.SelectedItems.Clear();
                _viewModel.SelectedItem = null;
                _viewModel.UpdateSelection(Array.Empty<PhotoListItem>());
            }
        }

        var flyout = BuildFileContextFlyout();
        flyout.ShowAt(listView, e.GetPosition(listView));
        e.Handled = true;
    }

    private void OnFileItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PhotoListItem)
        {
            return;
        }
    }

    private async void OnFileItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not ListViewBase listView)
        {
            return;
        }

        var container = FindAncestor<SelectorItem>(e.OriginalSource as DependencyObject);
        if (container is null || listView.ItemFromContainer(container) is not PhotoListItem item)
        {
            return;
        }

        if (!item.IsFolder)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(item.FilePath).ConfigureAwait(true);
        e.Handled = true;
    }

    private void SetMapMarkers(List<(double Latitude, double Longitude, PhotoMetadata Metadata, PhotoItem Item)> items)
    {
        if (_map is null || _markerLayer is null)
        {
            return;
        }

        var features = new List<IFeature>(items.Count);
        var hasBounds = false;
        var minX = 0d;
        var minY = 0d;
        var maxX = 0d;
        var maxY = 0d;

        foreach (var item in items)
        {
            var position = SphericalMercator.FromLonLat(new MPoint(item.Longitude, item.Latitude));
            if (!hasBounds)
            {
                minX = maxX = position.X;
                minY = maxY = position.Y;
                hasBounds = true;
            }
            else
            {
                minX = Math.Min(minX, position.X);
                maxX = Math.Max(maxX, position.X);
                minY = Math.Min(minY, position.Y);
                maxY = Math.Max(maxY, position.Y);
            }

            var feature = new PointFeature(position);
            feature.Styles.Clear();
            foreach (var style in CreatePinStyles(item.Metadata))
            {
                feature.Styles.Add(style);
            }
            feature[PhotoMetadataKey] = item.Metadata;
            feature[PhotoItemKey] = item.Item;
            features.Add(feature);
        }

        _markerLayer.Features = features;
        _map.Refresh();

        if (!hasBounds)
        {
            return;
        }

        var spanX = maxX - minX;
        var spanY = maxY - minY;
        var padding = Math.Max(spanX, spanY) * 0.1;
        if (padding <= 0)
        {
            padding = 500;
        }

        var bounds = new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
        _map.Navigator.ZoomToBox(bounds, MBoxFit.Fit, 0, Mapsui.Animations.Easing.CubicOut);
    }

    private async void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListViewBase listView)
        {
            return;
        }

        var selected = listView.SelectedItems
            .OfType<PhotoListItem>()
            .ToList();
        _viewModel.UpdateSelection(selected);
        await UpdateMapFromSelectionAsync().ConfigureAwait(true);
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnCreateFolderClicked(object sender, RoutedEventArgs e)
    {
        var currentFolder = _viewModel.CurrentFolderPath;
        if (string.IsNullOrWhiteSpace(currentFolder))
        {
            return;
        }

        var folderName = await ShowTextInputDialogAsync(
            LocalizationService.GetString("Dialog.NewFolder.Title"),
            LocalizationService.GetString("Dialog.NewFolder.Primary"),
            LocalizationService.GetString("Dialog.NewFolder.DefaultName"),
            LocalizationService.GetString("Dialog.NewFolder.Placeholder")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        if (ContainsInvalidFileNameChars(folderName))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.InvalidName.Title"),
                LocalizationService.GetString("Dialog.InvalidName.Detail")).ConfigureAwait(true);
            return;
        }

        var targetPath = Path.Combine(currentFolder, folderName);
        if (Directory.Exists(targetPath) || File.Exists(targetPath))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                LocalizationService.GetString("Dialog.AlreadyExists.Detail")).ConfigureAwait(true);
            return;
        }

        try
        {
            Directory.CreateDirectory(targetPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or IOException
            or NotSupportedException
            or ArgumentException
            or PathTooLongException)
        {
            AppLog.Error($"Failed to create folder: {targetPath}", ex);
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.CreateFolderFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")).ConfigureAwait(true);
            return;
        }

        await _viewModel.RefreshAsync().ConfigureAwait(true);
        _viewModel.SelectItemByPath(targetPath);
    }

    private async void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItems.Count != 1 || _viewModel.SelectedItems[0] is not PhotoListItem item)
        {
            return;
        }

        var parent = Path.GetDirectoryName(item.FilePath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.RenameNotAvailable.Title"),
                LocalizationService.GetString("Dialog.RenameNotAvailable.Detail")).ConfigureAwait(true);
            return;
        }

        var newName = await ShowTextInputDialogAsync(
            LocalizationService.GetString("Dialog.Rename.Title"),
            LocalizationService.GetString("Dialog.Rename.Primary"),
            item.FileName,
            LocalizationService.GetString("Dialog.Rename.Placeholder")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var normalizedName = NormalizeRename(item, newName);
        if (string.Equals(normalizedName, item.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (ContainsInvalidFileNameChars(normalizedName))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.InvalidName.Title"),
                LocalizationService.GetString("Dialog.InvalidName.Detail")).ConfigureAwait(true);
            return;
        }

        var targetPath = Path.Combine(parent, normalizedName);
        if (Directory.Exists(targetPath) || File.Exists(targetPath))
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                LocalizationService.GetString("Dialog.AlreadyExists.Detail")).ConfigureAwait(true);
            return;
        }

        try
        {
            if (item.IsFolder)
            {
                Directory.Move(item.FilePath, targetPath);
            }
            else
            {
                File.Move(item.FilePath, targetPath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or IOException
            or NotSupportedException
            or ArgumentException
            or PathTooLongException)
        {
            AppLog.Error($"Failed to rename item: {item.FilePath}", ex);
            await ShowMessageDialogAsync(
                LocalizationService.GetString("Dialog.RenameFailed.Title"),
                LocalizationService.GetString("Dialog.SeeLogDetail")).ConfigureAwait(true);
            return;
        }

        await _viewModel.RefreshAsync().ConfigureAwait(true);
        _viewModel.SelectItemByPath(targetPath);
    }

    private async void OnMoveClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItems.Count == 0)
        {
            return;
        }

        // 単一フォルダが選択されている場合、そのフォルダに遷移する（ダブルクリックと同じ動作）
        if (_viewModel.SelectedItems.Count == 1 && _viewModel.SelectedItems[0].IsFolder)
        {
            await _viewModel.LoadFolderAsync(_viewModel.SelectedItems[0].FilePath).ConfigureAwait(true);
            return;
        }

        // それ以外の場合、選択された項目を移動先フォルダへ移動する
        var destination = await PickFolderAsync(PickerLocationId.PicturesLibrary).ConfigureAwait(true);
        if (destination is null)
        {
            return;
        }

        await MoveItemsToFolderAsync(_viewModel.SelectedItems, destination.Path).ConfigureAwait(true);
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItems.Count == 0)
        {
            return;
        }

        var message = _viewModel.SelectedItems.Count == 1
            ? BuildDeleteMessage(_viewModel.SelectedItems[0])
            : LocalizationService.Format("Dialog.DeleteConfirm.Multiple", _viewModel.SelectedItems.Count);
        var confirmed = await ShowConfirmationDialogAsync(
            LocalizationService.GetString("Dialog.DeleteConfirm.Title"),
            message,
            LocalizationService.GetString("Common.Delete")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await DeleteItemsAsync(_viewModel.SelectedItems).ConfigureAwait(true);
    }

    private MenuFlyout BuildFileContextFlyout()
    {
        var flyout = new MenuFlyout();

        var createFolder = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.NewFolder"),
            Icon = new SymbolIcon(Symbol.Folder),
            IsEnabled = _viewModel.CanCreateFolder
        };
        createFolder.Click += OnCreateFolderClicked;

        var renameItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Rename"),
            Icon = new SymbolIcon(Symbol.Edit),
            IsEnabled = _viewModel.CanRenameSelection
        };
        renameItem.Click += OnRenameClicked;

        var moveItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Move"),
            Icon = new SymbolIcon(Symbol.Forward),
            IsEnabled = _viewModel.CanModifySelection
        };
        moveItem.Click += OnMoveClicked;

        var moveParentItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.MoveToParent"),
            Icon = new SymbolIcon(Symbol.Up),
            IsEnabled = _viewModel.CanMoveToParentSelection
        };
        moveParentItem.Click += OnMoveToParentClicked;

        var deleteItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.Delete"),
            Icon = new SymbolIcon(Symbol.Delete),
            IsEnabled = _viewModel.CanModifySelection
        };
        deleteItem.Click += OnDeleteClicked;

        var editExifItem = new MenuFlyoutItem
        {
            Text = LocalizationService.GetString("Menu.EditExif"),
            Icon = new SymbolIcon(Symbol.Edit),
            IsEnabled = _viewModel.CanRenameSelection && IsJpegFile(_viewModel.SelectedItem)
        };
        editExifItem.Click += OnEditExifClicked;

        flyout.Items.Add(createFolder);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(renameItem);
        flyout.Items.Add(moveItem);
        flyout.Items.Add(moveParentItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(editExifItem);
        flyout.Items.Add(deleteItem);

        return flyout;
    }

    private static bool IsInternalDrag(DragEventArgs e)
    {
        if (!e.DataView.Properties.TryGetValue(InternalDragKey, out var value))
        {
            return false;
        }

        return value is bool isInternal && isInternal;
    }

    private static bool TryGetDropTargetFolder(ListViewBase listView, UIElement root, DragEventArgs e, out PhotoListItem target)
    {
        target = null!;
        var point = e.GetPosition(root);
        var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, root);
        foreach (var element in elements)
        {
            var container = element as SelectorItem ?? FindAncestor<SelectorItem>(element);
            if (container is null)
            {
                continue;
            }

            if (!IsDescendantOf(container, listView))
            {
                continue;
            }

            if (listView.ItemFromContainer(container) is not PhotoListItem item || !item.IsFolder)
            {
                continue;
            }

            target = item;
            return true;
        }

        return false;
    }

    private bool TryGetBreadcrumbTarget(BreadcrumbBar breadcrumbBar, DragEventArgs e, out BreadcrumbSegment target)
    {
        target = null!;
        var point = e.GetPosition(RootGrid);
        var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, RootGrid);
        foreach (var element in elements)
        {
            var container = element as BreadcrumbBarItem ?? FindAncestor<BreadcrumbBarItem>(element);
            if (container is null)
            {
                continue;
            }

            if (!IsDescendantOf(container, breadcrumbBar))
            {
                continue;
            }

            if (container.DataContext is not BreadcrumbSegment segment)
            {
                continue;
            }

            target = segment;
            return true;
        }

        return false;
    }

    private async Task MoveItemsToFolderAsync(IReadOnlyList<PhotoListItem>? items, string destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            return;
        }

        if (items is null || items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            var sourcePath = item.FilePath;
            var parent = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            if (IsSamePath(parent, destinationFolder))
            {
                continue;
            }

            if (item.IsFolder && IsDescendantPath(sourcePath, destinationFolder))
            {
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.MoveFailed.Title"),
                    LocalizationService.GetString("Dialog.MoveIntoSelf.Detail")).ConfigureAwait(true);
                return;
            }

            var targetPath = Path.Combine(destinationFolder, item.FileName);
            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.AlreadyExists.Title"),
                    LocalizationService.GetString("Dialog.AlreadyExistsDestination.Detail")).ConfigureAwait(true);
                return;
            }

            try
            {
                if (item.IsFolder)
                {
                    Directory.Move(sourcePath, targetPath);
                }
                else
                {
                    File.Move(sourcePath, targetPath);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                or IOException
                or NotSupportedException
                or ArgumentException
                or PathTooLongException)
            {
                AppLog.Error($"Failed to move item: {sourcePath}", ex);
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.MoveFailed.Title"),
                    LocalizationService.GetString("Dialog.SeeLogDetail")).ConfigureAwait(true);
                return;
            }
        }

        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private static bool IsSamePath(string left, string right)
    {
        var normalizedLeft = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDescendantPath(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject? source) where T : DependencyObject
    {
        if (source is null)
        {
            yield break;
        }

        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(source);
        for (var index = 0; index < count; index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(source, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject ancestor)
    {
        var current = child;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private async Task DeleteItemsAsync(IReadOnlyList<PhotoListItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsFolder && Directory.GetParent(item.FilePath) is null)
            {
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.DeleteNotAvailable.Title"),
                    LocalizationService.GetString("Dialog.DeleteNotAvailable.Detail")).ConfigureAwait(true);
                return;
            }

            try
            {
                if (item.IsFolder)
                {
                    Directory.Delete(item.FilePath, recursive: true);
                }
                else
                {
                    File.Delete(item.FilePath);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                or IOException
                or NotSupportedException
                or ArgumentException
                or PathTooLongException)
            {
                AppLog.Error($"Failed to delete item: {item.FilePath}", ex);
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.DeleteFailed.Title"),
                    LocalizationService.GetString("Dialog.SeeLogDetail")).ConfigureAwait(true);
                return;
            }
        }

        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private static string BuildDeleteMessage(PhotoListItem item)
    {
        return item.IsFolder
            ? LocalizationService.Format("Dialog.DeleteConfirm.Folder", item.FileName)
            : LocalizationService.Format("Dialog.DeleteConfirm.File", item.FileName);
    }

    private async Task<string?> ShowTextInputDialogAsync(
        string title,
        string primaryButtonText,
        string? defaultText,
        string placeholderText)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return null;
        }

        var textBox = new TextBox
        {
            Text = defaultText ?? string.Empty,
            PlaceholderText = placeholderText,
            MinWidth = 260
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = LocalizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        textBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        };
        dialog.Opened += (_, _) =>
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        };

        var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        var value = textBox.Text.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<bool> ShowConfirmationDialogAsync(
        string title,
        string message,
        string primaryButtonText)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = LocalizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        return result == ContentDialogResult.Primary;
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
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private async void OnEditExifClicked(object sender, RoutedEventArgs e)
    {
        // Validate selection
        if (_viewModel.SelectedItems.Count != 1)
        {
            await ShowMessageDialogAsync(
                LocalizationService.GetString("ExifEditor.Title"),
                LocalizationService.GetString("Message.ExifEditorMultipleFiles")).ConfigureAwait(true);
            return;
        }

        var item = _viewModel.SelectedItems[0];
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
            await _viewModel.RefreshAsync().ConfigureAwait(true);
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

    private void HideMapStatusForExifPick()
    {
        if (MapStatusOverlay is null || MapStatusPanel is null)
        {
            return;
        }

        if (MapStatusOverlay.Visibility == Visibility.Collapsed && MapStatusPanel.Visibility == Visibility.Collapsed)
        {
            return;
        }

        _restoreMapStatusAfterExifPick = true;
        _exifPickPreviousStatusOverlayVisibility = MapStatusOverlay.Visibility;
        _exifPickPreviousStatusPanelVisibility = MapStatusPanel.Visibility;
        MapStatusOverlay.Visibility = Visibility.Collapsed;
        MapStatusPanel.Visibility = Visibility.Collapsed;
    }

    private void RestoreMapStatusAfterExifPick()
    {
        if (!_restoreMapStatusAfterExifPick)
        {
            return;
        }

        _restoreMapStatusAfterExifPick = false;
        if (MapStatusOverlay is not null)
        {
            MapStatusOverlay.Visibility = _exifPickPreviousStatusOverlayVisibility;
        }

        if (MapStatusPanel is not null)
        {
            MapStatusPanel.Visibility = _exifPickPreviousStatusPanelVisibility;
        }
    }

    private Task<(double Latitude, double Longitude)?> PickExifLocationAsync()
    {
        if (MapControl is null || _map is null)
        {
            _viewModel.ShowNotificationMessage(
                LocalizationService.GetString("Message.ExifPickLocationUnavailable"),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            return Task.FromResult<(double Latitude, double Longitude)?>(null);
        }

        if (_exifLocationPicker is not null)
        {
            return _exifLocationPicker.Task;
        }

        _isPickingExifLocation = true;
        HideMapStatusForExifPick();
        _exifLocationPicker = new TaskCompletionSource<(double Latitude, double Longitude)?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _viewModel.ShowNotificationMessage(
            LocalizationService.GetString("Message.ExifPickLocationInstruction"),
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        return _exifLocationPicker.Task;
    }

    private void CompleteExifLocationPick(double latitude, double longitude)
    {
        if (!_isPickingExifLocation)
        {
            return;
        }

        _isPickingExifLocation = false;
        RestoreMapStatusAfterExifPick();
        var picker = _exifLocationPicker;
        _exifLocationPicker = null;
        _viewModel.ShowNotificationMessage(string.Empty, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        picker?.TrySetResult((latitude, longitude));
    }

    private void CancelExifLocationPick()
    {
        if (!_isPickingExifLocation)
        {
            return;
        }

        _isPickingExifLocation = false;
        RestoreMapStatusAfterExifPick();
        var picker = _exifLocationPicker;
        _exifLocationPicker = null;
        _viewModel.ShowNotificationMessage(
            LocalizationService.GetString("Message.ExifPickLocationCanceled"),
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        picker?.TrySetResult(null);
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

    private static bool ContainsInvalidFileNameChars(string name)
    {
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }

    private static string NormalizeRename(PhotoListItem item, string newName)
    {
        var trimmed = newName.Trim();
        if (item.IsFolder)
        {
            return trimmed;
        }

        var originalExtension = Path.GetExtension(item.FileName);
        if (string.IsNullOrWhiteSpace(originalExtension))
        {
            return trimmed;
        }

        var newExtension = Path.GetExtension(trimmed);
        if (string.IsNullOrWhiteSpace(newExtension))
        {
            return $"{trimmed}{originalExtension}";
        }

        return trimmed;
    }

    private async void OnMoveToParentClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedItems.Count == 0)
        {
            return;
        }

        var currentFolder = _viewModel.CurrentFolderPath;
        if (string.IsNullOrWhiteSpace(currentFolder))
        {
            return;
        }

        var parent = Directory.GetParent(currentFolder);
        if (parent is null)
        {
            return;
        }

        await MoveItemsToFolderAsync(_viewModel.SelectedItems, parent.FullName).ConfigureAwait(true);
    }

    private void OnDetailsSortClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
        {
            return;
        }

        if (!Enum.TryParse(tag, out FileSortColumn column))
        {
            return;
        }

        _viewModel.ToggleSort(column);
    }

    private void OnMapInfoReceived(object? sender, MapInfoEventArgs e)
    {
        if (e is null || MapControl.Map?.Layers is null)
        {
            return;
        }

        var mapInfo = e.GetMapInfo(MapControl.Map.Layers);
        if (mapInfo?.Feature is not PointFeature feature)
        {
            return;
        }

        if (_markerLayer is null || !_markerLayer.Features.Contains(feature))
        {
            return;
        }

        var itemObj = feature[PhotoItemKey];
        var metadataObj = feature[PhotoMetadataKey];

        if (itemObj is null || metadataObj is null)
        {
            AppLog.Info("Marker clicked but missing PhotoItem or PhotoMetadata.");
            return;
        }

        if (itemObj is not PhotoItem photoItem || metadataObj is not PhotoMetadata metadata)
        {
            return;
        }

        FocusPhotoItem(photoItem);
        ShowMarkerFlyout(photoItem, metadata);
    }

    private void ShowMarkerFlyout(PhotoItem photoItem, PhotoMetadata metadata)
    {
        _flyoutMetadata = metadata;

        FlyoutTakenAtLabel.Text = LocalizationService.GetString("Flyout.TakenAtLabel.Text");
        FlyoutTakenAt.Text = metadata.TakenAtText ?? "-";

        if (!string.IsNullOrWhiteSpace(metadata.CameraSummary))
        {
            FlyoutCameraLabel.Text = LocalizationService.GetString("Flyout.CameraLabel.Text");
            FlyoutCamera.Text = metadata.CameraSummary;
            FlyoutCameraPanel.Visibility = Visibility.Visible;
        }
        else
        {
            FlyoutCameraPanel.Visibility = Visibility.Collapsed;
        }

        FlyoutFileLabel.Text = LocalizationService.GetString("Flyout.FileLabel.Text");
        FlyoutFileName.Text = photoItem.FileName;

        if (!string.IsNullOrWhiteSpace(photoItem.ResolutionText))
        {
            FlyoutResolutionLabel.Text = LocalizationService.GetString("Flyout.ResolutionLabel.Text");
            FlyoutResolution.Text = photoItem.ResolutionText;
            FlyoutResolutionPanel.Visibility = Visibility.Visible;
        }
        else
        {
            FlyoutResolutionPanel.Visibility = Visibility.Collapsed;
        }

        FlyoutGoogleMapsLink.Content = LocalizationService.GetString("Flyout.GoogleMapsButton.Content");

        MarkerFlyout.ShowAt(MapControl);
    }

    private void FocusPhotoItem(PhotoItem photoItem)
    {
        var target = _viewModel.Items.FirstOrDefault(item
            => !item.IsFolder
               && string.Equals(item.FilePath, photoItem.FilePath, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        _viewModel.SelectedItem = target;

        var listView = GetFileListView();
        if (listView is null)
        {
            return;
        }
        listView.ScrollIntoView(target);
    }

    private ListViewBase? GetFileListView()
    {
        if (RootGrid is null)
        {
            return null;
        }

        var candidates = FindDescendants<ListViewBase>(RootGrid)
            .Where(listView => ReferenceEquals(listView.ItemsSource, _viewModel.Items))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates.FirstOrDefault(listView => listView.Visibility == Visibility.Visible)
            ?? candidates[0];
    }

    private async void OnGoogleMapsLinkClicked(object sender, RoutedEventArgs e)
    {
        if (_flyoutMetadata?.HasLocation != true)
        {
            return;
        }

        var url = GenerateGoogleMapsUrl(_flyoutMetadata.Latitude!.Value, _flyoutMetadata.Longitude!.Value);

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            try
            {
                await Launcher.LaunchUriAsync(uri);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                or System.Runtime.InteropServices.COMException
                or ArgumentException)
            {
                AppLog.Error("Failed to launch Google Maps URL.", ex);
                _viewModel.ShowNotificationMessage(
                    LocalizationService.GetString("Message.LaunchBrowserFailed"),
                    InfoBarSeverity.Error);
            }
        }

        MarkerFlyout.Hide();
    }

    private static string GenerateGoogleMapsUrl(double latitude, double longitude)
    {
        return $"https://www.google.com/maps?q={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private void OnMapPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (MapControl is null || _map is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(MapControl);
        if (_isPickingExifLocation)
        {
            if (point.Properties.IsRightButtonPressed)
            {
                CancelExifLocationPick();
                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isExifPickPointerActive = true;
            _exifPickPointerStart = point.Position;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Ctrl キーが押されている場合のみ矩形選択を有効化
        var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (!ctrlPressed)
        {
            return;
        }

        var worldStart = GetWorldPosition(e);
        if (worldStart is null)
        {
            return;
        }

        LockMapPan();
        _mapRectangleSelecting = true;
        _mapRectangleStart = worldStart;
        MapControl.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMapPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var rectangleStart = _mapRectangleStart;
        if (!_mapRectangleSelecting || MapControl is null || _map is null || rectangleStart is null)
        {
            return;
        }

        var worldEnd = GetWorldPosition(e);
        if (worldEnd is null)
        {
            return;
        }

        UpdateRectangleSelectionLayer(rectangleStart, worldEnd);
        e.Handled = true;
    }

    private void OnMapPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (MapControl is null || _map is null)
        {
            return;
        }

        if (_isPickingExifLocation)
        {
            if (!_isExifPickPointerActive)
            {
                return;
            }

            _isExifPickPointerActive = false;
            var startPoint = _exifPickPointerStart;
            _exifPickPointerStart = null;

            if (startPoint is null)
            {
                return;
            }

            var currentPoint = e.GetCurrentPoint(MapControl).Position;
            var deltaX = currentPoint.X - startPoint.Value.X;
            var deltaY = currentPoint.Y - startPoint.Value.Y;
            if (Math.Abs(deltaX) > 6 || Math.Abs(deltaY) > 6)
            {
                return;
            }

            var worldPosition = GetWorldPosition(e);
            if (worldPosition is null)
            {
                return;
            }

            var lonLat = SphericalMercator.ToLonLat(worldPosition);
            CompleteExifLocationPick(lonLat.Y, lonLat.X);
            e.Handled = true;
            return;
        }

        var rectangleStart = _mapRectangleStart;
        _mapRectangleSelecting = false;
        _mapRectangleStart = null;
        MapControl.ReleasePointerCapture(e.Pointer);
        RestoreMapPanLock();

        if (rectangleStart is null)
        {
            ClearRectangleSelectionLayer();
            return;
        }

        var worldEnd = GetWorldPosition(e);
        if (worldEnd is null)
        {
            ClearRectangleSelectionLayer();
            return;
        }

        var minX = Math.Min(rectangleStart.X, worldEnd.X);
        var maxX = Math.Max(rectangleStart.X, worldEnd.X);
        var minY = Math.Min(rectangleStart.Y, worldEnd.Y);
        var maxY = Math.Max(rectangleStart.Y, worldEnd.Y);
        var selectionBounds = new MRect(minX, minY, maxX, maxY);

        SelectPhotosInRectangle(selectionBounds);
        ClearRectangleSelectionLayer();
        e.Handled = true;
    }

    private void OnMapPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _mapRectangleSelecting = false;
        _mapRectangleStart = null;
        ClearRectangleSelectionLayer();
        RestoreMapPanLock();
    }

    private void LockMapPan()
    {
        if (MapControl?.Map?.Navigator is not { } navigator)
        {
            return;
        }

        if (!_mapPanLockActive)
        {
            _mapPanLockBeforeSelection = navigator.PanLock;
            _mapPanLockActive = true;
        }

        navigator.PanLock = true;
    }

    private void RestoreMapPanLock()
    {
        if (!_mapPanLockActive)
        {
            return;
        }

        if (MapControl?.Map?.Navigator is not { } navigator)
        {
            return;
        }

        navigator.PanLock = _mapPanLockBeforeSelection;
        _mapPanLockActive = false;
    }

    private MPoint? GetWorldPosition(PointerRoutedEventArgs e)
    {
        if (MapControl?.Map?.Navigator is not { } navigator)
        {
            return null;
        }

        var screenPos = e.GetCurrentPoint(MapControl).Position;
        return navigator.Viewport.ScreenToWorld(screenPos.X, screenPos.Y);
    }

    private void UpdateRectangleSelectionLayer(MPoint start, MPoint end)
    {
        if (_map is null)
        {
            return;
        }

        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);

        var polygon = new Polygon(new LinearRing(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY)
        }));

        var feature = new GeometryFeature
        {
            Geometry = polygon
        };

        var polygonStyle = new VectorStyle
        {
            Fill = new Brush(SelectionFillColor),
            Outline = new Pen(SelectionOutlineColor, 2)
        };

        feature.Styles.Add(polygonStyle);

        if (_rectangleSelectionLayer is null)
        {
            _rectangleSelectionLayer = new MemoryLayer
            {
                Name = "RectangleSelection",
                Features = new[] { feature },
                Style = null
            };
            _map.Layers.Add(_rectangleSelectionLayer);
        }
        else
        {
            _rectangleSelectionLayer.Features = new[] { feature };
        }

        _map.Refresh();
    }

    private void ClearRectangleSelectionLayer()
    {
        if (_map is null || _rectangleSelectionLayer is null)
        {
            return;
        }

        _map.Layers.Remove(_rectangleSelectionLayer);
        _rectangleSelectionLayer.Dispose();
        _rectangleSelectionLayer = null;
        _map.Refresh();
    }

    private void SelectPhotosInRectangle(MRect selectionBounds)
    {
        if (_markerLayer is null || _map is null)
        {
            return;
        }

        var selectedItems = new List<PhotoListItem>();

        foreach (var feature in _markerLayer.Features)
        {
            if (feature is not PointFeature pointFeature)
            {
                continue;
            }

            var point = pointFeature.Point;
            if (point is null)
            {
                continue;
            }

            if (point.X >= selectionBounds.Min.X && point.X <= selectionBounds.Max.X
                && point.Y >= selectionBounds.Min.Y && point.Y <= selectionBounds.Max.Y)
            {
                var itemObj = feature[PhotoItemKey];
                if (itemObj is PhotoItem photoItem)
                {
                    var listItem = _viewModel.Items.FirstOrDefault(item =>
                        !item.IsFolder && string.Equals(item.FilePath, photoItem.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (listItem is not null)
                    {
                        selectedItems.Add(listItem);
                    }
                }
            }
        }

        var listView = GetFileListView();
        if (listView is not null)
        {
            listView.SelectedItems.Clear();
            foreach (var selectedItem in selectedItems)
            {
                listView.SelectedItems.Add(selectedItem);
            }
        }
        else
        {
            _viewModel.UpdateSelection(selectedItems);
        }

        _viewModel.SelectedItem = selectedItems.Count > 0 ? selectedItems[0] : null;

        if (selectedItems.Count > 0 && listView is not null)
        {
            listView.ScrollIntoView(selectedItems[0]);
        }
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
