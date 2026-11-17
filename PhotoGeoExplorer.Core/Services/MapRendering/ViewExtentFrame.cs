namespace PhotoGeoExplorer.Services.MapRendering;

/// <summary>
/// 描き終えた表示範囲画像。<paramref name="Image"/> の参照を 1 つ持った状態で受け取り、表示側が不要になったときに
/// <see cref="SharedResource{T}.Release"/> する。
/// </summary>
internal sealed record ViewExtentFrame<T>(ViewExtentRequest Request, long Generation, SharedResource<T> Image)
    where T : class, IDisposable;
