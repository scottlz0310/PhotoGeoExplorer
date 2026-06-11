using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// UiDispatcher のテスト
/// テスト環境では DispatcherQueue が取得できないため、フォールバック動作を検証する
/// </summary>
public class UiDispatcherTests
{
    [Fact]
    public void IsAvailableReturnsFalseWithoutDispatcherQueue()
    {
        // Arrange
        var dispatcher = new UiDispatcher();

        // Assert
        Assert.False(dispatcher.IsAvailable);
    }

    [Fact]
    public async Task RunAsyncExecutesSynchronouslyWithoutDispatcherQueue()
    {
        // Arrange
        var dispatcher = new UiDispatcher();
        var executed = false;

        // Act
        await dispatcher.RunAsync(() => executed = true);

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public async Task RunAsyncPropagatesExceptionWithoutDispatcherQueue()
    {
        // Arrange
        var dispatcher = new UiDispatcher();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.RunAsync(() => throw new InvalidOperationException("test")));
    }

    [Fact]
    public async Task EnqueueAsyncExecutesDirectlyWithoutDispatcherQueue()
    {
        // Arrange
        var dispatcher = new UiDispatcher();

        // Act
        var result = await dispatcher.EnqueueAsync(() => Task.FromResult(42));

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void TryEnqueueReturnsFalseWithoutDispatcherQueue()
    {
        // Arrange
        var dispatcher = new UiDispatcher();
        var executed = false;

        // Act
        var enqueued = dispatcher.TryEnqueue(() => executed = true);

        // Assert
        Assert.False(enqueued);
        Assert.False(executed);
    }

    [Fact]
    public void CreateTimerReturnsNullWithoutDispatcherQueue()
    {
        // Arrange
        var dispatcher = new UiDispatcher();

        // Assert
        Assert.Null(dispatcher.CreateTimer());
    }
}
