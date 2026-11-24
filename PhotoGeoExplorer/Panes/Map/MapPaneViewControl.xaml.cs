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
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using Mapsui.UI;
using Mapsui.UI.WinUI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using Windows.ApplicationModel.DataTransfer;
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
    private MapPaneViewModel? _viewModel;
    private XamlRoot? _observedXamlRoot;
    private Mapsui.Map? _map;
    private PhotoMetadata? _flyoutMetadata;
    private bool _mapRectangleSelecting;
    private MPoint? _mapRectangleStart;
    private MemoryLayer? _rectangleSelectionLayer;
    private bool _mapPanLockBeforeSelection;
    private bool _mapPanLockActive;
    private readonly MapPaneViewLifecycle _lifecycle = new(AppLog.Info);

    public MapPaneViewControl()
    {
        InitializeComponent();
        MapControl.SetMapRenderer(new ObservingMapRenderer(new MapRenderer(), OnMapRenderStarting));
        ConfigureMapHeaderCollapse();
        _exifLocationPicker = new MapExifLocationPicker(
            canPick: () => _map is not null,
            hideMapStatus: HideMapStatusForExifPick,
            restoreMapStatus: UpdateMapStatusFromViewModel);
    }

    private Action? OnMapRenderStarting(Viewport viewport, System.Collections.Generic.IReadOnlyList<ILayer> layers, RenderService renderService)
        => _viewModel?.BeginMapRender(viewport, layers, renderService);

    internal IExifLocationPicker ExifLocationPicker => _exifLocationPicker;

    /// <summary>
    /// 地域データの取得の確認ダイアログを出す（View の責務。取得するか・以降は確認しないかだけを返す）。
    /// 表示中のダイアログがある間は、そのダイアログが閉じるまで待たず、取得しない扱いにする
    /// </summary>
    internal async Task<AreaDataConsent> AskAreaDataConsentAsync(System.Collections.Generic.IReadOnlyList<string> prefectureNames, CancellationToken cancellationToken)
    {
        var remember = new CheckBox { Content = LocalizationService.GetString("AreaData.Download.Prompt.Remember") };
        var content = new StackPanel { Spacing = 12, MaxWidth = 420 };
        content.Children.Add(new TextBlock
        {
            Text = LocalizationService.Format("AreaData.Download.Prompt.Body", string.Join(", ", prefectureNames)),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = LocalizationService.GetString("AreaData.Download.Prompt.Notice"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.8
        });
        content.Children.Add(remember);
        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("AreaData.Download.Prompt.Title"),
            Content = content,
            PrimaryButtonText = LocalizationService.GetString("AreaData.Download.Prompt.Accept"),
            SecondaryButtonText = LocalizationService.GetString("AreaData.Download.Prompt.Never"),
            CloseButtonText = LocalizationService.GetString("AreaData.Download.Prompt.Decline"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        try
        {
            var result = await dialog.ShowAsync().AsTask(cancellationToken).ConfigureAwait(true);
            return result switch
            {
                ContentDialogResult.Primary => remember.IsChecked == true ? AreaDataConsent.AcceptAndRemember : AreaDataConsent.Accept,
                ContentDialogResult.Secondary => AreaDataConsent.DeclineAndRemember,
                _ => AreaDataConsent.Decline
            };
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // 別のダイアログが開いているときは開けない。取得しない扱いにして、選択の処理を止めない
            AppLog.Error("Failed to show the area data download dialog.", ex);
            return AreaDataConsent.Decline;
        }
    }

    // 幅が足りないときは、緯度経度を非表示 → 道路名を末尾省略 → 字地名を末尾省略の順に省略する。
    // XAML では指定できないため、ここで設定する（理由は PriorityCollapsePanel を参照）
    private void ConfigureMapHeaderCollapse()
    {
        MapHeaderLocationPanel.SetOverflow(MapHeaderAreaName, PriorityCollapseMode.Trim, priority: 1);
        MapHeaderLocationPanel.SetSeparator(MapHeaderAreaSeparator);
        MapHeaderLocationPanel.SetOverflow(MapHeaderRoad, PriorityCollapseMode.Trim, priority: 2);
        MapHeaderLocationPanel.SetSeparator(MapHeaderRoadSeparator);
        MapHeaderLocationPanel.SetOverflow(MapHeaderCoordinatesButton, PriorityCollapseMode.Hide, priority: 3);
    }

    internal void ConfigureMapImageExport(IDialogService dialogService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lifecycle.NotifyLoaded(DescribeState());
        AttachViewModel(DataContext as MapPaneViewModel);
        ObserveXamlRoot();
        // Loaded 前に受信した PropertyChanged を取りこぼした場合に備えて UI を再同期する
        ApplyMapFromViewModel();
        UpdateMapStatusFromViewModel();
        UpdateMapImageSaveState();
    }

    private string DescribeState()
        => $"hasViewModel={_viewModel is not null}, hasMap={_map is not null}";

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as MapPaneViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 同じインスタンスの再追加では Loaded が先に届くことがある。古い Unloaded は無視する (#278)
        if (!_lifecycle.TryNotifyUnloaded(IsLoaded, DescribeState()))
        {
            return;
        }

        _exifLocationPicker.Cancel();
        StopObservingXamlRoot();
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
            if (_lifecycle.IsViewLoaded)
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
        _viewModel.BackgroundBusyChanged += OnBackgroundBusyChanged;
        ReportPixelDensity();
        ApplyMapFromViewModel();
        UpdateMapStatusFromViewModel();
    }

    // 画素密度は XamlRoot からしか分からない。表示倍率の変化（別のモニターへの移動など）を ViewModel へ伝える
    private void ObserveXamlRoot()
    {
        if (!ReferenceEquals(_observedXamlRoot, XamlRoot))
        {
            StopObservingXamlRoot();
            _observedXamlRoot = XamlRoot;
            if (_observedXamlRoot is not null)
            {
                _observedXamlRoot.Changed += OnXamlRootChanged;
            }
        }

        ReportPixelDensity();
    }

    private void StopObservingXamlRoot()
    {
        if (_observedXamlRoot is not null)
        {
            _observedXamlRoot.Changed -= OnXamlRootChanged;
            _observedXamlRoot = null;
        }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ReportPixelDensity();

    private void ReportPixelDensity()
    {
        if (_viewModel is not null && GetPixelDensity() is { } pixelDensity)
        {
            _viewModel.UpdatePixelDensity(pixelDensity);
        }
    }

    // Mapsui の MapControl.GetPixelDensity() は「描画面の画素幅 ÷ 表示幅」で、表示倍率の変更の直後は 2 つの値の更新が
    // 追いつかず、古い値や中途半端な比になる（XamlRoot.Changed で読むと、その後も訂正されない）。
    // 表示倍率の変更の時点で更新済みの XamlRoot.RasterizationScale を、画素密度の唯一の取得元にする
    private double? GetPixelDensity() => XamlRoot?.RasterizationScale;

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnMapPaneViewModelPropertyChanged;
            _viewModel.BackgroundBusyChanged -= OnBackgroundBusyChanged;
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
        if (!_lifecycle.IsViewLoaded || _viewModel is null)
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
        map.Layers.Changed += OnMapLayersChanged;
    }

    private void UnsubscribeFromMapState(Mapsui.Map? map)
    {
        if (map is null)
        {
            return;
        }

        map.DataChanged -= OnMapDataChanged;
        map.Navigator.ViewportChanged -= OnMapViewportChanged;
        map.Layers.Changed -= OnMapLayersChanged;
    }

    private void OnMapDataChanged(object? sender, DataChangedEventArgs e)
        => QueueMapImageSaveStateUpdate();

    // タイルソース切替はベースレイヤーを差し替えるだけで Map インスタンスを変えないため、
    // レイヤー構成の変化を捉えないと Busy 購読が破棄済みの旧レイヤーに残る。
    private void OnMapLayersChanged(object sender, LayerCollectionChangedEventArgs args)
        => QueueMapImageSaveStateUpdate();

    private void OnBackgroundBusyChanged(object? sender, EventArgs e)
        => QueueMapImageSaveStateUpdate();

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
        var hasValidViewport = map?.Navigator.Viewport.HasSize() == true;
        var isTileLoading = _viewModel?.IsBackgroundBusy == true;
        _viewModel?.UpdateMapImageSaveState(hasValidViewport, isTileLoading);
    }

    private async void OnSaveMapImageClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            await _viewModel.SaveMapImageAsync(
                _dialogService.ShowMapImageSaveFilePickerAsync,
                CaptureMapImageAsync).ConfigureAwait(true);
        }
        // ViewModel は失敗通知を出したうえで再スローする契約のため、async void の
        // ここで終端しないと Application.UnhandledException へ抜けてプロセスが落ちる。
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppLog.Error("Failed to save the current map image.", ex);
        }
    }

    private Task<Stream> CaptureMapImageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var viewModel = _viewModel ?? throw new InvalidOperationException("The map pane view model is not attached.");
        var viewport = MapControl.Map.Navigator.Viewport;
        var pixelDensity = GetPixelDensity()
            ?? throw new InvalidOperationException("Map pixel density is not available.");
        return viewModel.CaptureMapImageAsync(viewport, (float)pixelDensity, cancellationToken);
    }

    private void OnMapHeaderRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        MapHeaderMenu.ShowAt(MapHeaderBar, new FlyoutShowOptions { Position = e.GetPosition(MapHeaderBar) });
        e.Handled = true;
    }

    private void OnMapHeaderMenuOpening(object? sender, object e)
    {
        if (_viewModel?.LocationInfo.DisplaySettings is not { } settings)
        {
            return;
        }

        MapHeaderMenuShowAddress.IsChecked = settings.ShowAddress;
        MapHeaderMenuShowRoadName.IsChecked = settings.ShowRoadName;
        MapHeaderMenuShowCoordinates.IsChecked = settings.ShowCoordinates;
        MapHeaderMenuIncludePrefecture.IsChecked = settings.IncludePrefecture;
    }

    private void OnMapHeaderMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.LocationInfo.DisplaySettings is not { } settings)
        {
            return;
        }

        settings.ShowAddress = MapHeaderMenuShowAddress.IsChecked;
        settings.ShowRoadName = MapHeaderMenuShowRoadName.IsChecked;
        settings.ShowCoordinates = MapHeaderMenuShowCoordinates.IsChecked;
        settings.IncludePrefecture = MapHeaderMenuIncludePrefecture.IsChecked;
    }

    private void OnRoadNameClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } viewModel || viewModel.LocationInfo.RoadCandidates.Count == 0)
        {
            return;
        }

        RoadCandidatesList.ItemsSource = viewModel.LocationInfo.RoadCandidates;
        RoadCandidatesFlyout.ShowAt((FrameworkElement)sender);
    }

    private void OnConfirmRoadClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } viewModel || sender is not FrameworkElement { DataContext: RoadCandidateItem item })
        {
            return;
        }

        RoadCandidatesFlyout.Hide();
        _ = viewModel.LocationInfo.ConfirmRoadAsync(item);
    }

    private void OnUnconfirmRoadClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        RoadCandidatesFlyout.Hide();
        _ = viewModel.LocationInfo.UnconfirmRoadAsync();
    }

    private void OnCopyCoordinatesClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } viewModel || viewModel.LocationInfo.CoordinatesText is not { Length: > 0 } coordinates)
        {
            return;
        }

        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(coordinates);
            Clipboard.SetContent(dataPackage);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // 他のアプリがクリップボードを開いている間は失敗する
            AppLog.Error("Failed to copy the coordinates to the clipboard.", ex);
            viewModel.RequestNotification(
                LocalizationService.GetString("Message.CoordinatesCopyFailed"),
                InfoBarSeverity.Error);
            return;
        }

        viewModel.RequestNotification(
            $"{LocalizationService.GetString("Message.CoordinatesCopied")} {coordinates}",
            InfoBarSeverity.Success);
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
