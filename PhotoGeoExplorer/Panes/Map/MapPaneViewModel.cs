using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// Map Pane の ViewModel
/// 地図の状態管理、マーカー表示、ズーム制御を実装
/// </summary>
internal sealed class MapPaneViewModel : PaneViewModelBase
{
    private const string PhotoMetadataKey = "PhotoMetadata";
    private const string PhotoItemKey = "PhotoItem";
    private const int DefaultMapZoomLevel = 14;
    private static readonly int[] MapZoomLevelOptions = { 8, 10, 12, 14, 16, 18 };

    private readonly IMapPaneService _service;
    private Mapsui.Map? _map;
    private TileLayer? _baseTileLayer;
    private MemoryLayer? _markerLayer;
    private MapTileSourceType _currentTileSource = MapTileSourceType.OpenStreetMap;
    private int _mapDefaultZoomLevel = DefaultMapZoomLevel;
    private CancellationTokenSource? _mapUpdateCts;
    private bool _isMapInitialized;
    private string _statusTitle = string.Empty;
    private string _statusDetail = string.Empty;
    private Symbol _statusIcon = Symbol.Map;
    private Visibility _statusVisibility = Visibility.Visible;

    public MapPaneViewModel()
        : this(new MapPaneService())
    {
    }

    internal MapPaneViewModel(IMapPaneService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Title = "Map";
    }

    /// <summary>
    /// 地図オブジェクト（UI スレッドでのみアクセス可能）
    /// </summary>
    public Mapsui.Map? Map
    {
        get => _map;
        private set => SetProperty(ref _map, value);
    }

    /// <summary>
    /// 地図の初期化状態
    /// </summary>
    public bool IsMapInitialized
    {
        get => _isMapInitialized;
        private set => SetProperty(ref _isMapInitialized, value);
    }

    /// <summary>
    /// ステータスメッセージのタイトル
    /// </summary>
    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    /// <summary>
    /// ステータスメッセージの詳細
    /// </summary>
    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    /// <summary>
    /// ステータスアイコン
    /// </summary>
    public Symbol StatusIcon
    {
        get => _statusIcon;
        private set => SetProperty(ref _statusIcon, value);
    }

    /// <summary>
    /// ステータスの表示状態
    /// </summary>
    public Visibility StatusVisibility
    {
        get => _statusVisibility;
        private set => SetProperty(ref _statusVisibility, value);
    }

    /// <summary>
    /// 現在のタイルソース
    /// </summary>
    public MapTileSourceType CurrentTileSource
    {
        get => _currentTileSource;
        private set => SetProperty(ref _currentTileSource, value);
    }

    /// <summary>
    /// 地図のデフォルトズームレベル
    /// </summary>
    public int MapDefaultZoomLevel
    {
        get => _mapDefaultZoomLevel;
        set
        {
            var normalized = NormalizeMapZoomLevel(value);
            SetProperty(ref _mapDefaultZoomLevel, normalized);
        }
    }

    protected override async Task OnInitializeAsync()
    {
        try
        {
            // UI スレッドで地図を初期化
            DispatcherQueue? dispatcherQueue = null;
            const int ClassNotRegisteredHresult = unchecked((int)0x80040154);
            try
            {
                dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }
            catch (COMException ex) when (ex.HResult == ClassNotRegisteredHresult)
            {
                // テスト環境の場合は初期化をスキップ
                AppLog.Info("DispatcherQueue is not available. Skipping map initialization.");
                IsMapInitialized = true;
                return;
            }

            if (dispatcherQueue is null)
            {
                // テスト環境の場合は初期化をスキップ
                IsMapInitialized = true;
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var enqueued = dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    InitializeMapCore();
                    IsMapInitialized = true;
                    tcs.TrySetResult(true);
                }
#pragma warning disable CA1031 // This callback must complete the TaskCompletionSource on any failure.
                catch (Exception ex)
                {
                    AppLog.Error("Failed to initialize map in MapPaneViewModel.", ex);
                    ShowStatus(
                        LocalizationService.GetString("MapStatus.InitFailedTitle"),
                        LocalizationService.GetString("MapStatus.SeeLogDetail"),
                        Symbol.Map);
                    tcs.TrySetException(ex);
                }
#pragma warning restore CA1031 // This callback must complete the TaskCompletionSource on any failure.
            });

