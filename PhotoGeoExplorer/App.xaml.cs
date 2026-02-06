using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Windows.Globalization;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Services;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public partial class App : Application
{
    private const int MinimumSplashDurationMs = 2000;
    private Window? _window;
    private SplashWindow? _splashWindow;
    private string? _startupFilePath;
    private DateTimeOffset _splashShownAt;
    private bool _splashCloseRequested;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppLog.Info("App constructed.");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        AppLog.Info("App launched.");
        ApplyLanguageOverrideFromSettings();
        _startupFilePath = GetFileActivationPath();
        if (!IsPackaged())
        {
            _splashWindow = new SplashWindow();
            _splashWindow.Activate();
            _splashShownAt = DateTimeOffset.UtcNow;
        }

        var mainWindow = new MainWindow();
        _window = mainWindow;
        if (!string.IsNullOrWhiteSpace(_startupFilePath))
        {
            mainWindow.SetStartupFilePath(_startupFilePath);
        }
        _window.Activated += OnMainWindowActivated;
        _window.Activate();
    }

    private void OnMainWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
    {
        if (_window is not null)
        {
            _window.Activated -= OnMainWindowActivated;
        }

        if (_splashWindow is not null)
        {
            _ = CloseSplashAsync();
        }
    }

    private async Task CloseSplashAsync()
    {
        if (_splashCloseRequested)
        {
            return;
        }

        _splashCloseRequested = true;
        var splashWindow = _splashWindow;
        if (splashWindow is null)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _splashShownAt;
        var remaining = TimeSpan.FromMilliseconds(MinimumSplashDurationMs) - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining).ConfigureAwait(false);
        }

        splashWindow.DispatcherQueue.TryEnqueue(() =>
        {
            splashWindow.Close();
        });
        _splashWindow = null;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var exceptionInfo = $"Type: {e.Exception?.GetType().FullName ?? "Unknown"}, Message: {e.Exception?.Message ?? "Unknown"}";
        AppLog.Error($"UI thread unhandled exception. {exceptionInfo}", e.Exception);
    }

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        var exceptionInfo = $"IsTerminating: {e.IsTerminating}, Type: {exception?.GetType().FullName ?? "Unknown"}, Message: {exception?.Message ?? "Unknown"}";
        AppLog.Error($"AppDomain unhandled exception. {exceptionInfo}", exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var exceptionInfo = $"Type: {e.Exception?.GetType().FullName ?? "Unknown"}, InnerExceptions: {e.Exception?.InnerExceptions.Count ?? 0}";
        AppLog.Error($"Unobserved task exception. {exceptionInfo}", e.Exception);
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        AppLog.Info("ProcessExit event received.");
    }

    private static void ApplyLanguageOverrideFromSettings()
    {
        var settingsService = new SettingsService();
        var languageOverride = settingsService.LoadLanguageOverride();
        if (string.IsNullOrWhiteSpace(languageOverride))
        {
            return;
        }

        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageOverride;
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to apply language override.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to apply language override.", ex);
        }
    }

    private static string? GetFileActivationPath()
    {
        AppActivationArguments activationArgs;
        try
        {
            activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to get activation arguments.", ex);
            return null;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to get activation arguments.", ex);
            return null;
        }

        if (activationArgs.Kind != ExtendedActivationKind.File)
        {
            return null;
        }

        if (activationArgs.Data is not FileActivatedEventArgs fileArgs)
        {
            return null;
        }

        var files = fileArgs.Files.OfType<StorageFile>().ToList();
        if (files.Count == 0)
        {
            return null;
        }

        if (files.Count > 1)
        {
            AppLog.Info($"File activation received {files.Count} items. Using the first file.");
        }

        return files[0].Path;
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
