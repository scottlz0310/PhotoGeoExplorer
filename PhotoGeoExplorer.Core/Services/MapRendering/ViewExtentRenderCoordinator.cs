namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 表示範囲画像の描画を、背景スレッドで 1 つずつ実行する。Mapsui にも WinUI にも依存しない。
/// <list type="bullet">
/// <item>未着手の要求は最新の 1 件で置き換える（待ち行列は持たない）。</item>
/// <item>実行中の描画は原則として完走させる。要求のたびにキャンセルすると、連続したパン・ズームの間は確定描画が
/// 一度も完了せず、直前の画像が表示範囲から外れて背景が白紙になるため。</item>
/// <item>実行中の描画をキャンセルするのは、結果が使えないと分かっているとき（描画元・スタイルの変更、最新の表示範囲と
/// 重ならなくなった）と破棄のときだけ。</item>
/// <item>描画は 1 つずつ逐次に走るので、結果は世代の昇順に届く。最新の要求と互換でない・範囲が重ならない古い結果は
/// 破棄し、古い画像への巻き戻りを防ぐ。</item>
/// </list>
/// <see cref="Submit"/> は任意のスレッドから呼べ、破棄後も例外を投げない。Mapsui はアニメーション中の視点変更を
/// UI スレッド以外で通知するため、通知ハンドラから呼べることが要件になる。
/// </summary>
internal sealed class ViewExtentRenderCoordinator<T> : IDisposable
    where T : class, IDisposable
{
    private readonly Func<ViewExtentRequest, CancellationToken, Task<T>> _renderAsync;
    private readonly Func<Action, bool> _tryPostToUi;
    private readonly Action<ViewExtentFrame<T>> _showFrame;
    private readonly Action<ViewExtentRequest, Exception> _renderFailed;
    private readonly object _gate = new();
    private Pending? _pending;
    private Pending? _latest;
    private ViewExtentRequest? _running;
    // 描画 1 回ごとに RunOneAsync が作り、その終了時に必ず破棄する。Dispose は実行中の描画へキャンセルを伝えるだけ
#pragma warning disable CA2213 // Disposable fields should be disposed
    private CancellationTokenSource? _runningCts;
#pragma warning restore CA2213
    private bool _loopActive;
    private bool _disposed;
    private bool _busy;
    private int _uiPending;
    private long _nextGeneration;

    /// <param name="renderAsync">1 枚描く。背景スレッドで呼ばれる。キャンセルされたら <see cref="OperationCanceledException"/> を投げてよい。</param>
    /// <param name="tryPostToUi">処理を UI スレッドへ渡す。渡せなかったら false。</param>
    /// <param name="showFrame">UI スレッドで呼ばれ、画像を表示へ反映する。受け取った参照は呼び出された側が解放する。</param>
    /// <param name="renderFailed">描画が失敗したとき（キャンセル以外）。背景スレッドで呼ばれる。失敗は隠さず、必ずここへ通知する。</param>
    public ViewExtentRenderCoordinator(
        Func<ViewExtentRequest, CancellationToken, Task<T>> renderAsync,
        Func<Action, bool> tryPostToUi,
        Action<ViewExtentFrame<T>> showFrame,
        Action<ViewExtentRequest, Exception> renderFailed)
    {
        ArgumentNullException.ThrowIfNull(renderAsync);
        ArgumentNullException.ThrowIfNull(tryPostToUi);
        ArgumentNullException.ThrowIfNull(showFrame);
        ArgumentNullException.ThrowIfNull(renderFailed);

        _renderAsync = renderAsync;
        _tryPostToUi = tryPostToUi;
        _showFrame = showFrame;
        _renderFailed = renderFailed;
    }

    /// <summary>描画中、未着手の要求がある、または描いた画像の反映待ちか。待機中は地図画像を保存できない。</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _busy;
            }
        }
    }

    /// <summary><see cref="IsBusy"/> が変化したとき。任意のスレッドから呼ばれる。</summary>
    public event EventHandler? BusyChanged;

    /// <summary>描画を要求する。破棄後は何もしない。</summary>
    public void Submit(ViewExtentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool busyChanged;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var pending = new Pending(request, ++_nextGeneration);
            _pending = pending;
            _latest = pending;

            if (_running is not null && ShouldCancelRunning(_running, request))
            {
                _runningCts?.Cancel();
            }

            if (!_loopActive)
            {
                _loopActive = true;
                _ = Task.Run(LoopAsync);
            }

            busyChanged = UpdateBusyLocked();
        }

        RaiseBusyChanged(busyChanged);
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
        }
    }

    /// <summary>実行中の描画をキャンセルするか。互換でない（描画元・スタイルの変更）か、範囲が重ならない場合。</summary>
    internal static bool ShouldCancelRunning(ViewExtentRequest running, ViewExtentRequest newest)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(newest);
        return !running.IsCompatibleWith(newest) || !running.Extent.Intersects(newest.Extent);
    }

    private async Task LoopAsync()
    {
        while (true)
        {
            Pending? current;
            CancellationTokenSource? cts = null;
            bool busyChanged = false;
            lock (_gate)
            {
                if (_disposed || _pending is null)
                {
                    current = null;
                    _loopActive = false;
                    busyChanged = UpdateBusyLocked();
                }
                else
                {
                    current = _pending;
                    _pending = null;
                    cts = new CancellationTokenSource();
                    _running = current.Request;
                    _runningCts = cts;
                }
            }

            if (current is null)
            {
                RaiseBusyChanged(busyChanged);
                return;
            }

            await RunOneAsync(current, cts!).ConfigureAwait(false);
        }
    }

    private async Task RunOneAsync(Pending current, CancellationTokenSource cts)
    {
        T? image = null;
        Exception? failure = null;
        try
        {
            image = await _renderAsync(current.Request, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // キャンセルは結果が使えないと分かって止めたもの。失敗ではない
        }
        // 描画の失敗は握りつぶさず、必ず renderFailed へ通知する。ここで止めないとループが死んで以後の描画が止まる
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031
        {
            failure = ex;
        }

        bool apply;
        bool reportFailure;
        lock (_gate)
        {
            _running = null;
            _runningCts = null;
            cts.Dispose();

            apply = image is not null && ShouldApplyLocked(current);
            if (apply)
            {
                _uiPending++;
            }

            reportFailure = failure is not null && !_disposed;
        }

        if (reportFailure)
        {
            _renderFailed(current.Request, failure!);
        }

        if (image is null)
        {
            return;
        }

        if (!apply)
        {
            image.Dispose();
            return;
        }

        var frame = new ViewExtentFrame<T>(current.Request, current.Generation, new SharedResource<T>(image));
        var posted = false;
        try
        {
            posted = _tryPostToUi(() => ShowOnUiThread(frame));
        }
        finally
        {
            if (!posted)
            {
                frame.Image.Release();
                FinishUiWork();
            }
        }
    }

    private void ShowOnUiThread(ViewExtentFrame<T> frame)
    {
        try
        {
            bool disposed;
            lock (_gate)
            {
                disposed = _disposed;
            }

            if (disposed)
            {
                frame.Image.Release();
                return;
            }

            try
            {
                _showFrame(frame);
            }
            catch
            {
                frame.Image.Release();
                throw;
            }
        }
        finally
        {
            FinishUiWork();
        }
    }

    private void FinishUiWork()
    {
        bool busyChanged;
        lock (_gate)
        {
            _uiPending--;
            busyChanged = UpdateBusyLocked();
        }

        RaiseBusyChanged(busyChanged);
    }

    // 呼び出し側は _gate を持つ
    private bool ShouldApplyLocked(Pending finished)
    {
        if (_disposed || _latest is null)
        {
            return false;
        }

        if (!finished.Request.IsCompatibleWith(_latest.Request))
        {
            return false;
        }

        return finished.Generation == _latest.Generation
            || finished.Request.Extent.Intersects(_latest.Request.Extent);
    }

    // 呼び出し側は _gate を持つ。変化したら true
    private bool UpdateBusyLocked()
    {
        var busy = _loopActive || _pending is not null || _uiPending > 0;
        if (busy == _busy)
        {
            return false;
        }

        _busy = busy;
        return true;
    }

    private void RaiseBusyChanged(bool changed)
    {
        if (changed)
        {
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record Pending(ViewExtentRequest Request, long Generation);
}
