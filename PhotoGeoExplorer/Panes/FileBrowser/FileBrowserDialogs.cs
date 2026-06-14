using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// FileBrowser ペイン固有の複合ダイアログ・ピッカー表示を担うヘルパー。
/// View から XamlRoot 供給元と HostWindow を受け取り、コードビハインドの肥大化を防ぐ。
/// 汎用の <see cref="DialogService"/> とは責務が異なるため統合しない（フォローアップは ISSUE #156 参照）。
/// </summary>
internal sealed class FileBrowserDialogs
{
    private readonly FrameworkElement _xamlRootSource;
    private readonly Func<Window?> _hostWindowAccessor;
    private bool _isWaitingForXamlRoot;

    public FileBrowserDialogs(FrameworkElement xamlRootSource, Func<Window?> hostWindowAccessor)
    {
        _xamlRootSource = xamlRootSource;
        _hostWindowAccessor = hostWindowAccessor;
    }

    public Task<ConflictResolution> ShowMoveConflictAsync(string fileName, bool isFolder)
        => ShowConflictAsync("Dialog.MoveConflict", fileName);

    public Task<ConflictResolution> ShowCopyConflictAsync(string fileName, bool isFolder)
        => ShowConflictAsync("Dialog.CopyConflict", fileName);

    public Task ShowFileOperationErrorAsync(FileOperationError error, string defaultTitleKey)
        => ShowMappedErrorAsync(FileBrowserDialogErrorMap.MapFileOperationError(error, defaultTitleKey));

    public Task ShowMoveOperationErrorAsync(FileOperationSummary summary)
        => ShowMappedErrorAsync(FileBrowserDialogErrorMap.MapMoveError(summary.Failures[0].Error));

    public Task ShowCopyOperationErrorAsync(FileOperationSummary summary)
        => ShowMappedErrorAsync(FileBrowserDialogErrorMap.MapCopyError(summary.Failures[0].Error));

    public Task ShowDeleteOperationErrorAsync(FileOperationSummary summary)
        => ShowMappedErrorAsync(FileBrowserDialogErrorMap.MapDeleteError(summary.Failures[0].Error));

    private Task ShowMappedErrorAsync((string TitleKey, string MessageKey) keys)
        => ShowMessageAsync(
            LocalizationService.GetString(keys.TitleKey),
            LocalizationService.GetString(keys.MessageKey));

