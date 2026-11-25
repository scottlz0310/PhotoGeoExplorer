using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace PhotoGeoExplorer.Panes.Map;

internal sealed partial class MapPaneViewControl : UserControl, IDisposable
{
    private readonly MapExifLocationPicker _exifLocationPicker;
    private IDialogService _dialogService = null!;
    private MapPaneViewModel? _viewModel;
    private XamlRoot? _observedXamlRoot;
    private PhotoMetadata? _flyoutMetadata;
    private bool _mapRectangleSelecting;
    private GeoPoint? _mapRectangleStart;
    private readonly MapPaneViewLifecycle _lifecycle = new(AppLog.Info);

    public MapPaneViewControl()
    {
        InitializeComponent();
        ConfigureMapHeaderCollapse();
        ConnectMapView();
        _exifLocationPicker = new MapExifLocationPicker(
            canPick: () => MapView.Surface is not null,
            hideMapStatus: HideMapStatusForExifPick,
            restoreMapStatus: UpdateMapStatusFromViewModel);
    }

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

    // 幅が足りないときは、ズームレベルを非表示 → 緯度経度を非表示 → 道路名を末尾省略 → 字地名を末尾省略の順に省略する。
    // XAML では指定できないため、ここで設定する（理由は PriorityCollapsePanel を参照）
    private void ConfigureMapHeaderCollapse()
    {
        MapHeaderLocationPanel.SetOverflow(MapHeaderAreaName, PriorityCollapseMode.Trim, priority: 1);
        MapHeaderLocationPanel.SetSeparator(MapHeaderAreaSeparator);
        MapHeaderLocationPanel.SetOverflow(MapHeaderRoad, PriorityCollapseMode.Trim, priority: 2);
        MapHeaderLocationPanel.SetSeparator(MapHeaderRoadSeparator);
        MapHeaderLocationPanel.SetOverflow(MapHeaderCoordinatesButton, PriorityCollapseMode.Hide, priority: 3);
        MapHeaderLocationPanel.SetSeparator(MapHeaderCoordinatesSeparator);
        MapHeaderLocationPanel.SetOverflow(MapHeaderZoomText, PriorityCollapseMode.Hide, priority: 4);
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
        => $"hasViewModel={_viewModel is not null}, hasSurface={MapView.Surface is not null}";

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
        _viewModel?.UpdateMapImageSaveState(hasValidViewport: false, isTileLoading: false);
        AbortRectangleSelection();
        DetachViewModel();
        MapView.Surface = null;
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

        AbortRectangleSelection();
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

        if (propertyName == nameof(MapPaneViewModel.Surface))
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
        var surface = _viewModel?.Surface;
        if (ReferenceEquals(MapView.Surface, surface))
        {
            return;
        }

        MapView.Surface?.ClearSelectionRectangle();
        MapView.Surface = surface;
        UpdateMapImageSaveState();
    }

    // MapView のイベントを購読する。ポインタ入力は、地図の要素そのもので受ける（矩形選択・EXIF 位置ピック）
    private void ConnectMapView()
    {
        MapView.MarkerClicked += OnMarkerClicked;
        MapView.MapStateChanged += OnMapStateChanged;
        MapView.InputElement.PointerCaptureLost += OnMapPointerCaptureLost;
        MapView.InputElement.PointerMoved += OnMapPointerMoved;
        MapView.InputElement.PointerPressed += OnMapPointerPressed;
        MapView.InputElement.PointerReleased += OnMapPointerReleased;
    }

    // データ・視点・レイヤー構成のどれが変わっても保存可否を再評価する。
    // タイルソース切替はベースレイヤーを差し替えるだけで地図のインスタンスを変えないため、
    // レイヤー構成の変化を捉えないと Busy 購読が破棄済みの旧レイヤーに残る。
    private void OnMapStateChanged(object? sender, EventArgs e)
        => QueueMapImageSaveStateUpdate();

    private void OnBackgroundBusyChanged(object? sender, EventArgs e)
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

