using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// DispatcherQueue ベースの <see cref="IUiDispatcher"/> 実装。
/// 生成したスレッド（UI スレッド上での DI 構築を想定）の DispatcherQueue を捕捉する。
/// DispatcherQueue が取得できない環境（単体テスト等）では同期実行にフォールバックする。
/// </summary>
internal sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue? _dispatcherQueue;

    public UiDispatcher()
    {
        _dispatcherQueue = TryGetDispatcherQueue();
    }

    public bool IsAvailable => _dispatcherQueue is not null;

    public Task RunAsync(Action action)
    {
        if (_dispatcherQueue is null)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    throw;
                }
            }))
        {
            var ex = new InvalidOperationException("DispatcherQueue へのエンキューに失敗しました。");
            AppLog.Error("UiDispatcher.RunAsync: DispatcherQueue.TryEnqueue が false を返しました。", ex);
            tcs.SetException(ex);
        }

        return tcs.Task;
    }

    public Task<T> EnqueueAsync<T>(Func<Task<T>> asyncFunc)
    {
        if (_dispatcherQueue is null)
        {
            return asyncFunc();
        }

        var tcs = new TaskCompletionSource<T>();
        if (!_dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var result = await asyncFunc().ConfigureAwait(false);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    throw;
                }
            }))
        {
            var ex = new InvalidOperationException("DispatcherQueue へのエンキューに失敗しました。");
            AppLog.Error("UiDispatcher.EnqueueAsync: DispatcherQueue.TryEnqueue が false を返しました。", ex);
            tcs.SetException(ex);
        }

        return tcs.Task;
    }

    public bool TryEnqueue(Action action)
    {
        return _dispatcherQueue?.TryEnqueue(() => action()) ?? false;
    }

    public IUiDispatcherTimer? CreateTimer()
    {
        return _dispatcherQueue is null
            ? null
            : new DispatcherQueueTimerAdapter(_dispatcherQueue.CreateTimer());
    }

    private static DispatcherQueue? TryGetDispatcherQueue()
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
        }
        catch (COMException ex)
        {
            AppLog.Info($"DispatcherQueue is unavailable in this environment: {ex.Message}");
            return null;
        }
        catch (TypeInitializationException ex)
        {
            AppLog.Info($"DispatcherQueue initialization failed: {ex.Message}");
            return null;
        }
    }

    private sealed class DispatcherQueueTimerAdapter : IUiDispatcherTimer
    {
        private readonly DispatcherQueueTimer _timer;

        public DispatcherQueueTimerAdapter(DispatcherQueueTimer timer)
        {
            _timer = timer;
            _timer.Tick += OnTimerTick;
        }

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public event EventHandler? Tick;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        private void OnTimerTick(DispatcherQueueTimer sender, object args)
            => Tick?.Invoke(this, EventArgs.Empty);
    }
}
