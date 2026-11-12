using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Tests;

// WinUI の DispatcherQueue と同様に、単一スレッドで Post されたコールバックを順に実行する
internal sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;

    public SingleThreadSynchronizationContext()
    {
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
        ThreadId = _thread.ManagedThreadId;
    }

    public int ThreadId { get; }

    public override void Post(SendOrPostCallback d, object? state)
        => _queue.Add((d, state));

    public Task RunAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(
            _ =>
            {
                action().ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                        {
                            completion.TrySetException(task.Exception!.InnerExceptions);
                        }
                        else if (task.IsCanceled)
                        {
                            completion.TrySetCanceled();
                        }
                        else
                        {
                            completion.TrySetResult();
                        }
                    },
                    TaskScheduler.Default);
            },
            null);
        return completion.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
        _queue.Dispose();
    }

    private void Run()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
        {
            callback(state);
        }
    }
}
