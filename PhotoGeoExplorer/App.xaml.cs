using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Windows.Globalization;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public partial class App : Application
{
    private Window? _window;
    private SplashWindow? _splashWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppLog.Info("App constructed.");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        AppLog.Info("App launched.");
        ApplyLanguageOverrideFromSettings();
        _splashWindow = new SplashWindow();
        _splashWindow.Activate();

        _window = new MainWindow();
        _window.Activated += OnMainWindowActivated;
        _window.Activate();
    }

    private void OnMainWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
    {
        if (_splashWindow is null)
        {
            return;
        }

        _splashWindow.Close();
        _splashWindow = null;

        if (_window is not null)
        {
            _window.Activated -= OnMainWindowActivated;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Error("UI thread unhandled exception.", e.Exception);
    }

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        AppLog.Error("AppDomain unhandled exception.", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Unobserved task exception.", e.Exception);
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
}
