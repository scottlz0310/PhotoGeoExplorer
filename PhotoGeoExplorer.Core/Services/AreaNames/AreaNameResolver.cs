using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 国勢調査 小地域のポリゴンへの内包判定で、座標から字地名を求める
/// </summary>
internal sealed class AreaNameResolver : IAreaNameResolver
{
    internal const int DefaultCacheCapacity = 4;

    private readonly IPrefectureLocator _prefectureLocator;
    private readonly IAreaDataSource _dataSource;
    private readonly int _cacheCapacity;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, Task<AreaDataset?>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recentlyUsed = new();

    public AreaNameResolver(
        IPrefectureLocator prefectureLocator,
        IAreaDataSource dataSource,
        int cacheCapacity = DefaultCacheCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheCapacity, 1);

        _prefectureLocator = prefectureLocator ?? throw new ArgumentNullException(nameof(prefectureLocator));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _cacheCapacity = cacheCapacity;
    }

    public async Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var candidates = _prefectureLocator.FindCandidates(latitude, longitude);
        AreaMatch? best = null;
        foreach (var prefectureCode in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataset = await GetDatasetAsync(prefectureCode, cancellationToken).ConfigureAwait(false);
            var match = dataset?.Find(latitude, longitude);
            if (match is null)
            {
                continue;
            }

            if (!match.Name.SnappedToNearest)
            {
                return match.Name;
            }

            if (best is null || match.DistanceMeters < best.DistanceMeters)
            {
                best = match;
            }
        }

        return best?.Name;
    }

    // 読み込みは Task のまま保持して、同じ都道府県への同時要求で二重に読まない。
    // 導入されていない（null）・失敗した読み込みは保持しない（後から導入されたデータを読めるようにするため）
    private async Task<AreaDataset?> GetDatasetAsync(string prefectureCode, CancellationToken cancellationToken)
    {
        Task<AreaDataset?> loading;
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(prefectureCode, out var cached))
            {
                _recentlyUsed.Remove(prefectureCode);
                loading = cached;
            }
            else
            {
                // 呼び出し元のキャンセルで、他の呼び出しと共有する読み込みを止めない
                loading = _dataSource.LoadAsync(prefectureCode, CancellationToken.None);
                _cache[prefectureCode] = loading;
            }

            _recentlyUsed.AddFirst(prefectureCode);
            while (_recentlyUsed.Count > _cacheCapacity)
            {
                _cache.Remove(_recentlyUsed.Last!.Value);
                _recentlyUsed.RemoveLast();
            }
        }

        AreaDataset? dataset;
        try
        {
            dataset = await loading.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            Forget(prefectureCode, loading);
            throw;
        }

        if (dataset is null)
        {
            Forget(prefectureCode, loading);
        }

        return dataset;
    }

    private void Forget(string prefectureCode, Task<AreaDataset?> loading)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(prefectureCode, out var cached) && ReferenceEquals(cached, loading))
            {
                _cache.Remove(prefectureCode);
                _recentlyUsed.Remove(prefectureCode);
            }
        }
    }
}
