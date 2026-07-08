using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
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
using WinRT.Interop;

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
    private readonly CrashReportDialogService _crashReportDialogService;
    private readonly UpdateCheckDialogService _updateCheckDialogService;
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
            MapPaneControl.ExifLocationPicker,
            (message, severity) => _viewModel.ShowNotificationMessage(message, severity));
        // MainWindow のコンストラクタは UI スレッドで実行されるため、
        // ここで生成した UiDispatcher が UI スレッドの DispatcherQueue を捕捉する
        _fileBrowserPaneViewModel = new FileBrowserPaneViewModel(
            new FileBrowserPaneService(),
            _viewModel.WorkspaceState,
            exifEditorService,
            dialogService,
            uiDispatcher: new UiDispatcher());
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
        _crashReportDialogService = new CrashReportDialogService(
            dialogService,
            () => _crashReportService,
            () => _viewModel.CrashReportsDirectoryPath,
            (message, severity) => _viewModel.ShowNotificationMessage(message, severity));
        _updateCheckDialogService = new UpdateCheckDialogService(ShowMessageDialogAsync);
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
            if (_crashReportService?.HasReportableCrash == true)
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

    /// <summary>
    /// 別プロセスから RedirectActivationToAsync 経由でファイルが指定された際、
    /// 既存ウィンドウ側でフォルダ遷移・該当ファイル選択を行います。
    /// </summary>
    internal async Task NavigateToFileAsync(string filePath)
    {
        _startupCoordinator.SetStartupFilePath(filePath);
        await _startupCoordinator.ApplyStartupAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// ウィンドウを前面化します。単一インスタンス化で別プロセスからのアクティベーションを
    /// リダイレクトされた際、既存ウィンドウをユーザーに確実に見せるために使用します。
    /// </summary>
    internal void BringToForeground()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, ShowWindowRestore);
            SetForegroundWindow(hwnd);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to bring window to foreground.", ex);
        }
    }

    private const int ShowWindowRestore = 9;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
        await _crashReportDialogService.ShowCrashReportDialogAsync().ConfigureAwait(true);
    }

    private async void OnOpenLogFolderClicked(object sender, RoutedEventArgs e)
    {
        await _crashReportDialogService.OpenLogFolderAsync().ConfigureAwait(true);
    }

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        await _updateCheckDialogService.CheckForUpdatesAsync().ConfigureAwait(true);
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
