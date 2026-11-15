using PhotoMapStudio.Tiles.Vector;

namespace PhotoMapStudio.Spike;

public readonly record struct TileKey(string SourceId, int Z, int X, int Y);

/// <summary>デコード済みの MVT モデルの LRU キャッシュ。大きさは、デコード時の割り当てバイト数で見積もる（上限は見積もりの合計）。</summary>
public sealed class DecodedTileCache
{
    private readonly object _gate = new();
    private readonly Dictionary<TileKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();
    private readonly long _capacityBytes;
    private long _bytes;
    private long _hits;
    private long _misses;
    private long _evictions;

    public DecodedTileCache(long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        _capacityBytes = capacityBytes;
    }

    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public (long Hits, long Misses, long Evictions) Stats
    {
        get
        {
            lock (_gate)
            {
                return (_hits, _misses, _evictions);
            }
        }
    }

    /// <summary>見つかれば true（欠落タイルを覚えている場合、tile は null）。見つかった項目は最近使ったものへ移す。</summary>
    public bool TryGet(TileKey key, out VectorTile? tile)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                _hits++;
                tile = node.Value.Tile;
                return true;
            }

            _misses++;
            tile = null;
            return false;
        }
    }

    /// <summary>追加して、上限を超えた分を古い順に捨てる。tile が null なら、欠落（または失敗）したタイルを覚える。</summary>
    public void Add(TileKey key, VectorTile? tile, long estimatedBytes)
    {
        var size = Math.Max(estimatedBytes, 64);
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _bytes -= existing.Value.Bytes;
                _lru.Remove(existing);
                _map.Remove(key);
            }

            var node = new LinkedListNode<Entry>(new Entry(key, tile, size));
            _lru.AddFirst(node);
            _map[key] = node;
            _bytes += size;

            // 追加した 1 件は、上限を超えていても残す（1 枚の描画に要る分まで捨てない）
            while (_bytes > _capacityBytes && _lru.Count > 1)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _bytes -= last.Value.Bytes;
                _evictions++;
            }
        }
    }

    private sealed record Entry(TileKey Key, VectorTile? Tile, long Bytes);
}
