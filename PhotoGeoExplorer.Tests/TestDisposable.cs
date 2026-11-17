using System;
using System.Threading;

namespace PhotoGeoExplorer.Tests;

/// <summary>破棄の回数を数える、テスト用の破棄可能な値。</summary>
internal sealed class TestDisposable : IDisposable
{
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool IsDisposed => DisposeCount > 0;

    public void Dispose() => Interlocked.Increment(ref _disposeCount);
}