    public async Task<StorageFolder?> PickFolderAsync(PickerLocationId startLocation)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = startLocation
        };
        picker.FileTypeFilter.Add("*");

        var hostWindow = _hostWindowAccessor();
        if (hostWindow is null)
        {
            AppLog.Error("HostWindow is not set for FileBrowserPaneView.");
            return null;
        }

        var hwnd = WindowNative.GetWindowHandle(hostWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        try
        {
            return await picker.PickSingleFolderAsync().AsTask().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
        }

        return null;
    }

    public async Task<string?> ShowTextInputAsync(
        string title,
        string primaryButtonText,
        string? defaultText,
        string placeholderText)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return null;
        }

        var textBox = new TextBox
        {
            Text = defaultText ?? string.Empty,
            PlaceholderText = placeholderText,
            MinWidth = 260
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = LocalizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRootSource.XamlRoot
        };

        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        textBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        };
        dialog.Opened += (_, _) =>
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        };

        var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        var value = textBox.Text.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string primaryButtonText)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return false;
        }

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        };
        // E2E が確認ダイアログの文面（削除確認の単数/複数・ファイル/フォルダ分岐など）を検証できるよう ID を付与する
        AutomationProperties.SetAutomationId(messageBlock, "FileBrowser.ConfirmationMessage");

        var dialog = new ContentDialog
        {
            Title = title,
            Content = messageBlock,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = LocalizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = _xamlRootSource.XamlRoot
        };

        var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = LocalizationService.GetString("Common.Ok"),
            XamlRoot = _xamlRootSource.XamlRoot
        };

        await dialog.ShowAsync().AsTask().ConfigureAwait(true);
    }

    /// <summary>
    /// Move/Copy で共通の競合解決ダイアログ。リソースキー接頭辞のみが異なるため統一実装とする。
    /// </summary>
    private async Task<ConflictResolution> ShowConflictAsync(string resourcePrefix, string fileName)
    {
        var detail = LocalizationService.Format($"{resourcePrefix}.Detail", fileName);

        // StackPanel で「すべて上書き」「すべてスキップ」ボタンを追加
        var overwriteAllButton = new Button
        {
            Content = LocalizationService.GetString($"{resourcePrefix}.OverwriteAll"),
            Margin = new Thickness(0, 0, 8, 0),
        };
        var skipAllButton = new Button
        {
            Content = LocalizationService.GetString($"{resourcePrefix}.SkipAll"),
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString($"{resourcePrefix}.Title"),
            PrimaryButtonText = LocalizationService.GetString($"{resourcePrefix}.Overwrite"),
            SecondaryButtonText = LocalizationService.GetString($"{resourcePrefix}.Skip"),
            CloseButtonText = LocalizationService.GetString($"{resourcePrefix}.Cancel"),
            XamlRoot = _xamlRootSource.XamlRoot,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { overwriteAllButton, skipAllButton },
                    },
                },
            },
        };

        ConflictResolution? extraChoice = null;
        overwriteAllButton.Click += (_, _) =>
        {
            extraChoice = ConflictResolution.OverwriteAll;
            dialog.Hide();
        };
        skipAllButton.Click += (_, _) =>
        {
            extraChoice = ConflictResolution.SkipAll;
            dialog.Hide();
        };

        var result = await dialog.ShowAsync();
        if (extraChoice.HasValue)
        {
            return extraChoice.Value;
        }

        return result switch
        {
            ContentDialogResult.Primary => ConflictResolution.Overwrite,
            ContentDialogResult.Secondary => ConflictResolution.Skip,
            _ => ConflictResolution.Cancel,
        };
    }

    private async Task<bool> EnsureXamlRootAsync()
    {
        const int maxWaitMs = 3000;
        const int intervalMs = 50;

        if (_xamlRootSource.XamlRoot is not null)
        {
            return true;
        }

        // 既に別の呼び出しで待機中の場合は、重複してイベントハンドラを登録しない
        if (_isWaitingForXamlRoot)
        {
            // ポーリングのみで待機
            var pollingElapsed = 0;
            while (_xamlRootSource.XamlRoot is null && pollingElapsed < maxWaitMs)
            {
                await Task.Delay(intervalMs).ConfigureAwait(true);
                pollingElapsed += intervalMs;
            }
            return _xamlRootSource.XamlRoot is not null;
        }

        _isWaitingForXamlRoot = true;

        AppLog.Info("EnsureXamlRootAsync: XamlRoot is null, waiting for it to become available...");

        var tcs = new TaskCompletionSource<bool>();
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            _xamlRootSource.Loaded -= OnLoaded;
            tcs.TrySetResult(true);
        }

        _xamlRootSource.Loaded += OnLoaded;

        var elapsed = 0;
        while (_xamlRootSource.XamlRoot is null && elapsed < maxWaitMs)
        {
            await Task.Delay(intervalMs).ConfigureAwait(true);
            elapsed += intervalMs;

            if (tcs.Task.IsCompleted)
            {
                break;
            }
        }

        _xamlRootSource.Loaded -= OnLoaded;
        _isWaitingForXamlRoot = false;

        if (_xamlRootSource.XamlRoot is not null)
        {
            AppLog.Info($"EnsureXamlRootAsync: XamlRoot became available after {elapsed}ms.");
            return true;
        }

        AppLog.Info($"EnsureXamlRootAsync: XamlRoot still null after {elapsed}ms, giving up.");
        return false;
    }
}
