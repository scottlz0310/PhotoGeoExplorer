// 生成した値の所有権は SharedResource へ渡し、その Release で破棄される（テスト対象の挙動）
#pragma warning disable CA2000
using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.MapRendering;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class SharedResourceTests
{
    [Fact]
    public void ReleasingTheOnlyReferenceDisposesTheValue()
    {
        var value = new TestDisposable();
        var resource = new SharedResource<TestDisposable>(value);

        Assert.Equal(1, resource.ReferenceCount);
        resource.Release();

        Assert.True(value.IsDisposed);
        Assert.True(resource.IsDisposed);
        Assert.Equal(0, resource.ReferenceCount);
    }

    [Fact]
    public void ValueSurvivesUntilEveryLeaseIsReturned()
    {
        var value = new TestDisposable();
        var resource = new SharedResource<TestDisposable>(value);
        Assert.True(resource.TryAcquire(out var lease));

        // 描画中に画像が交換され、所有側が自分の参照を手放した
        resource.Release();

        Assert.False(value.IsDisposed);
        Assert.Same(value, lease.Value);

        lease.Dispose();

        Assert.True(value.IsDisposed);
    }

    [Fact]
    public void TryAcquireFailsAfterDisposalInsteadOfThrowing()
    {
        var resource = new SharedResource<TestDisposable>(new TestDisposable());
        resource.Release();

        Assert.False(resource.TryAcquire(out var lease));
        Assert.Null(lease);
    }

    [Fact]
    public void DisposingALeaseTwiceReturnsOnlyOneReference()
    {
        var value = new TestDisposable();
        var resource = new SharedResource<TestDisposable>(value);
        Assert.True(resource.TryAcquire(out var lease));

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, resource.ReferenceCount);
        Assert.False(value.IsDisposed);
    }

    [Fact]
    public void ReleasingMoreThanAcquiredThrows()
    {
        var resource = new SharedResource<TestDisposable>(new TestDisposable());
        resource.Release();

        Assert.Throws<ObjectDisposedException>(resource.Release);
    }

    [Fact]
    public void RejectsNullValue()
    {
        Assert.Throws<ArgumentNullException>(() => new SharedResource<TestDisposable>(null!));
    }

    [Fact]
    public async Task ConcurrentAcquireAndReleaseDisposesExactlyOnceAndNeverHandsOutADisposedValue()
    {
        for (var round = 0; round < 50; round++)
        {
            var value = new TestDisposable();
            var resource = new SharedResource<TestDisposable>(value);
            var usedDisposed = 0;
            using var start = new ManualResetEventSlim();

            var drawers = new Task[8];
            for (var i = 0; i < drawers.Length; i++)
            {
                drawers[i] = Task.Run(() =>
                {
                    start.Wait();
                    for (var n = 0; n < 200; n++)
                    {
                        if (resource.TryAcquire(out var lease))
                        {
                            using (lease)
                            {
                                if (lease.Value.IsDisposed)
                                {
                                    Interlocked.Increment(ref usedDisposed);
                                }
                            }
                        }
                    }
                });
            }

            var owner = Task.Run(() =>
            {
                start.Wait();
                resource.Release();
            });

            start.Set();
            await Task.WhenAll(drawers.Append(owner)).ConfigureAwait(true);

            Assert.Equal(0, usedDisposed);
            Assert.Equal(1, value.DisposeCount);
        }
    }
}
