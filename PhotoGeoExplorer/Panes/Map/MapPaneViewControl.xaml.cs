using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
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

    private MapPaneViewModel? _viewModel;
    private Mapsui.Map? _map;
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

    public MapPaneViewControl()
    {
        InitializeComponent();
    }

    public event EventHandler<MapPanePhotoFocusRequestedEventArgs>? PhotoFocusRequested;

    public event EventHandler<MapPaneRectangleSelectionEventArgs>? RectangleSelectionCompleted;

    public event EventHandler<MapPaneNotificationEventArgs>? NotificationRequested;

    public bool CanPickExifLocation => _map is not null;

    public Task<(double Latitude, double Longitude)?> PickExifLocationAsync()
    {
        if (!CanPickExifLocation)
        {
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
        return _exifLocationPicker.Task;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as MapPaneViewModel);
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as MapPaneViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelExifLocationPick();
        DetachViewModel();
        ClearRectangleSelectionLayer();
        _map = null;
    }

    private void AttachViewModel(MapPaneViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
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
        if (e.PropertyName == nameof(MapPaneViewModel.Map))
        {
            ApplyMapFromViewModel();
            return;
        }

        if (_restoreMapStatusAfterExifPick)
        {
            return;
        }

        if (e.PropertyName is nameof(MapPaneViewModel.StatusTitle)
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
        _map = map;
        if (map is not null)
        {
            MapControl.Map = map;
        }
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
        UpdateMapStatusFromViewModel();
    }

    private void CompleteExifLocationPick(double latitude, double longitude)
    {
        if (!_isPickingExifLocation)
        {
            return;
        }

        _isPickingExifLocation = false;
        _isExifPickPointerActive = false;
        _exifPickPointerStart = null;
        RestoreMapStatusAfterExifPick();
        var picker = _exifLocationPicker;
        _exifLocationPicker = null;
        picker?.TrySetResult((latitude, longitude));
    }

    private void CancelExifLocationPick()
    {
        if (!_isPickingExifLocation)
        {
            return;
        }

        _isPickingExifLocation = false;
        _isExifPickPointerActive = false;
        _exifPickPointerStart = null;
        RestoreMapStatusAfterExifPick();
        var picker = _exifLocationPicker;
        _exifLocationPicker = null;
        picker?.TrySetResult(null);
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

        PhotoFocusRequested?.Invoke(this, new MapPanePhotoFocusRequestedEventArgs(photoItem));
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
                NotificationRequested?.Invoke(this, new MapPaneNotificationEventArgs(
                    LocalizationService.GetString("Message.LaunchBrowserFailed"),
                    InfoBarSeverity.Error));
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
        _isExifPickPointerActive = false;
        _exifPickPointerStart = null;
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
        GC.SuppressFinalize(this);
    }

    private void SelectPhotosInRectangle(MRect selectionBounds)
    {
        var markerLayer = GetMarkerLayer();
        if (markerLayer is null)
        {
            RectangleSelectionCompleted?.Invoke(this, new MapPaneRectangleSelectionEventArgs(Array.Empty<PhotoItem>()));
            return;
        }

        var selectedPhotos = new List<PhotoItem>();
        var seenFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in markerLayer.Features)
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

            if (point.X < selectionBounds.Min.X || point.X > selectionBounds.Max.X
                || point.Y < selectionBounds.Min.Y || point.Y > selectionBounds.Max.Y)
            {
                continue;
            }

            if (feature[PhotoItemKey] is not PhotoItem photoItem)
            {
                continue;
            }

            if (seenFilePaths.Add(photoItem.FilePath))
            {
                selectedPhotos.Add(photoItem);
            }
        }

        RectangleSelectionCompleted?.Invoke(this, new MapPaneRectangleSelectionEventArgs(selectedPhotos));
    }
}

internal sealed class MapPanePhotoFocusRequestedEventArgs : EventArgs
{
    public MapPanePhotoFocusRequestedEventArgs(PhotoItem photoItem)
    {
        PhotoItem = photoItem ?? throw new ArgumentNullException(nameof(photoItem));
    }

    public PhotoItem PhotoItem { get; }
}

internal sealed class MapPaneRectangleSelectionEventArgs : EventArgs
{
    public MapPaneRectangleSelectionEventArgs(IReadOnlyList<PhotoItem> photoItems)
    {
        PhotoItems = photoItems ?? throw new ArgumentNullException(nameof(photoItems));
    }

    public IReadOnlyList<PhotoItem> PhotoItems { get; }
}

internal sealed class MapPaneNotificationEventArgs : EventArgs
{
    public MapPaneNotificationEventArgs(string message, InfoBarSeverity severity)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Severity = severity;
    }

    public string Message { get; }

    public InfoBarSeverity Severity { get; }
}
