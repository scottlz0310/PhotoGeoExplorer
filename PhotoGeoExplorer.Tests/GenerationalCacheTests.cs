using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.AreaTree;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class GenerationalCacheTests
{
    [Fact]
    public void StoreKeepsTheValueWhenNothingWasInvalidatedSinceTheLookup()
    {
        var cache = new GenerationalCache<string, int>(10);
        Assert.False(cache.TryGet("a", out _, out var generation));

        Assert.True(cache.Store(generation, "a", 1));

        Assert.True(cache.TryGet("a", out var value, out _));
        Assert.Equal(1, value);
    }

    [Fact]
    public void StoreDropsAValueWhenTheCacheWasInvalidatedAfterTheLookup()
    {
        var cache = new GenerationalCache<string, int>(10);
        Assert.False(cache.TryGet("a", out _, out var generation));

        // 値を求めている間に無効化された（確認の後・保存の前の競合）
        cache.Invalidate();

        Assert.False(cache.Store(generation, "a", 1));
        Assert.False(cache.TryGet("a", out _, out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void InvalidateDiscardsWhatWasStoredAndLaterStoresWithTheNewGenerationSucceed()
    {
        var cache = new GenerationalCache<string, int>(10);
        cache.TryGet("a", out _, out var before);
        cache.Store(before, "a", 1);

        cache.Invalidate();
        Assert.False(cache.TryGet("a", out _, out var after));
        Assert.True(cache.Store(after, "a", 2));

        Assert.True(cache.TryGet("a", out var value, out _));
        Assert.Equal(2, value);
    }

    [Fact]
    public void StoreDiscardsEverythingWhenTheLimitIsReached()
    {
        var cache = new GenerationalCache<int, int>(3);
        for (var i = 0; i < 3; i++)
        {
            cache.TryGet(i, out _, out var generation);
            cache.Store(generation, i, i);
        }

        Assert.Equal(3, cache.Count);
        cache.TryGet(3, out _, out var latest);
        cache.Store(latest, 3, 3);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(3, out _, out _));
    }

    [Fact]
    public async Task ARaceBetweenStoresAndInvalidationsNeverLeavesAValueOfAnOldGeneration()
    {
        var cache = new GenerationalCache<int, int>(100_000);
        var version = 0;
        var stop = false;

        // 値は「求め始めたときのバージョン」。無効化の後に、古いバージョンの値が残ってはならない
        var writers = new Task[4];
        for (var w = 0; w < writers.Length; w++)
        {
            writers[w] = Task.Run(() =>
            {
                var key = 0;
                while (!Volatile.Read(ref stop))
                {
                    var k = key++ % 50;
                    if (!cache.TryGet(k, out _, out var generation))
                    {
                        var seen = Volatile.Read(ref version);
                        cache.Store(generation, k, seen);
                    }
                }
            });
        }

        for (var i = 0; i < 300; i++)
        {
            // バージョンを進めてから無効化する。無効化の後に残る値のバージョンは、必ず新しいものになる
            Interlocked.Increment(ref version);
            var current = Volatile.Read(ref version);
            cache.Invalidate();
            for (var k = 0; k < 50; k++)
            {
                if (cache.TryGet(k, out var value, out _))
                {
                    Assert.True(value >= current, $"無効化の後に、古い値（{value} < {current}）が残っています。");
                }
            }

            await Task.Yield();
        }

        Volatile.Write(ref stop, true);
        await Task.WhenAll(writers).ConfigureAwait(true);
    }

    [Fact]
    public void ConstructorRejectsANonPositiveLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationalCache<int, int>(0));
    }
}
