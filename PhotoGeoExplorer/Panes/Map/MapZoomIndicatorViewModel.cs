using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図ヘッダーに表示する現在の論理ズームレベルと詳細ツールチップの ViewModel（#364）。
/// 状態の出所は #363 の <see cref="IMapViewController"/> / <see cref="MapViewStatus"/> と共有し、
/// Mapsui の型に依存しない。任意スレッドからの高頻度な状態通知を UI スレッドへ移して間引く。
/// </summary>
internal sealed class MapZoomIndicatorViewModel : BindableBase, IDisposable
{
    internal static readonly TimeSpan DefaultThrottleInterval = TimeSpan.FromMilliseconds(50);

    private readonly IMapViewController _controller;
    private readonly MapHeaderDisplaySettings _displaySettings;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _throttleInterval;
    private readonly Func<string, string> _getLocalizedString;
    private readonly Lock _gate = new();
    private MapViewStatus _latestStatus;
    private IUiDispatcherTimer? _throttleTimer;
    private DateTimeOffset? _lastAppliedAt;
    private bool _hasPendingUpdate;
    private bool _dispatchQueued;
    private bool _timerRunning;
    private bool _disposed;
    private string _zoomText = string.Empty;
    private string? _zoomToolTip;

    internal MapZoomIndicatorViewModel(
        IMapViewController controller,
        MapHeaderDisplaySettings displaySettings,
        IUiDispatcher uiDispatcher,
        TimeProvider? timeProvider = null,
        TimeSpan? throttleInterval = null,
        Func<string, string>? getLocalizedString = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _displaySettings = displaySettings ?? throw new ArgumentNullException(nameof(displaySettings));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _throttleInterval = throttleInterval ?? DefaultThrottleInterval;
        _getLocalizedString = getLocalizedString ?? LocalizationService.GetString;
        if (_throttleInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(throttleInterval), "Throttle interval must be non-negative.");
        }

