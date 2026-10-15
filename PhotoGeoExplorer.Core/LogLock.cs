using System;
using System.Threading;

namespace PhotoGeoExplorer;

/// <summary>ログの書き込みと世代の繰り下げを、プロセスをまたいで直列化する。</summary>
internal interface ILogLock
{
    IDisposable Acquire();
}

internal sealed class NullLogLock : ILogLock
{
    public static NullLogLock Instance { get; } = new();

    public IDisposable Acquire()
    {
        return NullReleaser.Instance;
    }

    private sealed class NullReleaser : IDisposable
    {
        public static NullReleaser Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// 名前付き Mutex による排他。単一インスタンスの転送元プロセスと本体が同じ <c>app.log</c> に同時に書き込んでも、
/// 共有違反によるログの欠損や、繰り下げ中の競合が起きないようにする。
/// 取得できない場合（待機の超過）はログを失わないことを優先し、排他なしで続行する。
/// </summary>
internal sealed class NamedMutexLogLock : ILogLock, IDisposable
{
    private readonly Mutex _mutex;
    private readonly TimeSpan _timeout;

    public NamedMutexLogLock(string name, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _mutex = new Mutex(initiallyOwned: false, name);
        _timeout = timeout;
    }

    public IDisposable Acquire()
    {
        var acquired = false;
        try
        {
            acquired = _mutex.WaitOne(_timeout);
        }
        catch (AbandonedMutexException)
        {
            // 前の所有者が解放せずに終了した。所有権は取得できているため、そのまま続行する。
            acquired = true;
        }

        return new Releaser(_mutex, acquired);
    }

    public void Dispose()
    {
        _mutex.Dispose();
    }

    private sealed class Releaser : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly bool _acquired;

        public Releaser(Mutex mutex, bool acquired)
        {
            _mutex = mutex;
            _acquired = acquired;
        }

        public void Dispose()
        {
            if (_acquired)
            {
                _mutex.ReleaseMutex();
            }
        }
    }
}
