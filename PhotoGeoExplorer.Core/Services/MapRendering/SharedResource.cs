using System.Diagnostics.CodeAnalysis;

namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 参照カウントで所有権を管理する破棄可能な値。最後の参照が離れたときに <see cref="IDisposable.Dispose"/> する。
/// Mapsui の描画や PNG 保存は UI スレッド以外で走りうるため、画像の交換直後に無条件で破棄すると、描画中の画像を壊す。
/// 描く側は <see cref="TryAcquire"/> で描画の間だけ参照を取り、交換した側は自分の参照を <see cref="Release"/> する。
/// </summary>
internal sealed class SharedResource<T>
    where T : class, IDisposable
{
    private readonly object _gate = new();
    private T? _value;
    private int _count = 1;

    /// <summary>参照 1 つ（呼び出し側のもの）を持った状態で作る。</summary>
    public SharedResource(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _value is null;
            }
        }
    }

    /// <summary>現在の参照数。破棄済みなら 0。</summary>
    public int ReferenceCount
    {
        get
        {
            lock (_gate)
            {
                return _value is null ? 0 : _count;
            }
        }
    }

    /// <summary>
    /// 参照を 1 つ取る。破棄済みなら false（交換の直後に別スレッドが古い画像を描こうとした場合。描かずに済ませる）。
    /// 返したリースを <see cref="Lease.Dispose"/> で返す。
    /// </summary>
    public bool TryAcquire([NotNullWhen(true)] out Lease? lease)
    {
        lock (_gate)
        {
            if (_value is null)
            {
                lease = null;
                return false;
            }

            _count++;
            lease = new Lease(this, _value);
            return true;
        }
    }

    /// <summary>自分が持つ参照を 1 つ手放す。最後の 1 つで値を破棄する。</summary>
    public void Release()
    {
        T? toDispose = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_value is null, this);
            if (--_count == 0)
            {
                toDispose = _value;
                _value = null;
            }
        }

        toDispose?.Dispose();
    }

    /// <summary>借りた参照。<see cref="Value"/> は <see cref="Dispose"/> するまで有効。</summary>
    internal sealed class Lease : IDisposable
    {
        private SharedResource<T>? _owner;

        internal Lease(SharedResource<T> owner, T value)
        {
            _owner = owner;
            Value = value;
        }

        public T Value { get; }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release();
        }
    }
}
