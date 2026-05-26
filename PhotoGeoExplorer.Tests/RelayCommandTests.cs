using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class RelayCommandTests
{
    [Fact]
    public async Task Execute_CanExecuteIsFalseDuringExecution()
    {
        var tcs = new TaskCompletionSource();
        var command = new RelayCommand(() => tcs.Task);

        command.Execute(null);
        await Task.Yield();

        Assert.False(command.CanExecute(null));

        var waitTask = WaitForCanExecuteChangedAsync(command);
        tcs.SetResult();
        await waitTask.ConfigureAwait(true);

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_SecondCallIsIgnoredDuringExecution()
    {
        var tcs = new TaskCompletionSource();
        var executionCount = 0;
        var command = new RelayCommand(() => { executionCount++; return tcs.Task; });

        command.Execute(null);
        await Task.Yield();

        command.Execute(null);
        command.Execute(null);

        Assert.Equal(1, executionCount);

        var waitTask = WaitForCanExecuteChangedAsync(command);
        tcs.SetResult();
        await waitTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task Execute_CanReexecuteAfterCompletion()
    {
        var executionCount = 0;
        var command = new RelayCommand(() => { executionCount++; return Task.CompletedTask; });

        var waitTask1 = WaitForCanExecuteChangedAsync(command);
        command.Execute(null);
        await waitTask1.ConfigureAwait(true);

        var waitTask2 = WaitForCanExecuteChangedAsync(command);
        command.Execute(null);
        await waitTask2.ConfigureAwait(true);

        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task Execute_RaisesCanExecuteChangedOnStartAndCompletion()
    {
        var tcs = new TaskCompletionSource();
        var command = new RelayCommand(() => tcs.Task);
        var changeCount = 0;
        command.CanExecuteChanged += (_, _) => changeCount++;

        command.Execute(null);
        await Task.Yield();

        Assert.Equal(1, changeCount);

        var waitTask = WaitForCanExecuteChangedAsync(command);
        tcs.SetResult();
        await waitTask.ConfigureAwait(true);

        Assert.Equal(2, changeCount);
    }

    [Fact]
    public void CanExecute_RespectsCanExecuteDelegate()
    {
        var allowed = false;
        var command = new RelayCommand(() => Task.CompletedTask, () => allowed);

        Assert.False(command.CanExecute(null));

        allowed = true;
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_Generic_GuardsAgainstReentrance()
    {
        var tcs = new TaskCompletionSource();
        var executionCount = 0;
        var command = new RelayCommand<string>(_ => { executionCount++; return tcs.Task; });

        command.Execute("first");
        await Task.Yield();

        Assert.False(command.CanExecute("second"));
        command.Execute("second");
        Assert.Equal(1, executionCount);

        var waitTask = WaitForCanExecuteChangedAsync(command);
        tcs.SetResult();
        await waitTask.ConfigureAwait(true);

        Assert.True(command.CanExecute(null));
    }

    private static async Task WaitForCanExecuteChangedAsync(RelayCommand command, int timeoutMs = 5000)
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? s, EventArgs e)
        {
            command.CanExecuteChanged -= Handler;
            tcs.TrySetResult();
        }

        command.CanExecuteChanged += Handler;
        await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(true);
        command.CanExecuteChanged -= Handler;
    }

    private static async Task WaitForCanExecuteChangedAsync(RelayCommand<string> command, int timeoutMs = 5000)
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? s, EventArgs e)
        {
            command.CanExecuteChanged -= Handler;
            tcs.TrySetResult();
        }

        command.CanExecuteChanged += Handler;
        await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(true);
        command.CanExecuteChanged -= Handler;
    }
}
