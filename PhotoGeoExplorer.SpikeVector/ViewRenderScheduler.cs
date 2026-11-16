using System.Diagnostics;

namespace PhotoMapStudio.Spike;

/// <summary>描画の内訳（デコード済みキャッシュの効果を見るため、取得・デコード・描画を分ける）。</summary>
public readonly record struct RenderTimings(TimeSpan Fetch, TimeSpan Decode, TimeSpan Draw, int Tiles, int CacheHits, TimeSpan Polygons = default, TimeSpan Lines = default, TimeSpan Symbols = default, TimeSpan LineLabels = default, TimeSpan Labels = default);

/// <summary>描画の出力。画像の参照（1 つ）を持つ。受け取った側が <see cref="RefCountedImage.Release"/> する。</summary>
public sealed record RenderOutput(RefCountedImage Image, RenderTimings Timings);

public sealed record RenderResult(RenderRequest Request, RefCountedImage Image, TimeSpan Duration, RenderTimings Timings);

public readonly record struct ViewRenderStats(
    long Submitted,
    long Replaced,
    long Started,
    long Delivered,
    long DiscardedStale,
    long Cancelled,
    long Failed);

/// <summary>
/// 要求を集約して、背景スレッドで 1 つずつ描く。
/// - 未着手の古い要求は、最新で置き換える（<see cref="ViewRenderStats.Replaced"/>）。
/// - 実行中の描画は、原則として完走させる（連続操作中に、確定描画が完了せず白紙化するのを防ぐ）。
///   キャンセルするのは、ソース・スタイルが変わった場合と、最新の表示範囲が描画範囲と重ならなくなった場合、破棄のときに加えて、
///   <see cref="IdleCancelAfter"/> が有効なとき、操作が止まった（一定時間、新しい要求が来ない）のに、実行中の描画が古い視点のままの場合
///   （最新の描画の開始が、古い描画の完了待ちで遅れるのを避ける。ほぼ終わっている描画は、完了させる）。
/// - 反映は世代番号で単調にする（古い世代の結果で、新しい結果を上書きしない）。互換（同じソース・スタイル）で、
///   最新の表示範囲と重なる結果だけを反映し、それ以外は破棄して画像を解放する。
/// </summary>
public sealed class ViewRenderScheduler : IDisposable
{
    private readonly Func<RenderRequest, CancellationToken, Task<RenderOutput>> _render;
    private readonly Action<RenderResult> _deliver;
    private readonly object _gate = new();
    private RenderRequest? _pending;
    private RenderRequest? _latest;
    private RenderRequest? _running;
    private CancellationTokenSource? _runningCts;
    private bool _loopActive;
    private bool _disposed;
    private long _lastDeliveredGeneration = -1;
    private long _submitted;
    private long _replaced;
    private long _started;
    private long _delivered;
    private long _discarded;
    private long _cancelled;
    private long _failed;
    private Timer? _idleTimer;
    private long _lastSubmitTicks;
    private long _runningStartTicks;
    private double _averageDurationMs;
    private long _idleCancelled;

    /// <summary>
    /// 操作が止まったとみなす時間（最後の要求から）。0 で無効。止まったとき、実行中の描画が最新の要求と別の世代で、
    /// かつ、直近の描画時間の 70% を超えて進んでいなければ、キャンセルして最新の要求を先に描く。
    /// </summary>
    public TimeSpan IdleCancelAfter { get; init; } = TimeSpan.Zero;

    public long IdleCancelled
    {
        get
        {
            lock (_gate)
            {
                return _idleCancelled;
            }
        }
    }

