using System;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Extensions;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// <see cref="IMapViewController"/> の Mapsui 実装。
/// Mapsui の <see cref="Navigator"/> への変換と状態スナップショットの構築をここに閉じ込める（#362 / #363 / #364）。
/// </summary>
internal sealed class MapViewController : IMapViewController
{
    private readonly Lock _gate = new();
    private Mapsui.Map? _map;
    private Action? _onViewApplied;
    private Func<Action, CancellationToken, Task>? _invokeOnUiThreadAsync;
    private MapTileSourceType _configuredTileSource = MapTileSourceType.OpenStreetMap;
    private MapTileSourceType _displayedTileSource = MapTileSourceType.OpenStreetMap;
    private string _attributionText = string.Empty;
    private double _pixelDensity = 1.0;
    private bool _isBusy;
    private long _renderedSeq;
    private MapViewTarget? _pendingTarget;
    private MapViewStatus _currentStatus = MapViewStatus.Empty;

    public MapViewStatus CurrentStatus
    {
        get
        {
            lock (_gate)
            {
                return _currentStatus;
            }
        }
    }

    public event EventHandler<MapViewStatus>? StatusChanged;

    internal void Attach(
        Mapsui.Map map,
        MapTileSourceType configuredTileSource,
        MapTileSourceType displayedTileSource,
        string attributionText,
        double pixelDensity,
        bool isBusy,
        Action? onViewApplied = null,
        Func<Action, CancellationToken, Task>? invokeOnUiThreadAsync = null)
    {
        ArgumentNullException.ThrowIfNull(map);

        Mapsui.Map? previousMap;
        MapViewStatus snapshot;
        lock (_gate)
        {
            previousMap = _map;
            _map = map;
            _configuredTileSource = configuredTileSource;
            _displayedTileSource = displayedTileSource;
            _attributionText = attributionText ?? string.Empty;
            _pixelDensity = double.IsFinite(pixelDensity) && pixelDensity > 0 ? pixelDensity : 1.0;
            _isBusy = isBusy;
            _onViewApplied = onViewApplied;
            _invokeOnUiThreadAsync = invokeOnUiThreadAsync;
            _pendingTarget = null;
            snapshot = RebuildSnapshotLocked();
        }

        if (previousMap is not null)
        {
            previousMap.Navigator.ViewportChanged -= OnViewportChanged;
        }

        map.Navigator.ViewportChanged += OnViewportChanged;
        RaiseStatusChanged(snapshot);
    }

    internal void Detach()
    {
        Mapsui.Map? previousMap;
        MapViewStatus snapshot;
        lock (_gate)
        {
            previousMap = _map;
            _map = null;
            _onViewApplied = null;
            _invokeOnUiThreadAsync = null;
            _pendingTarget = null;
            _isBusy = false;
            snapshot = RebuildSnapshotLocked();
        }

        if (previousMap is not null)
        {
            previousMap.Navigator.ViewportChanged -= OnViewportChanged;
        }

        RaiseStatusChanged(snapshot);
    }

    internal void UpdateTileSource(
        MapTileSourceType configuredTileSource,
        MapTileSourceType displayedTileSource,
        string attributionText,
        bool isBusy)
    {
        MapViewStatus? changedSnapshot = null;
        lock (_gate)
        {
            _configuredTileSource = configuredTileSource;
            _displayedTileSource = displayedTileSource;
            _attributionText = attributionText ?? string.Empty;
            _isBusy = isBusy;
            changedSnapshot = UpdateSnapshotIfChangedLocked();
        }

        if (changedSnapshot is not null)
        {
            RaiseStatusChanged(changedSnapshot);
        }
    }

    internal void UpdatePixelDensity(double pixelDensity)
    {
        if (!double.IsFinite(pixelDensity) || pixelDensity <= 0)
        {
            return;
        }

        MapViewStatus? changedSnapshot = null;
        lock (_gate)
        {
            _pixelDensity = pixelDensity;
            changedSnapshot = UpdateSnapshotIfChangedLocked();
        }

        if (changedSnapshot is not null)
        {
            RaiseStatusChanged(changedSnapshot);
        }
    }

    internal void UpdateBusy(bool isBusy)
    {
        MapViewStatus? changedSnapshot = null;
        lock (_gate)
        {
            if (_isBusy == isBusy)
            {
                return;
            }

            _isBusy = isBusy;
            changedSnapshot = UpdateSnapshotIfChangedLocked();
        }

        if (changedSnapshot is not null)
        {
            RaiseStatusChanged(changedSnapshot);
        }
    }

    /// <summary>
    /// 現在の対象ビューポートに対するフレーム反映が完了したことを記録し、<see cref="MapViewStatus.RenderedSeq"/> を進める。
    /// 描画失敗や古い過渡フレームでは呼び出さない。
    /// </summary>
    internal void NotifyFrameRendered()
    {
        MapViewStatus? changedSnapshot = null;
        lock (_gate)
        {
            if (_map is null || _pendingTarget is not null)
            {
                return;
            }

            _renderedSeq++;
            changedSnapshot = UpdateSnapshotIfChangedLocked();
        }

        if (changedSnapshot is not null)
        {
            RaiseStatusChanged(changedSnapshot);
        }
    }

