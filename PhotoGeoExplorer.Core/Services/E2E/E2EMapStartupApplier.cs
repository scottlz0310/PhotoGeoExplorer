using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Services.E2E;

/// <summary>
/// 起動時の環境変数で指定された地図表示（#363 案 A）を適用し、
/// 対象ビューポートのフレームが反映されて非ビジーになった時点で状態 JSON を 1 回ログ出力する。
/// 描画失敗時は <see cref="MapViewStatus.RenderedSeq"/> が進まないため完了ログを出力しない。
/// </summary>
internal sealed class E2EMapStartupApplier : IDisposable
{
    internal const string StatusLogPrefix = "[E2E][MapStatus] ";

    private readonly IMapViewController _controller;
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logError;
    private readonly Lock _gate = new();
    private long _baselineRenderedSeq;
    private bool _subscribed;
    private bool _logged;
    private bool _disposed;

    internal E2EMapStartupApplier(
        IMapViewController controller,
        Action<string>? logInfo = null,
        Action<string>? logError = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logInfo = logInfo ?? (_ => { });
        _logError = logError ?? (_ => { });
    }

    internal async Task ApplyAsync(MapViewTarget target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logInfo($"[E2E][MapView] Applying startup map view: lat={target.Latitude}, lon={target.Longitude}, zoom={target.Zoom}");

        // 移動前のフレーム反映と区別するため、SetViewAsync の直前の RenderedSeq を基準にする。
        var baselineSeq = _controller.CurrentStatus.RenderedSeq;
        lock (_gate)
        {
            _baselineRenderedSeq = baselineSeq;
        }

        var result = await _controller.SetViewAsync(target, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logError($"[E2E][MapView] Failed to apply startup map view: {result.Error}");
            return;
        }

        // 移動前の古い状態を誤って出力しないよう、SetViewAsync 完了後に購読を開始し、
        // 直後に CurrentStatus を再確認してフレーム反映・非ビジー遷移の取りこぼしを防ぐ。
        lock (_gate)
        {
            if (_disposed || _logged)
            {
                return;
            }

            if (!_subscribed)
            {
                _controller.StatusChanged += OnStatusChanged;
                _subscribed = true;
            }
        }

        TryEmitStatusLog(_controller.CurrentStatus);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Unsubscribe();
    }

    private void OnStatusChanged(object? sender, MapViewStatus status)
        => TryEmitStatusLog(status);

    private void TryEmitStatusLog(MapViewStatus status)
    {
        if (!status.HasValidViewport || status.IsBusy)
        {
            return;
        }

        var shouldLog = false;
        lock (_gate)
        {
            if (!_disposed && !_logged && status.RenderedSeq > _baselineRenderedSeq)
            {
                _logged = true;
                shouldLog = true;
            }
        }

        if (!shouldLog)
        {
            return;
        }

        Unsubscribe();
        _logInfo($"{StatusLogPrefix}{status.ToJson()}");
    }

    private void Unsubscribe()
    {
        var shouldUnsubscribe = false;
        lock (_gate)
        {
            if (_subscribed)
            {
                _subscribed = false;
                shouldUnsubscribe = true;
            }
        }

        if (shouldUnsubscribe)
        {
            _controller.StatusChanged -= OnStatusChanged;
        }
    }
}
