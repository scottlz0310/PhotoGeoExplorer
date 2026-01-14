using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BruTile.Cache;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Mapsui.UI.WinUI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Globalization;
using PhotoGeoExplorer.Models;
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
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private bool _layoutStored;
    private bool _mapInitialized;
    private Map? _map;
    private Mapsui.Tiling.Layers.TileLayer? _baseTileLayer;
    private MemoryLayer? _markerLayer;
    private bool _previewFitToWindow = true;
    private bool _previewMaximized;
    private bool _windowSized;
    private bool _windowIconSet;
    private bool _suppressBreadcrumbNavigation;
    private CancellationTokenSource? _mapUpdateCts;
    private CancellationTokenSource? _settingsCts;
    private GridLength _storedDetailWidth;
    private GridLength _storedFileBrowserWidth;
    private GridLength _storedMapRowHeight;
    private GridLength _storedMapSplitterHeight;
    private GridLength _storedSplitterWidth;
    private double _storedMapRowMinHeight;
    private bool _previewDragging;
    private Point _previewDragStart;
    private double _previewStartHorizontalOffset;
    private double _previewStartVerticalOffset;
    private List<PhotoListItem>? _dragItems;
    private bool _isApplyingSettings;
    private string? _languageOverride;
    private string? _startupFilePath;
    private ThemePreference _themePreference = ThemePreference.System;
    private int _mapDefaultZoomLevel = DefaultMapZoomLevel;
    private MapTileSourceType _mapTileSource = MapTileSourceType.OpenStreetMap;
    private PhotoMetadata? _flyoutMetadata;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new FileSystemService());
        _settingsService = new SettingsService();
        RootGrid.DataContext = _viewModel;
        Title = LocalizationService.GetString("MainWindow.Title");
        AppLog.Info("MainWindow constructed.");
        Activated += OnActivated;
        Closed += OnClosed;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
            const string userAgent = "PhotoGeoExplorer/1.4.0 (scott.lz0310@gmail.com)";
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
            const string userAgent = "PhotoGeoExplorer/1.4.0 (scott.lz0310@gmail.com)";
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
        UpdateMapZoomMenuChecks(_mapDefaultZoomLevel);
        _mapTileSource = Enum.IsDefined(settings.MapTileSource) ? settings.MapTileSource : MapTileSourceType.OpenStreetMap;
        UpdateMapTileSourceMenuChecks(_mapTileSource);

        _viewModel.ShowImagesOnly = settings.ShowImagesOnly;
        _viewModel.FileViewMode = Enum.IsDefined<FileViewMode>(settings.FileViewMode)
            ? settings.FileViewMode
            : FileViewMode.Details;

        if (!string.IsNullOrWhiteSpace(settings.LastFolderPath)
            && Directory.Exists(settings.LastFolderPath))
        {
            await _viewModel.LoadFolderAsync(settings.LastFolderPath).ConfigureAwait(true);
        }
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
            MapTileSource = _mapTileSource
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

    private void OnPreviewImageOpened(object sender, RoutedEventArgs e)
    {
        _previewFitToWindow = true;
        ApplyPreviewFit();
    }

    private void OnPreviewScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_previewFitToWindow)
        {
            ApplyPreviewFit();
        }
    }

    private void OnPreviewFitClicked(object sender, RoutedEventArgs e)
    {
        _previewFitToWindow = true;
        ApplyPreviewFit();
    }

    private void OnPreviewZoomInClicked(object sender, RoutedEventArgs e)
    {
        _previewFitToWindow = false;
        AdjustPreviewZoom(1.2f);
    }

    private void OnPreviewZoomOutClicked(object sender, RoutedEventArgs e)
    {
        _previewFitToWindow = false;
        AdjustPreviewZoom(1f / 1.2f);
    }

    private void OnPreviewMaximizeChecked(object sender, RoutedEventArgs e)
    {
        TogglePreviewMaximize(true);
    }

    private void OnPreviewMaximizeUnchecked(object sender, RoutedEventArgs e)
    {
        TogglePreviewMaximize(false);
    }

    private void OnPreviewNextClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectNext();
    }

    private void OnPreviewPreviousClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectPrevious();
    }

    private void AdjustPreviewZoom(float multiplier)
    {
        if (PreviewScrollViewer is null)
        {
            return;
        }

        var current = PreviewScrollViewer.ZoomFactor;
        var target = current * multiplier;
        var clamped = Math.Clamp(target, PreviewScrollViewer.MinZoomFactor, PreviewScrollViewer.MaxZoomFactor);
        PreviewScrollViewer.ChangeView(null, null, clamped, true);
    }

    private void OnPreviewPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (PreviewScrollViewer is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(PreviewScrollViewer);
        if (point.Properties.MouseWheelDelta == 0)
        {
            return;
        }

        _previewFitToWindow = false;

        var multiplier = point.Properties.MouseWheelDelta > 0 ? 1.1f : 1f / 1.1f;
        var current = PreviewScrollViewer.ZoomFactor;
        var target = Math.Clamp(current * multiplier, PreviewScrollViewer.MinZoomFactor, PreviewScrollViewer.MaxZoomFactor);
        if (Math.Abs(target - current) < 0.0001f)
        {
            return;
        }

        var cursor = point.Position;
        var contentX = (PreviewScrollViewer.HorizontalOffset + cursor.X) / current;
        var contentY = (PreviewScrollViewer.VerticalOffset + cursor.Y) / current;
        var targetOffsetX = contentX * target - cursor.X;
        var targetOffsetY = contentY * target - cursor.Y;

        PreviewScrollViewer.ChangeView(targetOffsetX, targetOffsetY, target, true);
        e.Handled = true;
    }

    private void OnPreviewPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (PreviewScrollViewer is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(PreviewScrollViewer);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _previewFitToWindow = false;
        _previewDragging = true;
        _previewDragStart = point.Position;
        _previewStartHorizontalOffset = PreviewScrollViewer.HorizontalOffset;
        _previewStartVerticalOffset = PreviewScrollViewer.VerticalOffset;
        PreviewScrollViewer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPreviewPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewDragging || PreviewScrollViewer is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(PreviewScrollViewer).Position;
        var deltaX = point.X - _previewDragStart.X;
        var deltaY = point.Y - _previewDragStart.Y;
        PreviewScrollViewer.ChangeView(
            _previewStartHorizontalOffset - deltaX,
            _previewStartVerticalOffset - deltaY,
            null,
            true);
        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewDragging || PreviewScrollViewer is null)
        {
            return;
        }

        _previewDragging = false;
        PreviewScrollViewer.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPreviewPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _previewDragging = false;
    }

    private void ApplyPreviewFit()
    {
        if (PreviewScrollViewer is null || PreviewImage?.Source is not BitmapImage bitmap)
        {
            return;
        }

        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
        {
            return;
        }

        var viewportWidth = PreviewScrollViewer.ViewportWidth;
        var viewportHeight = PreviewScrollViewer.ViewportHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var rasterScale = RootGrid?.XamlRoot?.RasterizationScale ?? 1.0;
        var imageWidth = bitmap.PixelWidth / rasterScale;
        var imageHeight = bitmap.PixelHeight / rasterScale;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        var scaleX = viewportWidth / imageWidth;
        var scaleY = viewportHeight / imageHeight;
        var target = (float)Math.Min(scaleX, scaleY);
        var clamped = Math.Clamp(target, PreviewScrollViewer.MinZoomFactor, PreviewScrollViewer.MaxZoomFactor);
        PreviewScrollViewer.ChangeView(0, 0, clamped, true);
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
            MapRow.Height = _storedMapRowHeight;
            MapSplitterRow.Height = _storedMapSplitterHeight;
            MapRow.MinHeight = _storedMapRowMinHeight;
            FileBrowserPane.Visibility = Visibility.Visible;
            MapPane.Visibility = Visibility.Visible;
            MainSplitter.Visibility = Visibility.Visible;
            MapRowSplitter.Visibility = Visibility.Visible;
        }

        _previewFitToWindow = true;
        ApplyPreviewFit();
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

        _viewModel?.Dispose();

        GC.SuppressFinalize(this);
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

        flyout.Items.Add(createFolder);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(renameItem);
        flyout.Items.Add(moveItem);
        flyout.Items.Add(moveParentItem);
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
}