    public async Task<MapViewCommandResult> SetViewAsync(MapViewTarget target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!MapViewTarget.TryCreate(target.Latitude, target.Longitude, target.Zoom, out var validated, out var validationError))
        {
            return new MapViewCommandResult(false, CurrentStatus, validationError);
        }

        Mapsui.Map? map;
        Action? onViewApplied;
        Func<Action, CancellationToken, Task>? invokeOnUiThreadAsync;
        lock (_gate)
        {
            map = _map;
            onViewApplied = _onViewApplied;
            invokeOnUiThreadAsync = _invokeOnUiThreadAsync;
        }

        if (map is null)
        {
            return new MapViewCommandResult(false, CurrentStatus, "The map is not initialized.");
        }

        var applied = false;
        void Apply()
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_map, map))
                {
                    return;
                }

                if (!map.Navigator.Viewport.HasSize())
                {
                    // 初回レイアウト前は解像度適用が反映されない場合があるため、サイズ確定時に再適用する
                    _pendingTarget = validated;
                }
                else
                {
                    _pendingTarget = null;
                }
            }

            ApplyTargetToMap(map, validated);
            onViewApplied?.Invoke();
            applied = true;
        }

        if (invokeOnUiThreadAsync is not null)
        {
            await invokeOnUiThreadAsync(Apply, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Apply();
        }

        if (!applied)
        {
            return new MapViewCommandResult(false, CurrentStatus, "The map was detached before applying the view.");
        }

        MapViewStatus snapshot;
        bool changed;
        lock (_gate)
        {
            var previous = _currentStatus;
            snapshot = RebuildSnapshotLocked();
            changed = snapshot != previous;
        }

        if (changed)
        {
            RaiseStatusChanged(snapshot);
        }

        return new MapViewCommandResult(true, snapshot);
    }

    private void OnViewportChanged(object? sender, EventArgs e)
    {
        Mapsui.Map? mapToApply;
        MapViewTarget target;
        Action? onViewApplied;
        MapViewStatus? changedSnapshot = null;

        lock (_gate)
        {
            if (TryTakePendingTargetLocked(out mapToApply, out target, out onViewApplied))
            {
                // ロックの外で適用する
            }
            else
            {
                changedSnapshot = UpdateSnapshotIfChangedLocked();
            }
        }

        if (mapToApply is not null)
        {
            ApplyTargetToMap(mapToApply, target);
            onViewApplied?.Invoke();
            lock (_gate)
            {
                changedSnapshot = UpdateSnapshotIfChangedLocked();
            }
        }

        if (changedSnapshot is not null)
        {
            RaiseStatusChanged(changedSnapshot);
        }
    }

    private bool TryTakePendingTargetLocked(
        out Mapsui.Map? map,
        out MapViewTarget target,
        out Action? onViewApplied)
    {
        if (_map is { } currentMap && _pendingTarget is { } pending && currentMap.Navigator.Viewport.HasSize())
        {
            map = currentMap;
            target = pending;
            onViewApplied = _onViewApplied;
            _pendingTarget = null;
            return true;
        }

        map = null;
        target = default;
        onViewApplied = null;
        return false;
    }

    private static void ApplyTargetToMap(Mapsui.Map map, MapViewTarget target)
    {
        var center = new MPoint(
            WebMercator.FromLongitude(target.Longitude),
            WebMercator.FromLatitude(target.Latitude));
        var resolution = WebMercator.ResolutionFromLogicalZoom(target.Zoom);
        map.Navigator.CenterOnAndZoomTo(center, resolution, duration: 0);
        map.Refresh();
    }

    private MapViewStatus? UpdateSnapshotIfChangedLocked()
    {
        var previous = _currentStatus;
        var next = RebuildSnapshotLocked();
        return next == previous ? null : next;
    }

    private MapViewStatus RebuildSnapshotLocked()
    {
        if (_map is null)
        {
            _currentStatus = MapViewStatus.Create(
                hasSize: false,
                centerX: 0,
                centerY: 0,
                resolution: 0,
                widthDip: 0,
                heightDip: 0,
                pixelDensity: _pixelDensity,
                configuredTileSource: _configuredTileSource,
                displayedTileSource: _displayedTileSource,
                attributionText: _attributionText,
                isBusy: _isBusy,
                renderedSeq: _renderedSeq);
            return _currentStatus;
        }

        var viewport = _map.Navigator.Viewport;
        _currentStatus = MapViewStatus.Create(
            hasSize: viewport.HasSize(),
            centerX: viewport.CenterX,
            centerY: viewport.CenterY,
            resolution: viewport.Resolution,
            widthDip: viewport.Width,
            heightDip: viewport.Height,
            pixelDensity: _pixelDensity,
            configuredTileSource: _configuredTileSource,
            displayedTileSource: _displayedTileSource,
            attributionText: _attributionText,
            isBusy: _isBusy,
            renderedSeq: _renderedSeq);
        return _currentStatus;
    }

    private void RaiseStatusChanged(MapViewStatus status)
        => StatusChanged?.Invoke(this, status);
}
