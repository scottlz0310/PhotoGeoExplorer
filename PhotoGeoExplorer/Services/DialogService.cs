using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PhotoGeoExplorer.Services;

internal sealed class DialogService : IDialogService
{
    private readonly FrameworkElement _dialogHost;
    private readonly Window _pickerHostWindow;
    private bool _isWaitingForXamlRoot;

    public DialogService(FrameworkElement dialogHost, Window pickerHostWindow)
    {
        _dialogHost = dialogHost ?? throw new ArgumentNullException(nameof(dialogHost));
        _pickerHostWindow = pickerHostWindow ?? throw new ArgumentNullException(nameof(pickerHostWindow));
    }

    public async Task<ContentDialogResult?> ShowContentDialogAsync(ContentDialog dialog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (!await EnsureXamlRootAsync(cancellationToken).ConfigureAwait(true))
        {
            return null;
        }

        dialog.XamlRoot = _dialogHost.XamlRoot;
        return await dialog.ShowAsync().AsTask(cancellationToken).ConfigureAwait(true);
    }

    public async Task ShowMessageDialogAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(message);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = LocalizationService.GetString("Common.Ok")
        };

        _ = await ShowContentDialogAsync(dialog, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StorageFile?> ShowFilePickerAsync(
        PickerLocationId startLocation,
        IReadOnlyList<string>? fileTypeFilter = null,
        CancellationToken cancellationToken = default)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = startLocation
        };

        if (fileTypeFilter is { Count: > 0 })
        {
            foreach (var extension in fileTypeFilter)
            {
                if (!string.IsNullOrWhiteSpace(extension))
                {
                    picker.FileTypeFilter.Add(extension);
                }
            }
        }
        else
        {
            picker.FileTypeFilter.Add("*");
        }

        var hwnd = WindowNative.GetWindowHandle(_pickerHostWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        try
        {
            return await picker.PickSingleFileAsync().AsTask(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("File picker failed.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("File picker failed.", ex);
        }

        return null;
    }

    public async Task<StorageFile?> ShowSaveFilePickerAsync(
        PickerLocationId startLocation,
        string suggestedFileName,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fileTypeChoices = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            throw new ArgumentException("Suggested file name is required.", nameof(suggestedFileName));
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = startLocation,
            SuggestedFileName = suggestedFileName
        };

        if (fileTypeChoices is { Count: > 0 })
        {
            foreach (var (label, extensions) in fileTypeChoices)
            {
                if (string.IsNullOrWhiteSpace(label) || extensions is null || extensions.Count == 0)
                {
                    continue;
                }

                picker.FileTypeChoices.Add(label, new List<string>(extensions));
            }
        }

        if (picker.FileTypeChoices.Count == 0)
        {
            picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        }

        var hwnd = WindowNative.GetWindowHandle(_pickerHostWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        try
        {
            return await picker.PickSaveFileAsync().AsTask(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("File save picker failed.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("File save picker failed.", ex);
        }

        return null;
    }

    private async Task<bool> EnsureXamlRootAsync(CancellationToken cancellationToken)
    {
        const int maxWaitMs = 3000;
        const int intervalMs = 50;

        if (_dialogHost.XamlRoot is not null)
        {
            return true;
        }

        if (_isWaitingForXamlRoot)
        {
            var pollingElapsed = 0;
            while (_dialogHost.XamlRoot is null && pollingElapsed < maxWaitMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(true);
                pollingElapsed += intervalMs;
            }

            return _dialogHost.XamlRoot is not null;
        }

        _isWaitingForXamlRoot = true;
        AppLog.Info("DialogService.EnsureXamlRootAsync: XamlRoot is null, waiting for it to become available...");

        var tcs = new TaskCompletionSource<bool>();
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            _dialogHost.Loaded -= OnLoaded;
            tcs.TrySetResult(true);
        }

        _dialogHost.Loaded += OnLoaded;

        var elapsed = 0;
        while (_dialogHost.XamlRoot is null && elapsed < maxWaitMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(true);
            elapsed += intervalMs;

            if (tcs.Task.IsCompleted)
            {
                break;
            }
        }

        _dialogHost.Loaded -= OnLoaded;
        _isWaitingForXamlRoot = false;

        if (_dialogHost.XamlRoot is not null)
        {
            AppLog.Info($"DialogService.EnsureXamlRootAsync: XamlRoot became available after {elapsed}ms.");
            return true;
        }

        AppLog.Info($"DialogService.EnsureXamlRootAsync: XamlRoot still null after {elapsed}ms, giving up.");
        return false;
    }
}