        _latestStatus = _controller.CurrentStatus;
        _controller.StatusChanged += OnViewStatusChanged;
        _displaySettings.PropertyChanged += OnDisplaySettingsChanged;
        ApplyStatusOnUiThread(_latestStatus);
    }

    public string ZoomText
    {
        get => _zoomText;
        private set
        {
            if (SetProperty(ref _zoomText, value))
            {
                OnPropertyChanged(nameof(ZoomVisibility));
            }
        }
    }

    public Visibility ZoomVisibility => _zoomText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public string? ZoomToolTip
    {
        get => _zoomToolTip;
        private set => SetProperty(ref _zoomToolTip, value);
    }

    public void Dispose()
    {
        IUiDispatcherTimer? timerToStop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _hasPendingUpdate = false;
            _dispatchQueued = false;
            _timerRunning = false;
            timerToStop = _throttleTimer;
            _throttleTimer = null;
        }

        _controller.StatusChanged -= OnViewStatusChanged;
        _displaySettings.PropertyChanged -= OnDisplaySettingsChanged;

        if (timerToStop is not null)
        {
            timerToStop.Tick -= OnThrottleTimerTick;
            if (!_uiDispatcher.TryEnqueue(timerToStop.Stop))
            {
                timerToStop.Stop();
            }
        }
    }

    internal static string FormatZoomLabel(double logicalZoom)
        => $"z{logicalZoom.ToString("F1", CultureInfo.InvariantCulture)}";

    internal static string BuildToolTip(
        MapViewStatus status,
        double logicalZoom,
        int styleZoom,
        Func<string, string>? getLocalizedString = null)
    {
        ArgumentNullException.ThrowIfNull(status);

        var localize = getLocalizedString ?? LocalizationService.GetString;
        var logicalZoomText = logicalZoom.ToString("F1", CultureInfo.InvariantCulture);
        var styleZoomText = styleZoom.ToString(CultureInfo.InvariantCulture);
        var zoomLine = FormatLocalized(localize, "MapZoom.ToolTip.ZoomLine", logicalZoomText, styleZoomText);

        var densityPercent = ((int)Math.Round(status.PixelDensity * 100.0, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture);
        var densityRatio = status.PixelDensity.ToString("F2", CultureInfo.InvariantCulture);
        var densityLine = FormatLocalized(localize, "MapZoom.ToolTip.PixelDensityLine", densityPercent, densityRatio);

        var displayedLabel = GetTileSourceLabel(localize, status.TileSource.Displayed);
        var backgroundLine = string.Equals(status.TileSource.Configured, status.TileSource.Displayed, StringComparison.Ordinal)
            ? FormatLocalized(localize, "MapZoom.ToolTip.BackgroundLine", displayedLabel)
            : FormatLocalized(
                localize,
                "MapZoom.ToolTip.BackgroundFallbackLine",
                displayedLabel,
                GetTileSourceLabel(localize, status.TileSource.Configured));

        return string.Join('\n', [zoomLine, densityLine, backgroundLine]);
    }

    private static string FormatLocalized(Func<string, string> localize, string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, localize(key), args);

    private static string GetTileSourceLabel(Func<string, string> localize, string tileSourceName)
    {
        if (string.IsNullOrWhiteSpace(tileSourceName))
        {
            return string.Empty;
        }

        var key = $"MapZoom.TileSource.{tileSourceName}";
        var localized = localize(key);
        return string.Equals(localized, key, StringComparison.Ordinal) ? tileSourceName : localized;
    }

    private void OnViewStatusChanged(object? sender, MapViewStatus status)
    {
        bool shouldEnqueue;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _latestStatus = status;
            _hasPendingUpdate = true;
            shouldEnqueue = !_dispatchQueued;
            if (shouldEnqueue)
            {
                _dispatchQueued = true;
            }
        }

        if (!shouldEnqueue)
        {
            return;
        }

        if (_uiDispatcher.TryEnqueue(HandleQueuedUpdateOnUiThread))
        {
            return;
        }

        HandleQueuedUpdateOnUiThread();
    }

    private void OnDisplaySettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MapHeaderDisplaySettings.ShowZoom) or null or ""))
        {
            return;
        }

        MapViewStatus latest;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            latest = _latestStatus;
        }

        if (_uiDispatcher.TryEnqueue(() => ApplyStatusOnUiThread(latest)))
        {
            return;
        }

        ApplyStatusOnUiThread(latest);
    }

    private void HandleQueuedUpdateOnUiThread()
    {
        MapViewStatus statusToApply;
        lock (_gate)
        {
            _dispatchQueued = false;
            if (_disposed || !_hasPendingUpdate)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            if (_throttleInterval > TimeSpan.Zero && _lastAppliedAt is { } lastApplied)
            {
                var elapsed = now - lastApplied;
                if (elapsed < _throttleInterval && EnsureThrottleTimerLocked() is { } timer)
                {
                    if (!_timerRunning)
                    {
                        timer.Interval = _throttleInterval - elapsed;
                        _timerRunning = true;
                        timer.Start();
                    }

                    return;
                }
            }

            _hasPendingUpdate = false;
            _lastAppliedAt = now;
            statusToApply = _latestStatus;
        }

        ApplyStatusOnUiThread(statusToApply);
    }

    private void OnThrottleTimerTick(object? sender, EventArgs e)
    {
        MapViewStatus statusToApply;
        lock (_gate)
        {
            _timerRunning = false;
            _throttleTimer?.Stop();
            if (_disposed || !_hasPendingUpdate)
            {
                return;
            }

            _hasPendingUpdate = false;
            _lastAppliedAt = _timeProvider.GetUtcNow();
            statusToApply = _latestStatus;
        }

        ApplyStatusOnUiThread(statusToApply);
    }

    private IUiDispatcherTimer? EnsureThrottleTimerLocked()
    {
        if (_throttleTimer is not null)
        {
            return _throttleTimer;
        }

        var timer = _uiDispatcher.CreateTimer();
        if (timer is null)
        {
            return null;
        }

        timer.Tick += OnThrottleTimerTick;
        _throttleTimer = timer;
        return timer;
    }

    private void ApplyStatusOnUiThread(MapViewStatus status)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        if (!_displaySettings.ShowZoom
            || !status.HasValidViewport
            || status.Zoom is not { } logicalZoom
            || !double.IsFinite(logicalZoom)
            || status.StyleZoom is not { } styleZoom)
        {
            ZoomText = string.Empty;
            ZoomToolTip = null;
            return;
        }

        ZoomText = FormatZoomLabel(logicalZoom);
        ZoomToolTip = BuildToolTip(status, logicalZoom, styleZoom, _getLocalizedString);
    }
}