    /// <param name="render">1 枚描く。キャンセルされたら <see cref="OperationCanceledException"/> を投げてよい。</param>
    /// <param name="deliver">結果の反映（背景スレッドから呼ばれる。UI へ渡す側で切り替える）。受け取った参照は、反映先が解放する。</param>
    public ViewRenderScheduler(Func<RenderRequest, CancellationToken, Task<RenderOutput>> render, Action<RenderResult> deliver)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
    }

    public ViewRenderStats Stats
    {
        get
        {
            lock (_gate)
            {
                return new ViewRenderStats(_submitted, _replaced, _started, _delivered, _discarded, _cancelled, _failed);
            }
        }
    }

    /// <summary>描画が動いているか、未着手の要求があるか。</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _loopActive;
            }
        }
    }

    public void Submit(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _submitted++;
            if (_pending is not null)
            {
                _replaced++;
            }

            _pending = request;
            _latest = request;
            _lastSubmitTicks = Stopwatch.GetTimestamp();
            if (IdleCancelAfter > TimeSpan.Zero)
            {
                _idleTimer ??= new Timer(OnIdle);
                _idleTimer.Change(IdleCancelAfter, Timeout.InfiniteTimeSpan);
            }

            if (_running is not null && ShouldCancelRunning(_running, request))
            {
                _runningCts?.Cancel();
            }

            if (!_loopActive)
            {
                _loopActive = true;
                _ = Task.Run(LoopAsync);
            }
        }
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
            _pending = null;
            _runningCts?.Cancel();
            _idleTimer?.Dispose();
        }
    }

    /// <summary>実行中の描画をキャンセルするか。互換でない（ソース・スタイルの変更）か、範囲が重ならない場合。</summary>
    internal static bool ShouldCancelRunning(RenderRequest running, RenderRequest newest)
        => !running.IsCompatibleWith(newest) || !running.Extent.Intersects(newest.Extent);

    private async Task LoopAsync()
    {
        while (true)
        {
            RenderRequest request;
            CancellationTokenSource cts;
            lock (_gate)
            {
                if (_disposed || _pending is null)
                {
                    _loopActive = false;
                    return;
                }

                request = _pending;
                _pending = null;
                cts = new CancellationTokenSource();
                _running = request;
                _runningCts = cts;
                _runningStartTicks = Stopwatch.GetTimestamp();
                _started++;
            }

            RenderOutput? output = null;
            var cancelled = false;
            var failed = false;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                output = await _render(request, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception)
            {
                failed = true;
            }

            stopwatch.Stop();

            RefCountedImage? toDeliver = null;
            lock (_gate)
            {
                _running = null;
                _runningCts = null;
                cts.Dispose();

                if (cancelled)
                {
                    _cancelled++;
                }
                else if (failed)
                {
                    _failed++;
                }
                else if (output is not null)
                {
                    var ms = stopwatch.Elapsed.TotalMilliseconds;
                    _averageDurationMs = _averageDurationMs == 0 ? ms : (_averageDurationMs * 0.7) + (ms * 0.3);
                    if (ShouldApply(request))
                    {
                        _lastDeliveredGeneration = request.Generation;
                        _delivered++;
                        toDeliver = output.Image;
                    }
                    else
                    {
                        _discarded++;
                        output.Image.Release();
                    }
                }
            }

            if (toDeliver is not null && output is not null)
            {
                try
                {
                    _deliver(new RenderResult(request, toDeliver, stopwatch.Elapsed, output.Timings));
                }
                catch
                {
                    toDeliver.Release();
                    throw;
                }
            }
        }
    }

    private void OnIdle(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _running is null || _latest is null || _running.Generation == _latest.Generation || _pending is null)
            {
                return;
            }

            var sinceSubmit = (Stopwatch.GetTimestamp() - _lastSubmitTicks) * 1000.0 / Stopwatch.Frequency;
            if (sinceSubmit < IdleCancelAfter.TotalMilliseconds * 0.9)
            {
                return;
            }

            var runningMs = (Stopwatch.GetTimestamp() - _runningStartTicks) * 1000.0 / Stopwatch.Frequency;
            if (_averageDurationMs > 0 && runningMs > _averageDurationMs * 0.7)
            {
                return;
            }

            _idleCancelled++;
            _runningCts?.Cancel();
        }
    }

    // 呼び出し側は _gate を持つ
    private bool ShouldApply(RenderRequest request)
    {
        if (_disposed || _latest is null)
        {
            return false;
        }

        if (!request.IsCompatibleWith(_latest) || request.Generation <= _lastDeliveredGeneration)
        {
            return false;
        }

        return request.Generation == _latest.Generation || request.Extent.Intersects(_latest.Extent);
    }
}
