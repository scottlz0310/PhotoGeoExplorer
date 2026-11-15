using SkiaSharp;

namespace PhotoMapStudio.Spike;

/// <summary>
/// 描いた画像の所有権。参照カウントで管理し、最後の参照が離れたときに解放する。
/// 描画中・保存中の画像を、置き換えのタイミングで解放しないための契約（#293 の「SKImage は参照カウントで管理」）。
/// </summary>
public sealed class RefCountedImage
{
    private readonly object _gate = new();
    private SKImage? _image;
    private int _count = 1;
    private static int s_live;

    public RefCountedImage(SKImage image)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        Interlocked.Increment(ref s_live);
    }

    /// <summary>解放されていない画像の数（リークの確認用）。</summary>
    public static int LiveCount => Volatile.Read(ref s_live);

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _image is null;
            }
        }
    }

    public int ReferenceCount
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>参照を 1 つ増やす。解放済みなら例外。</summary>
    public RefCountedImage Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_image is null, this);
            _count++;
            return this;
        }
    }

    /// <summary>取得した参照を、1 つ手放す。最後の 1 つで画像を解放する。</summary>
    public void Release()
    {
        SKImage? toDispose = null;
        lock (_gate)
        {
            if (_image is null)
            {
                throw new ObjectDisposedException(nameof(RefCountedImage));
            }

            if (--_count == 0)
            {
                toDispose = _image;
                _image = null;
            }
        }

        if (toDispose is not null)
        {
            toDispose.Dispose();
            Interlocked.Decrement(ref s_live);
        }
    }

    /// <summary>画像を借りる（参照は増やさない。呼び出し側が参照を持っている間だけ使う）。</summary>
    public SKImage Image
    {
        get
        {
            lock (_gate)
            {
                return _image ?? throw new ObjectDisposedException(nameof(RefCountedImage));
            }
        }
    }
}
