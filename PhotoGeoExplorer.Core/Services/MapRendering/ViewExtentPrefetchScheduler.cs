namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 操作が止まった（視点の通知が一定時間途絶えた）ときに、表示範囲の中心付近のタイルを先に取得・デコードする。
/// ズームアウトでこれまで見ていない隣のタイルが必要になったとき、描画の中でデコードする（密集市街地で 1 枚 30 ms 以上）
/// のを避けるための最適化で、失敗しても描画の中で取得・デコードされるため、結果には影響しない。
/// Mapsui にも描画エンジンにも依存しない。
/// <list type="bullet">
/// <item>描画が実行中の間は始めない（描画と CPU を取り合うため）。描画が終わるまで、待ち時間ごとに確認し直す。</item>
/// <item>直前に済ませたタイルと同じ（取得ズームと中心のタイルが同じ）なら、繰り返さない。</item>
/// <item>新しい視点が来たら、実行中の事前デコードを取り消す（その視点の先読みは、もう古い）。</item>
/// </list>
/// <see cref="Notify"/> は任意のスレッドから呼べ、破棄後は何もしない。Mapsui はアニメーション中の視点変更を
/// UI スレッド以外で通知するため、通知ハンドラから呼べることが要件になる。
/// </summary>
internal sealed class ViewExtentPrefetchScheduler : IDisposable
{
    /// <summary>操作が止まったと見なすまでの待ち時間の既定（設計合意 #295。PoC で効果を確認した値）。</summary>
    internal static readonly TimeSpan DefaultIdleDelay = TimeSpan.FromMilliseconds(120);

    private readonly Func<ViewExtentRequest, CancellationToken, Task> _prefetchAsync;
    private readonly Func<bool> _isBusy;
    private readonly Action<ViewExtentRequest, Exception> _prefetchFailed;
    private readonly TimeSpan _idleDelay;
    private readonly object _gate = new();
    private readonly ITimer _timer;
    private ViewExtentRequest? _latest;
    private (int Zoom, long X, long Y)? _lastDone;
    // 実行 1 回ごとに OnTimer が作り、その実行の終了時（RunAsync の Finish）に必ず破棄する。Dispose は取り消しを伝えるだけ
#pragma warning disable CA2213 // Disposable fields should be disposed
    private CancellationTokenSource? _running;
#pragma warning restore CA2213
    private bool _disposed;

    /// <param name="prefetchAsync">指定した視点の中心付近を先に取得・デコードする。キャンセルされたら <see cref="OperationCanceledException"/> を投げてよい。</param>
    /// <param name="isBusy">描画が実行中か。実行中は事前デコードを始めない。</param>
    /// <param name="prefetchFailed">事前デコードが失敗したとき（キャンセル以外）。失敗は隠さず、必ずここへ通知する。</param>
    /// <param name="idleDelay">視点の通知が途絶えてから、事前デコードを始めるまでの待ち時間。</param>
    /// <param name="timeProvider">待ち時間の計測に使う時計。テストで差し替える（<see langword="null"/> なら既定）。</param>
    public ViewExtentPrefetchScheduler(
        Func<ViewExtentRequest, CancellationToken, Task> prefetchAsync,
        Func<bool> isBusy,
        Action<ViewExtentRequest, Exception> prefetchFailed,
        TimeSpan idleDelay,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(prefetchAsync);
        ArgumentNullException.ThrowIfNull(isBusy);
        ArgumentNullException.ThrowIfNull(prefetchFailed);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleDelay, TimeSpan.Zero);

        _prefetchAsync = prefetchAsync;
        _isBusy = isBusy;
        _prefetchFailed = prefetchFailed;
        _idleDelay = idleDelay;
        _timer = (timeProvider ?? TimeProvider.System).CreateTimer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>視点が変わったことを伝える。待ち時間の計測をやり直し、実行中の事前デコードを取り消す。</summary>
    public void Notify(ViewExtentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _latest = request;
            _running?.Cancel();
            _timer.Change(_idleDelay, Timeout.InfiniteTimeSpan);
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
            _running?.Cancel();
            _timer.Dispose();
        }
    }

    /// <summary>事前デコードの単位。取得ズームと、視点の中心が属するタイル（連続ズームの切り捨てで求める）。</summary>
    internal static (int Zoom, long X, long Y) GetTileKey(ViewExtentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var zoom = Math.Max(0, (int)Math.Floor(WebMercator.ZoomFromResolution(request.Resolution, request.PixelDensity)));
        var tiles = Math.Pow(2, zoom);
        var circumference = 2 * Math.PI * WebMercator.EarthRadius;
        var x = (long)Math.Floor((request.CenterX + (circumference / 2)) / circumference * tiles);
        var y = (long)Math.Floor(((circumference / 2) - request.CenterY) / circumference * tiles);
        return (zoom, x, y);
    }

    private void OnTimer(object? state)
    {
        ViewExtentRequest request;
        CancellationTokenSource running;
        lock (_gate)
        {
            if (_disposed || _latest is not { } latest)
            {
                return;
            }

            if (_isBusy())
            {
                // 描画が終わっても、視点の通知はもう来ない。終わるまで確認し直す（新しい通知か破棄で止まる）
                _timer.Change(_idleDelay, Timeout.InfiniteTimeSpan);
                return;
            }

            if (GetTileKey(latest) == _lastDone)
            {
                return;
            }

            request = latest;
            running = new CancellationTokenSource();
            _running = running;
        }

        _ = RunAsync(request, running);
    }

    // running は、この実行が終わるときにここで解放する（_running から外してから解放するので、Notify・Dispose の Cancel と競合しない）
    private async Task RunAsync(ViewExtentRequest request, CancellationTokenSource running)
    {
        var finished = false;
        try
        {
            await _prefetchAsync(request, running.Token).ConfigureAwait(false);
            finished = true;
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
            // 新しい視点か破棄で取り消された。済んだことにせず、次の機会にやり直す
        }
        // 失敗は握りつぶさず、必ず prefetchFailed へ通知する。ここで止めると、未観測の例外としてタスクの終了処理に落ちる
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // 同じタイルで失敗し続けて記録があふれないよう、タイルが変わるまで再試行しない
            finished = true;
            _prefetchFailed(request, exception);
        }
        finally
        {
            Finish(request, running, finished);
        }
    }

    private void Finish(ViewExtentRequest request, CancellationTokenSource running, bool finished)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_running, running))
            {
                _running = null;
                if (finished && !_disposed)
                {
                    _lastDone = GetTileKey(request);
                }
            }
        }

        running.Dispose();
    }
}
