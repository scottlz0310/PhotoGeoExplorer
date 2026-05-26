using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;

namespace PhotoGeoExplorer.ViewModels;

/// <summary>
/// シンプルな RelayCommand 実装
/// </summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _isExecuting;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _dispatcherQueue = TryGetDispatcherQueue();
    }

    private static DispatcherQueue? TryGetDispatcherQueue()
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // WinUI 3 ランタイムが起動していない環境（テスト等）では null を返す
            return null;
        }
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("RelayCommand execution canceled.");
        }
        catch (Exception ex)
        {
            AppLog.Error("RelayCommand execution failed.", ex);
            throw;
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChangedOnUiThread();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyCanExecuteChangedOnUiThread()
    {
        if (_dispatcherQueue is not null)
        {
            _dispatcherQueue.TryEnqueue(RaiseCanExecuteChanged);
        }
        else
        {
            RaiseCanExecuteChanged();
        }
    }
}

/// <summary>
/// パラメータ付き RelayCommand 実装
/// </summary>
internal sealed class RelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _isExecuting;

    public RelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _dispatcherQueue = TryGetDispatcherQueue();
    }

    private static DispatcherQueue? TryGetDispatcherQueue()
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke((T?)parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute((T?)parameter).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("RelayCommand<T> execution canceled.");
        }
        catch (Exception ex)
        {
            AppLog.Error("RelayCommand<T> execution failed.", ex);
            throw;
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChangedOnUiThread();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyCanExecuteChangedOnUiThread()
    {
        if (_dispatcherQueue is not null)
        {
            _dispatcherQueue.TryEnqueue(RaiseCanExecuteChanged);
        }
        else
        {
            RaiseCanExecuteChanged();
        }
    }
}
