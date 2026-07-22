using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Fetcher;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI;
using Mapsui.UI.WinUI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using Windows.System;
using Windows.UI.Core;

namespace PhotoGeoExplorer.Panes.Map;

internal sealed partial class MapPaneViewControl : UserControl, IDisposable
{
    private const string PhotoItemKey = "PhotoItem";
    private const string PhotoMetadataKey = "PhotoMetadata";
    private static readonly Color SelectionFillColor = Color.FromArgb(64, 0, 120, 215);
    private static readonly Color SelectionOutlineColor = Color.FromArgb(255, 0, 120, 215);

    private readonly MapExifLocationPicker _exifLocationPicker;
    private IDialogService _dialogService = null!;
    private IMapImageService _mapImageService = null!;
    private MapPaneViewModel? _viewModel;
    private Mapsui.Map? _map;
    private INotifyPropertyChanged? _subscribedTileLayer;
    private PhotoMetadata? _flyoutMetadata;
    private bool _mapRectangleSelecting;
    private MPoint? _mapRectangleStart;
    private MemoryLayer? _rectangleSelectionLayer;
    private bool _mapPanLockBeforeSelection;
    private bool _mapPanLockActive;
    private bool _isViewLoaded;

    public MapPaneViewControl()
    {
        InitializeComponent();
        _exifLocationPicker = new MapExifLocationPicker(
            canPick: () => _map is not null,
            hideMapStatus: HideMapStatusForExifPick,
            restoreMapStatus: UpdateMapStatusFromViewModel);
    }

    internal IExifLocationPicker ExifLocationPicker => _exifLocationPicker;

    internal void ConfigureMapImageExport(IDialogService dialogService, IMapImageService mapImageService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _mapImageService = mapImageService ?? throw new ArgumentNullException(nameof(mapImageService));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isViewLoaded = true;
        AttachViewModel(DataContext as MapPaneViewModel);
        // Loaded 前に受信した PropertyChanged を取りこぼした場合に備えて UI を再同期する
        ApplyMapFromViewModel();
        UpdateMapStatusFromViewModel();
        UpdateMapImageSaveState();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as MapPaneViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isViewLoaded = false;
        _exifLocationPicker.Cancel();
        UnsubscribeFromMapState(_map);
        _viewModel?.UpdateMapImageSaveState(hasValidViewport: false, isTileLoading: false);
        DetachViewModel();
        ClearRectangleSelectionLayer();
        _map = null;
    }

