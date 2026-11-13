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

    // 読み込み済みのデータ（LRU）と、進行中の読み込みは分けて持つ。
    // 進行中の読み込みを LRU の追い出しの対象にすると、同じ都道府県を二重に読むことになる
    private readonly Dictionary<string, AreaDataset> _loaded = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recentlyUsed = new();
    private readonly Dictionary<string, Task<AreaDataset?>> _loading = new(StringComparer.Ordinal);

    // データの変更で破棄した後に、変更前から続いていた読み込みの結果を保存しないための世代
    private int _generation;

    public AreaNameResolver(
        IPrefectureLocator prefectureLocator,
        IAreaDataSource dataSource,
        int cacheCapacity = DefaultCacheCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheCapacity, 1);

        _prefectureLocator = prefectureLocator ?? throw new ArgumentNullException(nameof(prefectureLocator));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _cacheCapacity = cacheCapacity;
        _dataSource.DataChanged += OnDataChanged;
    }

    public event EventHandler? ResultsInvalidated;

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

            // 陸の町丁字への内包は、他の都道府県の結果で覆らない
            if (!match.IsWater && !match.Name.SnappedToNearest)
            {
                return match.Name;
            }

            if (best is null || IsBetter(match, best))
            {
                best = match;
            }
        }

        return best?.Name;
    }

    // 候補の都道府県をまたいでも、AreaDataset.Find と同じ優先順位にする:
    // 30m 以内の陸（近い順）→ 内包する水面 → 30m 以内の水面（近い順）
    private static bool IsBetter(AreaMatch candidate, AreaMatch current)
        => candidate.IsWater != current.IsWater
            ? !candidate.IsWater
            : candidate.DistanceMeters < current.DistanceMeters;

    private async Task<AreaDataset?> GetDatasetAsync(string prefectureCode, CancellationToken cancellationToken)
    {
        Task<AreaDataset?> loading;
        lock (_cacheLock)
        {
            if (_loaded.TryGetValue(prefectureCode, out var loaded))
            {
                _recentlyUsed.Remove(prefectureCode);
                _recentlyUsed.AddFirst(prefectureCode);
                return loaded;
            }

            if (!_loading.TryGetValue(prefectureCode, out loading!))
            {
                loading = LoadAndStoreAsync(prefectureCode, _generation);

                // 同期的に完了した読み込みは、この時点で結果の保存まで済んでいる
                if (!loading.IsCompleted)
                {
                    _loading[prefectureCode] = loading;
                }
            }
        }

        return await loading.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // 呼び出し元のキャンセルでは止めない（他の呼び出しと共有するため）。
    // 導入されていない（null）・失敗した読み込みは保持しない（後から導入されたデータを読めるようにするため）
    private async Task<AreaDataset?> LoadAndStoreAsync(string prefectureCode, int generation)
    {
        AreaDataset? dataset = null;
        try
        {
            dataset = await _dataSource.LoadAsync(prefectureCode, CancellationToken.None).ConfigureAwait(false);
            return dataset;
        }
        finally
        {
            lock (_cacheLock)
            {
                // 破棄済みなら何も保存しない。_loading には、破棄後に始まった同じ県の読み込みが入っていることがある
                if (generation == _generation)
                {
                    _loading.Remove(prefectureCode);
                }

                if (generation == _generation && dataset is not null)
                {
                    _loaded[prefectureCode] = dataset;
                    _recentlyUsed.AddFirst(prefectureCode);
                    while (_recentlyUsed.Count > _cacheCapacity)
                    {
                        _loaded.Remove(_recentlyUsed.Last!.Value);
                        _recentlyUsed.RemoveLast();
                    }
                }
            }
        }
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        lock (_cacheLock)
        {
            _generation++;
            _loaded.Clear();
            _recentlyUsed.Clear();
            _loading.Clear();
        }

        ResultsInvalidated?.Invoke(this, EventArgs.Empty);
    }
}
