using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Panes.Preview;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
[ExcludeFromCodeCoverage]
public sealed partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsCoordinator _settingsCoordinator;
    private readonly StartupCoordinator _startupCoordinator;
    private readonly FileBrowserPaneViewModel _fileBrowserPaneViewModel;
    private readonly PreviewPaneViewModel _previewPaneViewModel;
    private readonly MapPaneViewModel _mapPaneViewModel;
    private Services.ICrashReportService? _crashReportService;
    private bool _mapInitialized;
    private bool _disposed;
    private bool _windowSized;
    private bool _windowIconSet;
    private readonly HelpService _helpService;
    private readonly MainWindowLayoutCoordinator _layoutCoordinator;
    internal FileBrowserPaneViewModel FileBrowserPaneViewModel => _fileBrowserPaneViewModel;

    internal void SetCrashReportService(Services.ICrashReportService crashReportService)
    {
        _crashReportService = crashReportService ?? throw new ArgumentNullException(nameof(crashReportService));
    }

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        var dialogService = new DialogService(RootGrid, this);
        var exifEditorService = new ExifEditorService(
            dialogService,
            new ExifMetadataService(),
            MapPaneControl,
            (message, severity) => _viewModel.ShowNotificationMessage(message, severity));
        _fileBrowserPaneViewModel = new FileBrowserPaneViewModel(
            new FileBrowserPaneService(),
            _viewModel.WorkspaceState,
            exifEditorService,
            dialogService);
        _startupCoordinator = new StartupCoordinator(_fileBrowserPaneViewModel);
        _previewPaneViewModel = new PreviewPaneViewModel(new PreviewPaneService(), _viewModel.WorkspaceState);
        _mapPaneViewModel = new MapPaneViewModel(new MapPaneService(), _viewModel.WorkspaceState);
        _settingsCoordinator = new SettingsCoordinator(
            new SettingsService(),
            dialogService,
            theme => RootGrid.RequestedTheme = theme,
            _fileBrowserPaneViewModel,
            _mapPaneViewModel,
            _viewModel,
            () => _startupCoordinator.StartupFilePath);
        _helpService = new HelpService(
            dialogService,
            () => _settingsCoordinator.LanguageOverride,
            () => _settingsCoordinator.ExternalContentBaseUrl,
            () => _settingsCoordinator.ShowQuickStartOnStartup,
            value => _settingsCoordinator.ShowQuickStartOnStartup = value,
            () => _settingsCoordinator.SaveAsync(),
            _settingsCoordinator.SettingsFileExistsAtStartup,
            (message, severity) => _viewModel.ShowNotificationMessage(message, severity));
        _viewModel.ConfigureHelpService(_helpService);
        _viewModel.ConfigureSettingsCoordinator(_settingsCoordinator);
        RootGrid.DataContext = _viewModel;
        FileBrowserPaneControl.DataContext = _fileBrowserPaneViewModel;
        FileBrowserPaneControl.HostWindow = this;
        PreviewPaneControl.DataContext = _previewPaneViewModel;
        PreviewPaneControl.MaximizeChanged += OnPreviewMaximizeChanged;
        MapPaneControl.DataContext = _mapPaneViewModel;
        var paneLayoutHostService = new PaneLayoutHostService(
            FileBrowserPane,
            DetailPane,
            MainSplitter,
            LeftSingleHost,
            LeftVerticalSplitHost,
            LeftTopHost,
            LeftBottomHost,
            LeftRowSplitter,
            RightSingleHost,
            RightVerticalSplitHost,
            RightTopHost,
            RightBottomHost,
            MapPane,
            MapRowSplitter,
            RightHorizontalSplitHost,
            RightHorizontalLeftHost,
            RightHorizontalRightHost,
            RightColumnSplitter,
            PaneStagingArea,
            FileBrowserPaneControl,
            PreviewPaneControl,
            MapPaneControl);
        _layoutCoordinator = new MainWindowLayoutCoordinator(
            paneLayoutHostService,
            PreviewPaneControl,
            FileBrowserColumn,
            SplitterColumn,
            DetailColumn,
            MainContentGrid,
            RightVerticalSplitHost,
            PreviewRow,
            MapRow,
            MapSplitterRow,
            LeftVerticalSplitHost,
            LeftTopRow,
            LeftBottomRow,
            LeftSplitterRow,
            RightHorizontalSplitHost,
            RightLeftColumn,
            RightRightColumn,
            RightSplitterColumn);
        _settingsCoordinator.PaneLayoutChanged += OnPaneLayoutChanged;
        _layoutCoordinator.ApplyPaneLayout(
            _settingsCoordinator.PaneLayoutPreset,
            _settingsCoordinator.PaneRegion1View,
            _settingsCoordinator.PaneRegion2View,
            _settingsCoordinator.PaneRegion3View);
        UpdateToggleImagesOnlyMenuText();
        Title = LocalizationService.GetString("MainWindow.Title");
        AppLog.Info("MainWindow constructed.");
        Activated += OnActivated;
        Closed += OnClosed;
        _fileBrowserPaneViewModel.PropertyChanged += OnFileBrowserPanePropertyChanged;
        _viewModel.WorkspaceState.PhotoSelectionRequested += OnWorkspacePhotoSelectionRequested;
        _viewModel.WorkspaceState.NotificationRequested += OnWorkspaceNotificationRequested;
    }

    private void OnPreviewMaximizeChanged(object? sender, bool maximize)
    {
        _layoutCoordinator.TogglePreviewMaximize(maximize);
        _viewModel.PersistLayoutSettingsCommand.Execute(null);
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_mapInitialized)
        {
            return;
        }

        EnsureWindowSize();
        EnsureWindowIcon();
        _mapInitialized = true;
        AppLog.Info("MainWindow activated.");

        // MapPaneViewModel を初期化
        await _mapPaneViewModel.InitializeAsync().ConfigureAwait(true);

        await _settingsCoordinator.LoadAsync().ConfigureAwait(true);
        await _startupCoordinator.ApplyStartupAsync().ConfigureAwait(true);
        await _fileBrowserPaneViewModel.InitializeAsync().ConfigureAwait(true);

        // XamlRoot が確定するまでワンテンポ遅らせてからダイアログを表示
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_crashReportService?.PreviouslyTerminatedAbnormally == true)
            {
                _viewModel.ShowCrashReportBanner(_crashReportService.CrashReportsDirectoryPath);
            }

            await _helpService.ShowQuickStartIfNeededAsync().ConfigureAwait(true);
        });
    }

    private void EnsureWindowSize()
    {
        if (_windowSized)
        {
            return;
        }

        _windowSized = true;

        try
        {
            AppWindow.Resize(new SizeInt32(1200, 800));
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
    }

    private void EnsureWindowIcon()
    {
        if (_windowIconSet)
        {
            return;
        }

        _windowIconSet = true;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (!File.Exists(iconPath))
        {
            AppLog.Error($"Window icon not found: {iconPath}");
            return;
        }

        try
        {
            AppWindow.SetIcon(iconPath);
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to set window icon.", ex);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to set window icon.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to set window icon.", ex);
        }
    }
    public void SetStartupFilePath(string filePath)
    {
        _startupCoordinator.SetStartupFilePath(filePath);
    }

    private void OnFileBrowserPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 地図の更新は WorkspaceState 経由で MapPaneViewModel が行うため、ここでは不要

        if (e.PropertyName is nameof(FileBrowserPaneViewModel.ShowImagesOnly))
        {
            DispatcherQueue.TryEnqueue(UpdateToggleImagesOnlyMenuText);
        }
    }

    private void UpdateToggleImagesOnlyMenuText()
    {
        ToggleImagesOnlyMenuItem.Text = _fileBrowserPaneViewModel.ToggleImagesOnlyMenuText;
    }

    private void OnWorkspacePhotoSelectionRequested(object? sender, WorkspacePhotoSelectionRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        FileBrowserPaneControl.SelectItemsByFilePaths(e.FilePaths);
    }

    private void OnWorkspaceNotificationRequested(object? sender, WorkspaceNotificationRequestedEventArgs e)
    {
        _viewModel.ShowNotificationMessage(e.Message, e.Severity);
    }

    private void OnPaneLayoutChanged(object? sender, PaneLayoutChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _layoutCoordinator.ApplyPaneLayout(
                e.Preset,
                e.Region1View,
                e.Region2View,
                e.Region3View);
        });
    }

    private void OnMainSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        _layoutCoordinator.ApplyMainSplitterDelta(e.HorizontalChange);
    }

    private void OnMainSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _viewModel.PersistLayoutSettingsCommand.Execute(null);
    }

    private void OnMapSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        _layoutCoordinator.ApplyMapSplitterDelta(e.VerticalChange);
    }

    private void OnMapSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _viewModel.PersistLayoutSettingsCommand.Execute(null);
    }

    private void OnLeftSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        _layoutCoordinator.ApplyLeftSplitterDelta(e.VerticalChange);
    }

    private void OnRightHorizontalSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        _layoutCoordinator.ApplyRightHorizontalSplitterDelta(e.HorizontalChange);
    }

    private void OnInnerSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _viewModel.PersistLayoutSettingsCommand.Execute(null);
    }

    private async void OnOpenSettingsPaneClicked(object sender, RoutedEventArgs e)
    {
        if (!await EnsureXamlRootAsync().ConfigureAwait(true))
        {
            return;
        }

        if (Application.Current.Resources["SettingsPaneTemplate"] is not DataTemplate template)
        {
            AppLog.Error("Settings pane template not found.");
            return;
        }

        var viewModel = new SettingsPaneViewModel(
            _settingsCoordinator,
            _fileBrowserPaneViewModel,
            _viewModel);
        await viewModel.InitializeAsync().ConfigureAwait(true);
        viewModel.IsActive = true;

        var content = new ContentControl
        {
            Content = viewModel,
            ContentTemplate = template
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("MenuSettingsOpenPane.Text"),
            Content = content,
            CloseButtonText = LocalizationService.GetString("Common.Ok"),
            XamlRoot = RootGrid.XamlRoot
        };

        try
        {
            await dialog.ShowAsync().AsTask().ConfigureAwait(true);
            await viewModel.SaveIfDirtyAsync().ConfigureAwait(true);
        }
        finally
        {
            viewModel.IsActive = false;
            viewModel.Cleanup();
        }
    }

    private async void OnNotificationActionClicked(object sender, RoutedEventArgs e)
    {
        var url = _viewModel.NotificationActionUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        AppLog.Info("MainWindow.Closed event received.");
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppLog.Info("MainWindow dispose started.");

        try
        {
            _helpService.Dispose();
            _startupCoordinator.Dispose();
            _settingsCoordinator.PaneLayoutChanged -= OnPaneLayoutChanged;
            _settingsCoordinator.Dispose();

            // WorkspaceState イベントをアンサブスクライブ
            _viewModel.WorkspaceState.PhotoSelectionRequested -= OnWorkspacePhotoSelectionRequested;
            _viewModel.WorkspaceState.NotificationRequested -= OnWorkspaceNotificationRequested;
            _fileBrowserPaneViewModel.PropertyChanged -= OnFileBrowserPanePropertyChanged;

            // MapPaneViewModel のクリーンアップ
            _mapPaneViewModel.Cleanup();

            if (PreviewPaneControl is not null)
            {
                PreviewPaneControl.MaximizeChanged -= OnPreviewMaximizeChanged;
                PreviewPaneControl.DataContext = null;
            }

            if (FileBrowserPaneControl is not null)
            {
                FileBrowserPaneControl.DataContext = null;
                FileBrowserPaneControl.HostWindow = null;
            }

            if (MapPaneControl is not null)
            {
                MapPaneControl.DataContext = null;
            }

            _previewPaneViewModel?.Cleanup();
            _fileBrowserPaneViewModel.Dispose();

        }
        finally
        {
            AppLog.Info("MainWindow dispose completed.");
            GC.SuppressFinalize(this);
        }
    }

    private async void OnReportCrashClicked(object sender, RoutedEventArgs e)
    {
        var logContent = _crashReportService?.GetLatestCrashLogContent();

        var summaryPanel = BuildCrashReportSummaryPanel(logContent);

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("CrashReportDialog.Title"),
            Content = summaryPanel,
            PrimaryButtonText = LocalizationService.GetString("CrashReportDialog.GitHubButton"),
            SecondaryButtonText = LocalizationService.GetString("CrashReportDialog.CopyButton"),
            CloseButtonText = LocalizationService.GetString("CrashReportDialog.CloseButton"),
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await OpenCrashReportGitHubIssueAsync(logContent).ConfigureAwait(true);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await CopyLogAndOpenMailAsync(logContent).ConfigureAwait(true);
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
        folderLink.Click += OnOpenCrashReportFolderClicked;
        panel.Children.Add(folderLink);

        return panel;
    }

    private static string? ParseCrashLogField(string? logContent, string fieldName)
    {
        if (string.IsNullOrEmpty(logContent)) return null;
        foreach (var line in logContent.Split('\n'))
        {
            if (line.StartsWith(fieldName, StringComparison.Ordinal))
                return line[fieldName.Length..].Trim();
        }
        return null;
    }

    private static async Task OpenCrashReportGitHubIssueAsync(string? logContent)
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

        var url = $"{baseUrl}?title={title}&labels=bug&body={body}";
        _ = await Windows.System.Launcher.LaunchUriAsync(new Uri(url)).AsTask().ConfigureAwait(true);
    }

    private static async Task CopyLogAndOpenMailAsync(string? logContent)
    {
        var text = logContent ?? LocalizationService.GetString("CrashReportDialog.NoLog");
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

        var version = ParseCrashLogField(logContent, "App Version:") ?? string.Empty;
        var exType = ParseCrashLogField(logContent, "Exception Type:") ?? "Unknown";
        var subject = Uri.EscapeDataString($"[Problem Report] v{version} {exType}");
        var body = Uri.EscapeDataString(LocalizationService.GetString("CrashReportDialog.MailBody"));
        var mailto = new Uri($"mailto:photogeoexplorer@outlook.com?subject={subject}&body={body}");
        _ = await Windows.System.Launcher.LaunchUriAsync(mailto).AsTask().ConfigureAwait(true);
    }

    private async void OnOpenCrashReportFolderClicked(object sender, RoutedEventArgs e)
    {
        var path = _viewModel.CrashReportsDirectoryPath;
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

            _ = await Windows.System.Launcher.LaunchFolderPathAsync(path);
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

    private async void OnOpenLogFolderClicked(object sender, RoutedEventArgs e)
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

            _ = await Windows.System.Launcher.LaunchFolderPathAsync(logDirectory);
            AppLog.Info($"Opened log folder: {logDirectory}");
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

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
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
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.UpdateCheck.Title"),
                    message).ConfigureAwait(true);
            }
            else
            {
                await ShowMessageDialogAsync(
                    LocalizationService.GetString("Dialog.UpdateCheck.Title"),
                    LocalizationService.GetString("Dialog.UpdateCheck.NoUpdateDetail")).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Update check was cancelled (timeout or user action)");
            await ShowMessageDialogAsync(
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

    private void HandleOpenLogFolderFailure(Exception ex)
    {
        AppLog.Error("Failed to open log folder", ex);
        _viewModel.ShowNotificationMessage(
            LocalizationService.GetString("Message.FailedOpenLogFolder"),
            InfoBarSeverity.Error);
    }

    private async Task HandleUpdateCheckFailureAsync(Exception ex)
    {
        AppLog.Error("Failed to check for updates", ex);
        await ShowMessageDialogAsync(
            LocalizationService.GetString("Dialog.UpdateCheck.Title"),
            LocalizationService.GetString("Dialog.UpdateCheck.ErrorDetail")).ConfigureAwait(true);
    }

    private async Task ShowMessageDialogAsync(string title, string message)
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
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync().AsTask().ConfigureAwait(true);
    }

    /// <summary>
    /// XamlRoot が利用可能になるまで待機します。
    /// WinUI 3 では OnActivated 直後に XamlRoot が null になる環境があるため、
    /// Loaded イベントまたは DispatcherQueue で待機してから ContentDialog を表示します。
    /// </summary>
    /// <returns>XamlRoot が利用可能になった場合は true、タイムアウトした場合は false。</returns>
    private async Task<bool> EnsureXamlRootAsync()
    {
        const int maxWaitMs = 3000;
        const int intervalMs = 50;

        if (RootGrid.XamlRoot is not null)
        {
            return true;
        }

        AppLog.Info("EnsureXamlRootAsync: XamlRoot is null, waiting for it to become available...");

        // RootGrid.Loaded を待つ（まだ Loaded されていない場合）
        var tcs = new TaskCompletionSource<bool>();
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= OnLoaded;
            tcs.TrySetResult(true);
        }

        RootGrid.Loaded += OnLoaded;

        // 既に Loaded 済みの場合や、イベントが発火しない場合に備えてポーリングも併用
        var elapsed = 0;
        while (RootGrid.XamlRoot is null && elapsed < maxWaitMs)
        {
            await Task.Delay(intervalMs).ConfigureAwait(true);
            elapsed += intervalMs;

            // Loaded イベントが発火していたら終了
            if (tcs.Task.IsCompleted)
            {
                break;
            }
        }

        RootGrid.Loaded -= OnLoaded;

        if (RootGrid.XamlRoot is not null)
        {
            AppLog.Info($"EnsureXamlRootAsync: XamlRoot became available after {elapsed}ms.");
            return true;
        }

        AppLog.Info($"EnsureXamlRootAsync: XamlRoot still null after {elapsed}ms, giving up.");
        return false;
    }

}
