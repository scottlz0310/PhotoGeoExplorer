using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using Mapsui.Rendering;
using Mapsui.Styles;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
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
    private readonly IMapImageService _mapImageService;
    private readonly WorkspaceState _workspaceState;
    private readonly MapMarkerPresenter _markerPresenter;
    private readonly MapAttributionPresenter _attributionPresenter = new();
    private readonly MapViewController _viewController = new();
    private readonly ISecretStore _secretStore;
    private readonly FolderPickerHistory _saveFolderHistory = new();
    private readonly Func<DispatcherQueue?> _dispatcherQueueAccessor;
    private readonly IUiDispatcher _uiDispatcher;
    private DispatcherQueue? _dispatcherQueue;
    private double _pixelDensity = 1.0;
    private int _backgroundGeneration;
    private string _backgroundNotice = string.Empty;
    private Mapsui.Map? _map;
    private IMapBackground? _background;
    private MemoryLayer? _markerLayer;
    private MapTileSourceType _currentTileSource = MapTileSourceType.OpenStreetMap;
    private int _mapDefaultZoomLevel = MapZoomLevelCatalog.Default;
    private CancellationTokenSource? _mapUpdateCts;
    private int _selectionGeneration;
    private CancellationTokenSource? _mapImageSaveCts;
    private bool _isMapInitialized;
    private bool _hasValidMapViewport;
    private bool _isMapTileLoading;
    private bool _isMapImageSaving;
    private string _statusTitle = string.Empty;
    private string _statusDetail = string.Empty;
    private Symbol _statusIcon = Symbol.Map;
    private Visibility _statusVisibility = Visibility.Collapsed;

    public MapPaneViewModel()
        : this(new MapPaneService(), new WorkspaceState(), new MapImageService())
    {
    }

    internal MapPaneViewModel(IMapPaneService service)
        : this(service, new WorkspaceState(), new MapImageService())
    {
    }

    internal MapPaneViewModel(IMapPaneService service, WorkspaceState workspaceState)
        : this(service, workspaceState, new MapImageService())
    {
    }

    internal MapPaneViewModel(
        IMapPaneService service,
        WorkspaceState workspaceState,
        IMapImageService mapImageService,
        Func<DispatcherQueue?>? dispatcherQueueAccessor = null,
        ISecretStore? secretStore = null,
        IAreaNameResolver? areaNameResolver = null,
        IRoadNameSuggester? roadNameSuggester = null,
        IRoadNameConfirmationStore? roadNameConfirmationStore = null,
        AreaDataAcquisitionViewModel? areaDataAcquisition = null,
        IUiDispatcher? uiDispatcher = null)
    {
        LocationInfo = new MapLocationInfoViewModel(areaNameResolver, roadNameSuggester, roadNameConfirmationStore);
        AreaDataAcquisition = areaDataAcquisition ?? new AreaDataAcquisitionViewModel();
        _secretStore = secretStore ?? new PasswordVaultSecretStore();
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _workspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));
        _mapImageService = mapImageService ?? throw new ArgumentNullException(nameof(mapImageService));
        _dispatcherQueueAccessor = dispatcherQueueAccessor ?? DispatcherQueue.GetForCurrentThread;
        _uiDispatcher = uiDispatcher ?? new UiDispatcher();
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
        private set
        {
            if (SetProperty(ref _isMapInitialized, value))
            {
                OnPropertyChanged(nameof(CanSaveMapImage));
            }
        }
    }

    /// <summary>
    /// 地図画像を保存中かどうか
    /// </summary>
    public bool IsMapImageSaving
    {
        get => _isMapImageSaving;
        private set
        {
            if (SetProperty(ref _isMapImageSaving, value))
            {
                OnPropertyChanged(nameof(CanSaveMapImage));
            }
        }
    }

    /// <summary>
    /// 現在の地図表示を画像として保存できるかどうか
    /// </summary>
    public bool CanSaveMapImage => IsMapInitialized
        && _hasValidMapViewport
        && !_isMapTileLoading
        && !IsMapImageSaving;

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
    /// 背景地図についての注意書き（トークン未設定で代替表示中、認証エラーなど）。なければ空
    /// </summary>
    public string BackgroundNotice
    {
        get => _backgroundNotice;
        private set
        {
            if (SetProperty(ref _backgroundNotice, value))
            {
                OnPropertyChanged(nameof(BackgroundNoticeVisibility));
            }
        }
    }

    public Visibility BackgroundNoticeVisibility => string.IsNullOrEmpty(_backgroundNotice)
        ? Visibility.Collapsed
        : Visibility.Visible;

    /// <summary>
    /// 地図に表示中の帰属表示の文言
    /// </summary>
    internal string AttributionText => _background?.AttributionText ?? string.Empty;

    /// <summary>
    /// 背景が読み込み・描画の待機中かどうか。待機中は地図画像を保存できない
    /// </summary>
    internal bool IsBackgroundBusy => _background?.IsBusy == true;

    /// <summary>
    /// <see cref="IsBackgroundBusy"/> が変化したとき。任意のスレッドから呼ばれる
    /// </summary>
    internal event EventHandler? BackgroundBusyChanged;

    /// <summary>
    /// Mapsui に依存しない地図の表示状態の参照・操作契約（#362 / #363 / #364）
    /// </summary>
    internal IMapViewController ViewController => _viewController;

    /// <summary>
    /// 地図ヘッダーに表示する撮影位置情報
    /// </summary>
    public MapLocationInfoViewModel LocationInfo { get; }

    /// <summary>字地名の地域データが無い地域の写真を選んだときの、ダウンロードの案内と取得</summary>
    public AreaDataAcquisitionViewModel AreaDataAcquisition { get; }

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
                dispatcherQueue = _dispatcherQueueAccessor();
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

            _dispatcherQueue = dispatcherQueue;

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
        _mapImageSaveCts?.Cancel();
        UpdateMapImageSaveState(hasValidViewport: false, isTileLoading: false);
        IsMapInitialized = false;
        _mapUpdateCts?.Cancel();
        _mapUpdateCts?.Dispose();
        _mapUpdateCts = null;
        LocationInfo.Clear();

        _markerLayer?.Dispose();
        _markerLayer = null;

        var existingBackground = Interlocked.Exchange(ref _background, null);
        if (existingBackground is not null)
        {
            existingBackground.BusyChanged -= OnBackgroundBusyChanged;
            existingBackground.FrameRendered -= OnBackgroundFrameRendered;
            existingBackground.Dispose();
        }

        _viewController.Detach();
        _map?.Dispose();
        Map = null;
    }

    /// <summary>
    /// View が保持する Viewport とタイル読み込み状態を保存可否へ反映する
    /// </summary>
    public void UpdateMapImageSaveState(bool hasValidViewport, bool isTileLoading)
    {
        if (_hasValidMapViewport == hasValidViewport && _isMapTileLoading == isTileLoading)
        {
            return;
        }

        _hasValidMapViewport = hasValidViewport;
        _isMapTileLoading = isTileLoading;
        OnPropertyChanged(nameof(CanSaveMapImage));
    }

    /// <summary>
    /// 保存先選択と現在の地図スナップショット取得を View に委譲して PNG を保存する
    /// </summary>
    public async Task SaveMapImageAsync(
        MapImageSavePickerAsync showSavePickerAsync,
        MapImageSnapshotProviderAsync captureSnapshotAsync)
    {
        ArgumentNullException.ThrowIfNull(showSavePickerAsync);
        ArgumentNullException.ThrowIfNull(captureSnapshotAsync);

        if (!CanSaveMapImage)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _mapImageSaveCts = cts;
        IsMapImageSaving = true;

        try
        {
            var suggestedFileName = _mapImageService.CreateDefaultFileName(DateTimeOffset.Now);
            var pickerOptions = MapImageSavePickerOptions.Create(
                _saveFolderHistory.GetStartFolder(MapImageSavePickerOptions.SettingsIdentifier),
                _workspaceState.CurrentFolderPath,
                suggestedFileName);
            var outputPath = await showSavePickerAsync(pickerOptions, cts.Token).ConfigureAwait(true);

            if (outputPath is null)
            {
                RequestNotification(
                    LocalizationService.GetString("Message.MapImageSaveCanceled"),
                    InfoBarSeverity.Informational);
                return;
            }

            _saveFolderHistory.RecordFileLocation(MapImageSavePickerOptions.SettingsIdentifier, outputPath);
            using var pngStream = await captureSnapshotAsync(cts.Token).ConfigureAwait(true);
            await _mapImageService.SavePngAsync(pngStream, outputPath, cts.Token).ConfigureAwait(true);
            RequestNotification(
                $"{LocalizationService.GetString("Message.MapImageSaveSucceeded")} {outputPath}",
                InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            RequestNotification(
                LocalizationService.GetString("Message.MapImageSaveCanceled"),
                InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save the current map image.", ex);
            RequestNotification(
                $"{LocalizationService.GetString("Message.MapImageSaveFailed")} {ex.Message}",
                InfoBarSeverity.Error);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_mapImageSaveCts, cts))
            {
                _mapImageSaveCts = null;
            }

            cts.Dispose();
            IsMapImageSaving = false;
        }
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

        // Clear() が前の選択の解決を取り消すと、その待機側が先に再開し得る。世代は、その前に進めておく
        var generation = ++_selectionGeneration;

        // 前の選択の撮影位置情報を、新しい選択のメタデータ読み込みを待つ前に消す。
        // 読み込み中に前の写真の座標が表示・コピーできる状態を残さない
        LocationInfo.Clear();

        // 既存の更新をキャンセル
        var previousCts = _mapUpdateCts;
        _mapUpdateCts = null;
        if (previousCts is not null)
        {
            // ConfigureAwait(false) にすると、登録済みコールバックの実行でスレッドプールへ移った後、
            // ShowStatus / HideStatus が UI スレッド外で PropertyChanged を発火して RPC_E_WRONG_THREAD になる (#277)
            await previousCts.CancelAsync().ConfigureAwait(true);
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

        ApplyMarkers(points);

        // 撮影位置情報は単一選択の写真だけを対象にする。await 中に Cleanup() が走った場合は設定しない
        if (selectedItems.Count == 1 && points.Count == 1 && _map is not null)
        {
            var selected = points[0];
            var photo = new PhotoIdentity(selected.Item.FilePath, selected.Item.SizeBytes, selected.Metadata.TakenAt);
            await LocationInfo.SetLocationAsync(selected.Latitude, selected.Longitude, photo).ConfigureAwait(true);

            // 解決の待ち中に、選択の変更（解除・複数選択・別の写真）や Cleanup() があった場合は、古い選択のために通信や確認を始めない
            if (generation != _selectionGeneration || !ReferenceEquals(_mapUpdateCts, cts) || cts.IsCancellationRequested)
            {
                return;
            }

            // 確認ダイアログの応答や取得を待たない（選択の処理を止めない）。想定内の失敗は ViewModel が通知とログで報告する。
            // 想定外の例外は、観測されないタスクの例外としてアプリを落とさないよう、ここでログに残す
            _ = AreaDataAcquisition.OnLocationAsync(selected.Latitude, selected.Longitude).ContinueWith(
                task => AppLog.Error("The area data acquisition failed unexpectedly.", task.Exception),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    private void ApplyMarkers(List<(double Latitude, double Longitude, PhotoMetadata Metadata, PhotoItem Item)> points)
    {
        // Presenter 呼び出し直前で現在のフィールドを再取得する。
        // await 中に Cleanup() が走り得るため、都度チェックが必要。
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
            var background = CreateBackgroundRequest(newSource, out var notice);
            var newBackground = _service.CreateBackground(background);
            ReplaceBackground(_map, newBackground);
            newBackground.Attach(_map, GetPixelDensity);
            CurrentTileSource = newSource;
            _attributionPresenter.Update(background.SourceType);
            BackgroundNotice = notice;
            _viewController.UpdateTileSource(newSource, background.SourceType, newBackground.AttributionText, newBackground.IsBusy);

            _map.Refresh();
            AppLog.Info($"Switched map tile source to {newSource} (displaying {background.SourceType})");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            AppLog.Error("Map tile switch failed.", ex);
        }
    }

    // 新しい背景を先に作ってから差し替える。作成に失敗しても、表示中の背景は変わらない
    private void ReplaceBackground(Mapsui.Map map, IMapBackground newBackground)
    {
        if (Volatile.Read(ref _background) is { } oldBackground)
        {
            oldBackground.BusyChanged -= OnBackgroundBusyChanged;
            oldBackground.FrameRendered -= OnBackgroundFrameRendered;
            foreach (var layer in oldBackground.Layers)
            {
                map.Layers.Remove(layer);
            }

            oldBackground.Dispose();
        }

        var index = 0;
        foreach (var layer in newBackground.Layers)
        {
            map.Layers.Insert(index++, layer);
        }

        newBackground.BusyChanged += OnBackgroundBusyChanged;
        newBackground.FrameRendered += OnBackgroundFrameRendered;
        Volatile.Write(ref _background, newBackground);
    }

    internal Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService)
    {
        var background = Volatile.Read(ref _background);
        return background?.BeginMapRender(viewport, layers, renderService);
    }

    private void OnBackgroundBusyChanged(object? sender, EventArgs e)
    {
        _viewController.UpdateBusy(IsBackgroundBusy);
        BackgroundBusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackgroundFrameRendered(object? sender, EventArgs e)
        => _viewController.NotifyFrameRendered();

    /// <summary>
    /// 現在のビューポートを、背景を保存用に差し替えて PNG に描く。表示中のレイヤーには触らない
    /// </summary>
    internal async Task<Stream> CaptureMapImageAsync(
        Viewport viewport,
        float pixelDensity,
        CancellationToken cancellationToken)
    {
        var map = _map ?? throw new InvalidOperationException("The map is not initialized.");
        var background = _background ?? throw new InvalidOperationException("The map background is not initialized.");

        using var exportLayers = await background.CreateExportLayersAsync(viewport, pixelDensity, cancellationToken).ConfigureAwait(true);
        var layers = MapExportLayers.Compose(map.Layers, background, exportLayers);
        return _mapImageService.RenderPng(map, layers, viewport, pixelDensity);
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

        var background = CreateBackgroundRequest(_currentTileSource, out var notice);
        var (map, mapBackground, markerLayer) = _service.InitializeMap(background);

        Volatile.Write(ref _background, mapBackground);
        _viewController.Attach(
            map,
            _currentTileSource,
            background.SourceType,
            mapBackground.AttributionText,
            GetPixelDensity(),
            mapBackground.IsBusy,
            HideStatusOnUiThread,
            InvokeOnUiThreadAsync);
        mapBackground.BusyChanged += OnBackgroundBusyChanged;
        mapBackground.FrameRendered += OnBackgroundFrameRendered;
        mapBackground.Attach(map, GetPixelDensity);
        _markerLayer = markerLayer;
        _attributionPresenter.Attach(map, background.SourceType);
        _ = LoadAttributionImagesAsync(map);
        BackgroundNotice = notice;

        // Map プロパティ経由で _map を更新し、PropertyChanged を発火させる
        Map = map;
        HideStatus();
        AppLog.Info("Map initialized in MapPaneViewModel.");
    }

    private async Task LoadAttributionImagesAsync(Mapsui.Map map)
    {
        try
        {
            if (!await _attributionPresenter.LoadImagesAsync(map).ConfigureAwait(true))
            {
                AppLog.Error($"Failed to load the map attribution logo: {MapAttribution.MapboxLogoPath}");
                return;
            }

            map.RefreshGraphics();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Error("Failed to load the map attribution logo.", ex);
        }
    }

    // 設定は Mapbox でもトークンがなければ OSM で表示する。保存済みの設定値（CurrentTileSource）は書き換えない。
    private MapBackgroundRequest CreateBackgroundRequest(MapTileSourceType requested, out string notice)
    {
        var generation = ++_backgroundGeneration;
        notice = string.Empty;
        if (requested != MapTileSourceType.MapboxSatellite)
        {
            return new MapBackgroundRequest(requested, UserAgentProvider.UserAgent, TryPostToUi: TryPostToUi);
        }

        var token = ReadMapboxAccessToken();
        if (token is null)
        {
            notice = LocalizationService.GetString("MapStatus.MapboxTokenMissing");
            return new MapBackgroundRequest(MapTileSourceType.OpenStreetMap, UserAgentProvider.UserAgent, TryPostToUi: TryPostToUi);
        }

        return new MapBackgroundRequest(
            requested,
            UserAgentProvider.UserAgent,
            token,
            () => OnMapboxAuthenticationFailed(generation),
            TryPostToUi);
    }

    // 任意のスレッドから呼ばれる（ベクター背景は、描いた画像の反映を UI スレッドへ渡すために使う）
    private bool TryPostToUi(Action action)
    {
        var dispatcherQueue = _dispatcherQueue;
        return dispatcherQueue is not null && dispatcherQueue.TryEnqueue(() => action());
    }

    private double GetPixelDensity() => Volatile.Read(ref _pixelDensity);

    /// <summary>
    /// 地図の画素密度（物理ピクセル／論理ピクセル）を更新する。View が読み込み時と、表示倍率の変化（別のモニターへの移動など）で呼ぶ。
    /// 密度は XAML からしか分からず、視点の通知は UI スレッド以外から来るため、ここで保持して背景へは取得関数で渡す。
    /// </summary>
    internal void UpdatePixelDensity(double pixelDensity)
    {
        if (!double.IsFinite(pixelDensity) || pixelDensity <= 0 || pixelDensity == GetPixelDensity())
        {
            return;
        }

        Volatile.Write(ref _pixelDensity, pixelDensity);
        _viewController.UpdatePixelDensity(pixelDensity);
        _background?.OnPixelDensityChanged();
    }

    private void HideStatusOnUiThread()
    {
        if (!_uiDispatcher.TryEnqueue(HideStatus))
        {
            HideStatus();
        }
    }

    private Task InvokeOnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _uiDispatcher.RunAsync(action).WaitAsync(cancellationToken);
    }

    private string? ReadMapboxAccessToken()
    {
        try
        {
            var token = _secretStore.Get(SecretKeys.MapboxAccessToken);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (COMException ex)
        {
            AppLog.Error("Failed to read the Mapbox access token from the credential locker.", ex);
            return null;
        }
    }

    // タイル取得スレッドから呼ばれる。切り替え済みの古い背景からの通知は捨てる。
    private void OnMapboxAuthenticationFailed(int generation)
    {
        void Apply()
        {
            if (generation == _backgroundGeneration)
            {
                BackgroundNotice = LocalizationService.GetString("MapStatus.MapboxAuthFailed");
            }
        }

        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            Apply();
            return;
        }

        _ = dispatcherQueue.TryEnqueue(Apply);
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