        var hasValidViewport = MapView.Surface?.View.HasSize == true;
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
        var surface = _viewModel?.Surface ?? throw new InvalidOperationException("The map surface is not initialized.");
        var pixelDensity = GetPixelDensity()
            ?? throw new InvalidOperationException("Map pixel density is not available.");
        return surface.RenderPngAsync((float)pixelDensity, cancellationToken);
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
        MapHeaderMenuShowZoom.IsChecked = settings.ShowZoom;
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
        settings.ShowZoom = MapHeaderMenuShowZoom.IsChecked;
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

    private void OnMarkerClicked(object? sender, MapMarker marker)
    {
        _viewModel?.RequestPhotoFocus(marker.Item.FilePath);
        ShowMarkerFlyout(marker.Item, marker.Metadata);
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
        MarkerFlyout.ShowAt(MapView.InputElement);
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
        if (_viewModel?.Surface is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(MapView.InputElement);
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

        var geoStart = GetGeoPosition(e);
        if (geoStart is null)
        {
            return;
        }

        _viewModel?.Surface?.LockPan();
        _mapRectangleSelecting = true;
        _mapRectangleStart = geoStart;
        MapView.InputElement.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMapPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var rectangleStart = _mapRectangleStart;
        if (!_mapRectangleSelecting || _viewModel?.Surface is null || rectangleStart is null)
        {
            return;
        }

        var geoEnd = GetGeoPosition(e);
        if (geoEnd is null)
        {
            return;
        }

        _viewModel?.Surface?.ShowSelectionRectangle(rectangleStart.Value, geoEnd.Value);
        e.Handled = true;
    }

    private void OnMapPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel?.Surface is null)
        {
            return;
        }

        if (_exifLocationPicker.IsPicking)
        {
            if (_exifLocationPicker.HandlePointerReleased(
                e.GetCurrentPoint(MapView.InputElement).Position,
                () => GetGeoPosition(e)))
            {
                e.Handled = true;
            }
            return;
        }

        var rectangleStart = _mapRectangleStart;
        _mapRectangleSelecting = false;
        _mapRectangleStart = null;
        MapView.InputElement.ReleasePointerCapture(e.Pointer);
        var surface = _viewModel?.Surface;
        surface?.RestorePan();

        if (rectangleStart is null)
        {
            surface?.ClearSelectionRectangle();
            return;
        }

        var geoEnd = GetGeoPosition(e);
        if (geoEnd is null)
        {
            surface?.ClearSelectionRectangle();
            return;
        }

        SelectPhotosInRectangle(rectangleStart.Value, geoEnd.Value);
        surface?.ClearSelectionRectangle();
        e.Handled = true;
    }

    private void OnMapPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _exifLocationPicker.HandlePointerCaptureLost();
        AbortRectangleSelection();
    }

    // 選択の途中で枠・パン固定を残さない（ペインが外れる場合やキャプチャ喪失）。ViewModel を外す前に呼ぶ
    private void AbortRectangleSelection()
    {
        _mapRectangleSelecting = false;
        _mapRectangleStart = null;
        var surface = _viewModel?.Surface;
        surface?.ClearSelectionRectangle();
        surface?.RestorePan();
    }

    private GeoPoint? GetGeoPosition(PointerRoutedEventArgs e)
    {
        if (_viewModel?.Surface is not { } surface)
        {
            return null;
        }

        var screenPosition = e.GetCurrentPoint(MapView.InputElement).Position;
        return surface.TryGetGeoPosition(screenPosition.X, screenPosition.Y, out var position) ? position : null;
    }

    public void Dispose()
    {
        AbortRectangleSelection();
        DetachViewModel();
        MapView.Surface = null;
        GC.SuppressFinalize(this);
    }

    private void SelectPhotosInRectangle(GeoPoint corner1, GeoPoint corner2)
    {
        if (_viewModel?.Surface is not { } surface)
        {
            _viewModel?.RequestPhotoSelection(Array.Empty<string>());
            return;
        }

        var selectedPhotos = MapPaneSelectionHelper.SelectPhotosInRectangle(surface.Markers, corner1, corner2);
        var selectedFilePaths = selectedPhotos
            .Select(photo => photo.FilePath)
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _viewModel?.RequestPhotoSelection(selectedFilePaths);
    }
}
