using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using BruTile.Cache;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.WinUI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using System.ComponentModel;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _layoutStored;
    private bool _mapInitialized;
    private Map? _map;
    private ILayer? _baseTileLayer;
    private MemoryLayer? _markerLayer;
    private bool _previewFitToWindow = true;
    private bool _previewMaximized;
    private bool _windowSized;
    private GridLength _storedDetailWidth;
    private GridLength _storedFileBrowserWidth;
    private GridLength _storedMapRowHeight;
    private GridLength _storedMapSplitterHeight;
    private GridLength _storedSplitterWidth;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new FileSystemService());
        RootGrid.DataContext = _viewModel;
        AppLog.Info("MainWindow constructed.");
        Activated += OnActivated;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_mapInitialized)
        {
            return;
        }

        EnsureWindowSize();
        _mapInitialized = true;
        AppLog.Info("MainWindow activated.");
        await InitializeMapAsync().ConfigureAwait(true);
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

    private Task InitializeMapAsync()
    {
        if (_map is not null)
        {
            return Task.CompletedTask;
        }

        if (MapControl is null)
        {
            AppLog.Error("Map control is missing.");
            ShowMapStatus("Map control missing", "See log for details.", Symbol.Map);
            return Task.CompletedTask;
        }

        try
        {
            var map = new Map();
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoGeoExplorer",
                "Cache",
                "Tiles");
            Directory.CreateDirectory(cacheRoot);
            var persistentCache = new FileCache(cacheRoot, "png");
            const string userAgent = "PhotoGeoExplorer/0.1.0 (scott.lz0310@gmail.com)";
            var tileLayer = OpenStreetMap.CreateTileLayer(userAgent);
            if (tileLayer.TileSource is BruTile.Web.HttpTileSource httpTileSource)
            {
                httpTileSource.PersistentCache = persistentCache;
            }
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
            HideMapStatus();
            AppLog.Info("Map initialized.");
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Map init failed.", ex);
            ShowMapStatus("Map init failed", "See log for details.", Symbol.Map);
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error("Map init failed.", ex);
            ShowMapStatus("Map init failed", "See log for details.", Symbol.Map);
        }

        return Task.CompletedTask;
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
    }

    private Task UpdateMapFromSelectionAsync()
    {
        if (_map is null || _markerLayer is null)
        {
            return Task.CompletedTask;
        }

        var selectedItem = _viewModel.SelectedItem;
        if (selectedItem is null || selectedItem.IsFolder)
        {
            ClearMapMarkers();
            ShowMapStatus("Select a photo", "Pick an image with GPS data to show it on the map.", Symbol.Map);
            return Task.CompletedTask;
        }

        var metadata = _viewModel.SelectedMetadata;
        if (metadata?.HasLocation != true
            || metadata.Latitude is not double latitude
            || metadata.Longitude is not double longitude)
        {
            ClearMapMarkers();
            ShowMapStatus("Location data not found", "This photo has no GPS information.", Symbol.Map);
            return Task.CompletedTask;
        }

        SetMapMarker(latitude, longitude, metadata);
        HideMapStatus();
        return Task.CompletedTask;
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

    private void SetMapMarker(double latitude, double longitude, PhotoMetadata metadata)
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
        _markerLayer.Features = new[] { feature };
        _map.Refresh();

        var navigator = _map.Navigator;
        navigator.CenterOn(position, 0, Mapsui.Animations.Easing.CubicOut);
        if (navigator.Resolutions.Count > 0)
        {
            var targetLevel = Math.Clamp(14, 0, navigator.Resolutions.Count - 1);
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
            RelativeOffset = new RelativeOffset(0, -0.5)
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

        var scaleX = viewportWidth / bitmap.PixelWidth;
        var scaleY = viewportHeight / bitmap.PixelHeight;
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

    private async Task OpenFolderPickerAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        StorageFolder? folder;
        try
        {
            folder = await picker.PickSingleFolderAsync().AsTask().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
            return;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
            return;
        }

        if (folder is null)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(folder.Path).ConfigureAwait(true);
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
        if (args.Item is not BreadcrumbSegment segment)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(segment.FullPath).ConfigureAwait(true);
    }

    private void OnBreadcrumbDropDownClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not BreadcrumbSegment segment)
        {
            return;
        }

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

        flyout.ShowAt(button);
    }

    private async void OnBreadcrumbChildClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string folderPath)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(folderPath).ConfigureAwait(true);
    }

    private async void OnFileItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PhotoListItem item)
        {
            return;
        }

        if (item.IsFolder)
        {
            await _viewModel.LoadFolderAsync(item.FilePath).ConfigureAwait(true);
        }
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }
}
