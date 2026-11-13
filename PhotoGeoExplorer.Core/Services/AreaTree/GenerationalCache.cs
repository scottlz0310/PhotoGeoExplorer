namespace PhotoGeoExplorer.Services.AreaTree;

/// <summary>
/// 無効化できる保持。非同期に求める値を保持するとき、求め始めた後に無効化されていたら、その値を保存しない。
/// 世代の確認と保存、世代の更新と破棄は、同じ錠の内側で行う（確認の後・保存の前に無効化が挟まると、古い値が残るため）
/// </summary>
internal sealed class GenerationalCache<TKey, TValue>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, TValue> _items = [];
    private readonly int _maxEntries;
    private int _generation;

    /// <param name="maxEntries">保持する件数の上限。超えたら全部捨てて作り直す</param>
    public GenerationalCache(int maxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _maxEntries = maxEntries;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// 値を探す。見つからなければ、そのときの世代を返す（値を求めた後、この世代のまま保存できるかを <see cref="Store"/> が確かめる）
    /// </summary>
    public bool TryGet(TKey key, out TValue? value, out int generation)
    {
        lock (_gate)
        {
            generation = _generation;
            return _items.TryGetValue(key, out value);
        }
    }

    /// <summary>
    /// <paramref name="generation"/> の後に無効化されていなければ、値を保存する
    /// </summary>
    /// <returns>保存したか</returns>
    public bool Store(int generation, TKey key, TValue value)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return false;
            }

            if (_items.Count >= _maxEntries)
            {
                _items.Clear();
            }

            _items[key] = value;
            return true;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _items.Clear();
        }
    }
}
