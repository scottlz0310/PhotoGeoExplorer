using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// クラッシュレポートダイアログの表示・アクション（GitHub Issue 起動・メール起動・フォルダを開く）を担う
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class CrashReportDialogService
{
    private readonly IDialogService _dialogService;
    private readonly Func<ICrashReportService?> _getCrashReportService;
    private readonly Func<string?> _getCrashReportsDirectoryPath;
    private readonly Action<string, InfoBarSeverity> _showNotification;

    public CrashReportDialogService(
        IDialogService dialogService,
        Func<ICrashReportService?> getCrashReportService,
        Func<string?> getCrashReportsDirectoryPath,
        Action<string, InfoBarSeverity> showNotification)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _getCrashReportService = getCrashReportService ?? throw new ArgumentNullException(nameof(getCrashReportService));
        _getCrashReportsDirectoryPath = getCrashReportsDirectoryPath ?? throw new ArgumentNullException(nameof(getCrashReportsDirectoryPath));
        _showNotification = showNotification ?? throw new ArgumentNullException(nameof(showNotification));
    }

    public async Task ShowCrashReportDialogAsync()
    {
        var logContent = _getCrashReportService()?.GetLatestCrashLogContent();
        var summaryPanel = BuildCrashReportSummaryPanel(logContent);

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("CrashReportDialog.Title"),
            Content = summaryPanel,
            PrimaryButtonText = LocalizationService.GetString("CrashReportDialog.GitHubButton"),
            SecondaryButtonText = LocalizationService.GetString("CrashReportDialog.CopyButton"),
            CloseButtonText = LocalizationService.GetString("CrashReportDialog.CloseButton")
        };

        var result = await _dialogService.ShowContentDialogAsync(dialog).ConfigureAwait(true);

        if (result == ContentDialogResult.Primary)
        {
            await OpenCrashReportGitHubIssueAsync(logContent).ConfigureAwait(true);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await CopyLogAndOpenMailAsync(logContent).ConfigureAwait(true);
        }
    }

    public async Task OpenLogFolderAsync()
    {
        try
        {
            var logDirectory = Path.GetDirectoryName(AppLog.LogFilePath);
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                AppLog.Error("Log directory path is null or empty");
                return;
            }

            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
                AppLog.Info($"Created log directory: {logDirectory}");
            }

            var launched = await Windows.System.Launcher.LaunchFolderPathAsync(logDirectory);
            if (launched)
            {
                AppLog.Info($"Opened log folder: {logDirectory}");
            }
            else
            {
                HandleOpenLogFolderFailure($"Failed to launch log folder: {logDirectory}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (IOException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (ArgumentException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (InvalidOperationException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
    }

    private async Task OpenCrashReportsFolderAsync()
    {
        var path = _getCrashReportsDirectoryPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var launched = await Windows.System.Launcher.LaunchFolderPathAsync(path);
            if (!launched)
            {
                HandleOpenLogFolderFailure($"Failed to launch crash report folder: {path}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (IOException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (ArgumentException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
        catch (InvalidOperationException ex)
        {
            HandleOpenLogFolderFailure(ex);
        }
    }

    private StackPanel BuildCrashReportSummaryPanel(string? logContent)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 480 };

        var version = ParseCrashLogField(logContent, "App Version:");
        var timestamp = ParseCrashLogField(logContent, "Timestamp:");
        var exType = ParseCrashLogField(logContent, "Exception Type:");

        if (!string.IsNullOrEmpty(version) || !string.IsNullOrEmpty(timestamp) || !string.IsNullOrEmpty(exType))
        {
            var infoLines = new StackPanel { Spacing = 2 };
            if (!string.IsNullOrEmpty(version))
                infoLines.Children.Add(new TextBlock { Text = $"{LocalizationService.GetString("CrashReportDialog.LabelVersion")} {version}" });
            if (!string.IsNullOrEmpty(timestamp))
                infoLines.Children.Add(new TextBlock { Text = $"{LocalizationService.GetString("CrashReportDialog.LabelTimestamp")} {timestamp}" });
            if (!string.IsNullOrEmpty(exType))
                infoLines.Children.Add(new TextBlock { Text = $"{LocalizationService.GetString("CrashReportDialog.LabelException")} {exType}", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(infoLines);
        }

        panel.Children.Add(new TextBlock
        {
            Text = LocalizationService.GetString("CrashReportDialog.SupportNote"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12
        });

        var folderLink = new HyperlinkButton
        {
            Content = LocalizationService.GetString("CrashReportDialog.OpenFolderLink"),
            Padding = new Microsoft.UI.Xaml.Thickness(0)
        };
        folderLink.Click += async (_, _) => await OpenCrashReportsFolderAsync().ConfigureAwait(true);
        panel.Children.Add(folderLink);

        return panel;
    }

    internal static string? ParseCrashLogField(string? logContent, string fieldName)
    {
        if (string.IsNullOrEmpty(logContent)) return null;
        foreach (var line in logContent.Split('\n'))
        {
            if (line.StartsWith(fieldName, StringComparison.Ordinal))
                return line[fieldName.Length..].Trim();
        }
        return null;
    }

    internal static string BuildCrashReportGitHubIssueUrl(string? logContent)
    {
        const string baseUrl = "https://github.com/scottlz0310/PhotoGeoExplorer/issues/new";
        var exType = ParseCrashLogField(logContent, "Exception Type:") ?? "Unknown";
        var title = Uri.EscapeDataString($"[Problem] {exType}");

        var truncated = logContent is { Length: > 2000 }
            ? logContent[..2000] + "\n...(truncated)"
            : logContent ?? "(no log)";

        var body = Uri.EscapeDataString(
            "## 問題レポート\n\n" +
            "PhotoGeoExplorer の実行中に問題が検出されました。\n\n" +
            "<details>\n<summary>診断ログ</summary>\n\n```\n" +
            truncated +
            "\n```\n\n</details>\n\n" +
            "---\n*PhotoGeoExplorer から自動生成されました。*");

        return $"{baseUrl}?title={title}&labels=bug&body={body}";
    }

    private static async Task OpenCrashReportGitHubIssueAsync(string? logContent)
    {
        var url = BuildCrashReportGitHubIssueUrl(logContent);
        _ = await Windows.System.Launcher.LaunchUriAsync(new Uri(url)).AsTask().ConfigureAwait(true);
    }

    internal static Uri BuildCrashReportMailtoUri(string? logContent)
    {
        var version = ParseCrashLogField(logContent, "App Version:") ?? string.Empty;
        var exType = ParseCrashLogField(logContent, "Exception Type:") ?? "Unknown";
        var subject = Uri.EscapeDataString($"[Problem Report] v{version} {exType}");
        var body = Uri.EscapeDataString(LocalizationService.GetString("CrashReportDialog.MailBody"));
        return new Uri($"mailto:photogeoexplorer@outlook.com?subject={subject}&body={body}");
    }

    private static async Task CopyLogAndOpenMailAsync(string? logContent)
    {
        var text = logContent ?? LocalizationService.GetString("CrashReportDialog.NoLog");
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

        var mailto = BuildCrashReportMailtoUri(logContent);
        _ = await Windows.System.Launcher.LaunchUriAsync(mailto).AsTask().ConfigureAwait(true);
    }

    private void HandleOpenLogFolderFailure(Exception ex)
    {
        AppLog.Error("Failed to open log folder", ex);
        _showNotification(
            LocalizationService.GetString("Message.FailedOpenLogFolder"),
            InfoBarSeverity.Error);
    }

    private void HandleOpenLogFolderFailure(string logMessage)
    {
        AppLog.Error(logMessage);
        _showNotification(
            LocalizationService.GetString("Message.FailedOpenLogFolder"),
            InfoBarSeverity.Error);
    }
}