    private void AttachViewModel(MapPaneViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            if (_isViewLoaded)
            {
                ApplyMapFromViewModel();
                UpdateMapStatusFromViewModel();
            }
            return;
        }

        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is null)
        {
            UpdateMapStatusFromViewModel();
            return;
        }

        _viewModel.PropertyChanged += OnMapPaneViewModelPropertyChanged;
        ApplyMapFromViewModel();
        UpdateMapStatusFromViewModel();
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnMapPaneViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnMapPaneViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var propertyName = e.PropertyName;
        if (!HasUiThreadAccess())
        {
            _ = DispatcherQueue?.TryEnqueue(() => HandleViewModelPropertyChanged(propertyName));
            return;
        }

        HandleViewModelPropertyChanged(propertyName);
    }

    private void HandleViewModelPropertyChanged(string? propertyName)
    {
        if (!_isViewLoaded || _viewModel is null)
        {
            return;
        }

        if (propertyName == nameof(MapPaneViewModel.Map))
        {
            ApplyMapFromViewModel();
            if (!_exifLocationPicker.IsStatusRestorePending)
            {
                UpdateMapStatusFromViewModel();
            }
            return;
        }

        if (_exifLocationPicker.IsStatusRestorePending)
        {
            return;
        }

        if (propertyName is nameof(MapPaneViewModel.StatusTitle)
            or nameof(MapPaneViewModel.StatusDetail)
            or nameof(MapPaneViewModel.StatusIcon)
            or nameof(MapPaneViewModel.StatusVisibility))
        {
            UpdateMapStatusFromViewModel();
        }
    }

    private void ApplyMapFromViewModel()
    {
        SetMap(_viewModel?.Map);
    }

    private void SetMap(Mapsui.Map? map)
    {
        if (ReferenceEquals(_map, map))
        {
            return;
        }

        ClearRectangleSelectionLayer();
        UnsubscribeFromMapState(_map);
        _map = map;
        if (map is not null)
        {
            MapControl.Map = map;
            SubscribeToMapState(map);
        }

        UpdateMapImageSaveState();
    }

    private void SubscribeToMapState(Mapsui.Map map)
    {
        map.DataChanged += OnMapDataChanged;
        map.Navigator.ViewportChanged += OnMapViewportChanged;
        UpdateTileLayerSubscription(map);
    }

    private void UnsubscribeFromMapState(Mapsui.Map? map)
    {
        if (map is null)
        {
            return;
        }

        map.DataChanged -= OnMapDataChanged;
        map.Navigator.ViewportChanged -= OnMapViewportChanged;
        UpdateTileLayerSubscription(null);
    }

    private void OnMapDataChanged(object? sender, DataChangedEventArgs e)
        => QueueMapImageSaveStateUpdate();

    private void OnTileLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TileLayer.Busy))
        {
            QueueMapImageSaveStateUpdate();
        }
    }

    private void OnMapViewportChanged(object? sender, ViewportChangedEventArgs e)
        => QueueMapImageSaveStateUpdate();

    private void OnMapControlSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateMapImageSaveState();

    private void QueueMapImageSaveStateUpdate()
    {
        if (!HasUiThreadAccess())
        {
            _ = DispatcherQueue?.TryEnqueue(UpdateMapImageSaveState);
            return;
        }

        UpdateMapImageSaveState();
    }

    private void UpdateMapImageSaveState()
    {
        var map = _map;
        UpdateTileLayerSubscription(map);
        var hasValidViewport = map?.Navigator.Viewport.HasSize() == true;
        var isTileLoading = map?.Layers.OfType<TileLayer>().Any(layer => layer.Busy) == true;
        _viewModel?.UpdateMapImageSaveState(hasValidViewport, isTileLoading);
    }

    private void UpdateTileLayerSubscription(Mapsui.Map? map)
    {
        var tileLayer = map?.Layers.OfType<TileLayer>().FirstOrDefault();
        if (ReferenceEquals(_subscribedTileLayer, tileLayer))
        {
            return;
        }

        if (_subscribedTileLayer is not null)
        {
            _subscribedTileLayer.PropertyChanged -= OnTileLayerPropertyChanged;
        }

        _subscribedTileLayer = tileLayer;
        if (_subscribedTileLayer is not null)
        {
            _subscribedTileLayer.PropertyChanged += OnTileLayerPropertyChanged;
        }
    }

    private async void OnSaveMapImageClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        await _viewModel.SaveMapImageAsync(
            _dialogService.ShowMapImageSaveFilePickerAsync,
            CaptureMapImageAsync).ConfigureAwait(true);
    }

    private Task<Stream> CaptureMapImageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var map = MapControl.Map;
        var viewport = map.Navigator.Viewport;
        var pixelDensity = ((IMapControl)MapControl).GetPixelDensity()
            ?? throw new InvalidOperationException("Map pixel density is not available.");
        return Task.FromResult(_mapImageService.RenderPng(map, viewport, pixelDensity));
    }

    private void UpdateMapStatusFromViewModel()
    {
        if (MapStatusOverlay is null
            || MapStatusPanel is null
            || MapStatusTitle is null
            || MapStatusDescription is null
            || MapStatusIcon is null)
        {
            return;
        }

        if (_viewModel is null)
        {
            MapStatusOverlay.Visibility = Visibility.Collapsed;
            MapStatusPanel.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            MapStatusTitle.Text = _viewModel.StatusTitle;
            MapStatusDescription.Text = _viewModel.StatusDetail;
            MapStatusDescription.Visibility = string.IsNullOrWhiteSpace(_viewModel.StatusDetail)
                ? Visibility.Collapsed
                : Visibility.Visible;
            MapStatusIcon.Symbol = _viewModel.StatusIcon;

            var visibility = _viewModel.StatusVisibility;
            MapStatusOverlay.Visibility = visibility;
            MapStatusPanel.Visibility = visibility;
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to update map status view.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to update map status view.", ex);
        }
    }

    private bool HasUiThreadAccess()
    {
        return DispatcherQueue?.HasThreadAccess ?? false;
    }

    private bool HideMapStatusForExifPick()
    {
        if (MapStatusOverlay is null || MapStatusPanel is null)
        {
            return false;
        }

        if (MapStatusOverlay.Visibility == Visibility.Collapsed && MapStatusPanel.Visibility == Visibility.Collapsed)
        {
            return false;
        }

        MapStatusOverlay.Visibility = Visibility.Collapsed;
        MapStatusPanel.Visibility = Visibility.Collapsed;
        return true;
    }

    private MemoryLayer? GetMarkerLayer()
    {
        if (MapControl.Map?.Layers is not { } layers)
        {
            return null;
        }

        return layers.OfType<MemoryLayer>()
            .FirstOrDefault(layer => string.Equals(layer.Name, "PhotoMarkers", StringComparison.Ordinal));
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

        var markerLayer = GetMarkerLayer();
        if (markerLayer is null || !markerLayer.Features.Contains(feature))
        {
            return;
        }

        if (feature[PhotoItemKey] is not PhotoItem photoItem || feature[PhotoMetadataKey] is not PhotoMetadata metadata)
        {
            AppLog.Info("Marker clicked but missing PhotoItem or PhotoMetadata.");
            return;
        }

        _viewModel?.RequestPhotoFocus(photoItem.FilePath);
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
                _viewModel?.RequestNotification(
                    LocalizationService.GetString("Message.LaunchBrowserFailed"),
                    InfoBarSeverity.Error);
            }
        }

        MarkerFlyout.Hide();
    }

    private static string GenerateGoogleMapsUrl(double latitude, double longitude)
    {
        return $"https://www.google.com/maps?q={latitude.ToString(CultureInfo.InvariantCulture)},{longitude.ToString(CultureInfo.InvariantCulture)}";
    }

    private void OnMapPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (MapControl is null || _map is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(MapControl);
        if (_exifLocationPicker.IsPicking)
        {
            if (_exifLocationPicker.HandlePointerPressed(
                point.Properties.IsLeftButtonPressed,
                point.Properties.IsRightButtonPressed,
                point.Position))
            {
                e.Handled = true;
            }
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
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

        if (_exifLocationPicker.IsPicking)
        {
            if (_exifLocationPicker.HandlePointerReleased(
                e.GetCurrentPoint(MapControl).Position,
                () => GetWorldPosition(e)))
            {
                e.Handled = true;
            }
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
        _exifLocationPicker.HandlePointerCaptureLost();
        ClearRectangleSelectionLayer();
        RestoreMapPanLock();
    }

    private void LockMapPan()
    {
        if (MapControl.Map?.Navigator is not { } navigator)
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

        if (MapControl.Map?.Navigator is not { } navigator)
        {
            return;
        }

        navigator.PanLock = _mapPanLockBeforeSelection;
        _mapPanLockActive = false;
    }

    private MPoint? GetWorldPosition(PointerRoutedEventArgs e)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
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
        if (_map is null)
        {
            return;
        }

        if (_rectangleSelectionLayer is null)
        {
            return;
        }

        _map.Layers.Remove(_rectangleSelectionLayer);
        _rectangleSelectionLayer.Dispose();
        _rectangleSelectionLayer = null;
        _map.Refresh();
    }

    public void Dispose()
    {
        ClearRectangleSelectionLayer();
        UnsubscribeFromMapState(_map);
        DetachViewModel();
        _map = null;
        GC.SuppressFinalize(this);
    }

    private void SelectPhotosInRectangle(MRect selectionBounds)
    {
        var markerLayer = GetMarkerLayer();
        if (markerLayer is null)
        {
            _viewModel?.RequestPhotoSelection(Array.Empty<string>());
            return;
        }

        var selectedPhotos = MapPaneSelectionHelper.SelectPhotosInRectangle(markerLayer.Features, selectionBounds, PhotoItemKey);
        var selectedFilePaths = selectedPhotos
            .Select(photo => photo.FilePath)
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _viewModel?.RequestPhotoSelection(selectedFilePaths);
    }
}
