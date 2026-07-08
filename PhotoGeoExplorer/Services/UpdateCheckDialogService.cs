using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 手動更新チェックの実行・結果判定・ダイアログ表示を担う
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class UpdateCheckDialogService
{
    private readonly Func<string, string, Task> _showMessageDialogAsync;

    public UpdateCheckDialogService(Func<string, string, Task> showMessageDialogAsync)
    {
        _showMessageDialogAsync = showMessageDialogAsync ?? throw new ArgumentNullException(nameof(showMessageDialogAsync));
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            AppLog.Info("Manual update check triggered");
            var currentVersion = typeof(App).Assembly.GetName().Version;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var updateResult = await UpdateService.CheckForUpdatesAsync(currentVersion, cts.Token).ConfigureAwait(true);

            if (updateResult.IsUpdateAvailable)
            {
                var message = LocalizationService.Format("Dialog.UpdateCheck.UpdateAvailableDetail", updateResult.LatestVersion?.ToString() ?? "Unknown");
                await _showMessageDialogAsync(
                    LocalizationService.GetString("Dialog.UpdateCheck.Title"),
                    message).ConfigureAwait(true);
            }
            else
            {
                await _showMessageDialogAsync(
                    LocalizationService.GetString("Dialog.UpdateCheck.Title"),
                    LocalizationService.GetString("Dialog.UpdateCheck.NoUpdateDetail")).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Update check was cancelled (timeout or user action)");
            await _showMessageDialogAsync(
                LocalizationService.GetString("Dialog.UpdateCheck.Title"),
                LocalizationService.GetString("Dialog.UpdateCheck.ErrorDetail")).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            await HandleUpdateCheckFailureAsync(ex).ConfigureAwait(true);
        }
        catch (ArgumentException ex)
        {
            await HandleUpdateCheckFailureAsync(ex).ConfigureAwait(true);
        }
    }

    private async Task HandleUpdateCheckFailureAsync(Exception ex)
    {
        AppLog.Error("Failed to check for updates", ex);
        await _showMessageDialogAsync(
            LocalizationService.GetString("Dialog.UpdateCheck.Title"),
            LocalizationService.GetString("Dialog.UpdateCheck.ErrorDetail")).ConfigureAwait(true);
    }
}