            if (!enqueued)
            {
                AppLog.Error("Failed to enqueue map initialization.");
                tcs.TrySetException(new InvalidOperationException("Failed to enqueue map initialization."));
            }

            await tcs.Task.ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to initialize map pane.", ex);
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error("Failed to initialize map pane.", ex);
        }
    }

    protected override void OnCleanup()
    {
        _mapUpdateCts?.Cancel();
        _mapUpdateCts?.Dispose();
        _mapUpdateCts = null;

        _markerLayer?.Dispose();
        _markerLayer = null;

        _baseTileLayer?.Dispose();
        _baseTileLayer = null;

        _map?.Dispose();
        Map = null;
    }

    /// <summary>
    /// 選択された写真のマーカーを地図上に更新する
    /// </summary>
    public async Task UpdateMarkersFromSelectionAsync(IReadOnlyList<PhotoListItem> selectedItems)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);

        if (_map is null || _markerLayer is null || !IsMapInitialized)
        {
            return;
        }

        // 既存の更新をキャンセル
        var previousCts = _mapUpdateCts;
        _mapUpdateCts = null;
        if (previousCts is not null)
        {
            await previousCts.CancelAsync().ConfigureAwait(false);
            previousCts.Dispose();
        }

        var imageItems = selectedItems.Where(item => !item.IsFolder).ToList();
        if (imageItems.Count == 0)
        {
            ClearMapMarkers();
            ShowStatus(
                LocalizationService.GetString("MapStatus.SelectPhotoTitle"),
                LocalizationService.GetString("MapStatus.SelectPhotoDetail"),
                Symbol.Map);
            return;
        }

        // メタデータ読み込みが必要な場合（1件以上選択時）
        var cts = new CancellationTokenSource();
        _mapUpdateCts = cts;

        IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)> metadataItems;
        try
        {
            metadataItems = await _service.LoadPhotoMetadataAsync(imageItems, cts.Token).ConfigureAwait(true);
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
            ShowStatus(
                LocalizationService.GetString("MapStatus.LocationMissingTitle"),
                LocalizationService.GetString("MapStatus.LocationMissingSelectionDetail"),
                Symbol.Map);
            return;
        }

        if (points.Count == 1)
        {
            var single = points[0];
            SetMapMarker(single.Latitude, single.Longitude, single.Metadata, single.Item);
            HideStatus();
            return;
        }

        SetMapMarkers(points);
        HideStatus();
    }

    /// <summary>
    /// タイルソースを切り替える
    /// </summary>
    public void SwitchTileSource(MapTileSourceType newSource)
    {
        if (_map is null || !IsMapInitialized)
        {
            return;
        }

        try
        {
            var newTileLayer = _service.CreateTileLayer(newSource, UserAgentProvider.UserAgent);

            if (_baseTileLayer is not null)
            {
                _map.Layers.Remove(_baseTileLayer);
                _baseTileLayer.Dispose();
            }

            _map.Layers.Insert(0, newTileLayer);
            _baseTileLayer = newTileLayer;
            CurrentTileSource = newSource;

            _map.Refresh();
            AppLog.Info($"Switched map tile source to {newSource}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            AppLog.Error("Map tile switch failed.", ex);
        }
    }

    private void InitializeMapCore()
    {
        if (_map is not null)
        {
            return;
        }

        var (map, tileLayer, markerLayer) = _service.InitializeMap(_currentTileSource, UserAgentProvider.UserAgent);

        _map = map;
        _baseTileLayer = tileLayer;
        _markerLayer = markerLayer;

        Map = map;
        HideStatus();
        AppLog.Info("Map initialized in MapPaneViewModel.");
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

    private static int NormalizeMapZoomLevel(int level)
    {
        if (MapZoomLevelOptions.Contains(level))
        {
            return level;
        }

        return DefaultMapZoomLevel;
    }

    private void ShowStatus(string title, string detail, Symbol icon)
    {
        StatusTitle = title;
        StatusDetail = detail;
        StatusIcon = icon;
        StatusVisibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        StatusVisibility = Visibility.Collapsed;
    }
}
