using System.Collections.Concurrent;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.AreaTree;

/// <summary>
/// フォルダー内の写真の位置情報を読み、字地名を判定する。数千枚でも画面を止めないよう、背景で並列に処理し、
/// 途中経過を通知する。位置情報の読み取り結果（写真ごと）と判定結果（座標ごと）は保持して使い回す
/// </summary>
internal sealed class PhotoAreaClassifier
{
    internal const int DefaultMaxDegreeOfParallelism = 4;
    internal const int DefaultProgressBatchSize = 50;

    // 保持が際限なく増えないよう、超えたら捨てて作り直す
    private const int MaxCachedEntries = 100_000;

    private readonly Func<string, CancellationToken, Task<PhotoCoordinate?>> _readCoordinate;
    private readonly IAreaNameResolver _resolver;
    private readonly int _maxDegreeOfParallelism;
    private readonly int _progressBatchSize;
    private readonly GenerationalCache<(string FilePath, string Version), PhotoCoordinate?> _coordinates = new(MaxCachedEntries);

    // 地域データの変更より前に求め始めた字地名は、変更の後に保存されないようにする（世代の確認と保存・更新と破棄は同じ錠の内側）
    private readonly GenerationalCache<PhotoCoordinate, AreaName?> _areas = new(MaxCachedEntries);

    /// <param name="readCoordinate">写真の位置情報を読む。位置情報がない、または読めなければ null</param>
    /// <param name="resolver">座標から字地名を判定する</param>
    public PhotoAreaClassifier(
        Func<string, CancellationToken, Task<PhotoCoordinate?>> readCoordinate,
        IAreaNameResolver resolver,
        int maxDegreeOfParallelism = DefaultMaxDegreeOfParallelism,
        int progressBatchSize = DefaultProgressBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(progressBatchSize, 1);

        _readCoordinate = readCoordinate ?? throw new ArgumentNullException(nameof(readCoordinate));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        _progressBatchSize = progressBatchSize;
        _resolver.ResultsInvalidated += OnResultsInvalidated;
    }

    /// <summary>
    /// 写真の字地名を判定する。結果は <paramref name="progress"/> に、一定の枚数ごとと最後に通知する
    /// </summary>
    /// <exception cref="OperationCanceledException">キャンセルされた</exception>
    /// <exception cref="IOException">地域データを読めなかった</exception>
    public async Task ClassifyAsync(
        IReadOnlyList<PhotoRef> photos,
        IProgress<AreaClassificationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);

        var total = photos.Count;
        var pending = new ConcurrentQueue<PhotoAreaEntry>();
        var completed = 0;
        var reportLock = new object();

        void Report(bool force)
        {
            if (progress is null)
            {
                return;
            }

            // 通知は 1 つずつ順に行い、完了数と結果が食い違わないようにする
            lock (reportLock)
            {
                var batch = new List<PhotoAreaEntry>();
                while (pending.TryDequeue(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count > 0 || force)
                {
                    progress.Report(new AreaClassificationProgress(Volatile.Read(ref completed), total, batch));
                }
            }
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };
        await Parallel.ForEachAsync(
            photos,
            options,
            async (photo, token) =>
            {
                pending.Enqueue(await ClassifyOneAsync(photo, token).ConfigureAwait(false));
                if (Interlocked.Increment(ref completed) % _progressBatchSize == 0)
                {
                    Report(force: false);
                }
            }).ConfigureAwait(false);

        Report(force: true);
    }

    private async Task<PhotoAreaEntry> ClassifyOneAsync(PhotoRef photo, CancellationToken cancellationToken)
    {
        if (photo.HasLocation == false)
        {
            return new PhotoAreaEntry(photo.FilePath, PhotoAreaKind.NoLocation, null);
        }

        var coordinate = await GetCoordinateAsync(photo, cancellationToken).ConfigureAwait(false);
        if (coordinate is not { } location)
        {
            return new PhotoAreaEntry(photo.FilePath, PhotoAreaKind.NoLocation, null);
        }

        var area = await GetAreaAsync(location, cancellationToken).ConfigureAwait(false);
        return area is null
            ? new PhotoAreaEntry(photo.FilePath, PhotoAreaKind.NoAreaData, null)
            : new PhotoAreaEntry(photo.FilePath, PhotoAreaKind.Resolved, area);
    }

    private async Task<PhotoCoordinate?> GetCoordinateAsync(PhotoRef photo, CancellationToken cancellationToken)
    {
        var key = (photo.FilePath, photo.Version);
        if (_coordinates.TryGet(key, out var cached, out var generation))
        {
            return cached;
        }

        var coordinate = await _readCoordinate(photo.FilePath, cancellationToken).ConfigureAwait(false);
        _coordinates.Store(generation, key, coordinate);
        return coordinate;
    }

    private async Task<AreaName?> GetAreaAsync(PhotoCoordinate coordinate, CancellationToken cancellationToken)
    {
        if (_areas.TryGet(coordinate, out var cached, out var generation))
        {
            return cached;
        }

        var area = await _resolver.ResolveAsync(coordinate.Latitude, coordinate.Longitude, cancellationToken).ConfigureAwait(false);
        _areas.Store(generation, coordinate, area);
        return area;
    }

    private void OnResultsInvalidated(object? sender, EventArgs e) => _areas.Invalidate();
}
