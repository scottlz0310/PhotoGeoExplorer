using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// Map Pane の ViewModel
/// 地図の状態管理、マーカー表示、ズーム制御を実装
/// </summary>
internal sealed class MapPaneViewModel : PaneViewModelBase
{
    private readonly IMapPaneService _service;
    private readonly WorkspaceState _workspaceState;
    private readonly MapMarkerPresenter _markerPresenter;
    private Mapsui.Map? _map;
    private TileLayer? _baseTileLayer;
    private MemoryLayer? _markerLayer;
    private MapTileSourceType _currentTileSource = MapTileSourceType.OpenStreetMap;
    private int _mapDefaultZoomLevel = MapZoomLevelCatalog.Default;
    private CancellationTokenSource? _mapUpdateCts;
    private bool _isMapInitialized;
    private string _statusTitle = string.Empty;
    private string _statusDetail = string.Empty;
    private Symbol _statusIcon = Symbol.Map;
    private Visibility _statusVisibility = Visibility.Collapsed;

    public MapPaneViewModel()
        : this(new MapPaneService(), new WorkspaceState())
    {
    }

    internal MapPaneViewModel(IMapPaneService service)
        : this(service, new WorkspaceState())
    {
    }

    internal MapPaneViewModel(IMapPaneService service, WorkspaceState workspaceState)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _workspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));
        _markerPresenter = new MapMarkerPresenter(_service);
        Title = "Map";
        _workspaceState.PropertyChanged += OnWorkspaceStatePropertyChanged;
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
            var normalized = SettingsNormalization.NormalizeMapZoomLevel(value);
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
        _workspaceState.PropertyChanged -= OnWorkspaceStatePropertyChanged;
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
            // await 中に Cleanup() が走り破棄されている可能性があるため、ここで現在のフィールドを再取得する
            if (_map is { } mapAfterCancel && _markerLayer is { } markerLayerAfterCancel)
            {
                MapMarkerPresenter.ClearMarkers(mapAfterCancel, markerLayerAfterCancel);
            }

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

        // Presenter 呼び出し直前で現在のフィールドを再取得する。
        // ConfigureAwait(false) を挟んでいるため継続スレッドは保証されず、
        // 他スレッドの Cleanup() と真に並行し得るため、都度チェックが必要。
        if (points.Count == 0)
        {
            if (_map is { } mapForClear && _markerLayer is { } markerLayerForClear)
            {
                MapMarkerPresenter.ClearMarkers(mapForClear, markerLayerForClear);
            }

            ShowStatus(
                LocalizationService.GetString("MapStatus.LocationMissingTitle"),
                LocalizationService.GetString("MapStatus.LocationMissingSelectionDetail"),
                Symbol.Map);
            return;
        }

        if (points.Count == 1)
        {
            if (_map is not { } mapForSingle || _markerLayer is not { } markerLayerForSingle)
            {
                return;
            }

            var single = points[0];
            _markerPresenter.SetMarker(mapForSingle, markerLayerForSingle, single.Latitude, single.Longitude, single.Metadata, single.Item, _mapDefaultZoomLevel);
            HideStatus();
            return;
        }

        if (_map is not { } mapForMulti || _markerLayer is not { } markerLayerForMulti)
        {
            return;
        }

        _markerPresenter.SetMarkers(mapForMulti, markerLayerForMulti, points);
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

    internal void RequestPhotoFocus(string filePath)
    {
        _workspaceState.RequestPhotoFocus(filePath);
    }

    internal void RequestPhotoSelection(IReadOnlyList<string> filePaths)
    {
        _workspaceState.RequestPhotoSelection(filePaths);
    }

    internal void RequestNotification(string message, InfoBarSeverity severity)
    {
        _workspaceState.RequestNotification(message, severity);
    }

    private async void OnWorkspaceStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(WorkspaceState.SelectedPhotos))
        {
            return;
        }

        var selectedPhotos = _workspaceState.SelectedPhotos ?? Array.Empty<PhotoListItem>();
        await UpdateMarkersFromSelectionAsync(selectedPhotos).ConfigureAwait(true);
    }

    private void InitializeMapCore()
    {
        if (_map is not null)
        {
            return;
        }

        var (map, tileLayer, markerLayer) = _service.InitializeMap(_currentTileSource, UserAgentProvider.UserAgent);

        _baseTileLayer = tileLayer;
        _markerLayer = markerLayer;

        // Map プロパティ経由で _map を更新し、PropertyChanged を発火させる
        Map = map;
        HideStatus();
        AppLog.Info("Map initialized in MapPaneViewModel.");
    }

    private static bool TryGetValidLocation(PhotoMetadata metadata, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        if (!metadata.HasValidLocation
            || metadata.Latitude is not double lat
            || metadata.Longitude is not double lon)
        {
            return false;
        }

        latitude = lat;
        longitude = lon;
        return true;
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
