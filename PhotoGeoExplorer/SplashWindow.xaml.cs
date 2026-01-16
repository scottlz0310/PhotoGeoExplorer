using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Services;
using WinRT.Interop;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed partial class SplashWindow : Window
{
    private const int SplashImageWidth = 620;
    private const int SplashImageHeight = 300;

    public SplashWindow()
    {
        InitializeComponent();
        Title = LocalizationService.GetString("SplashWindow.Title");
        ConfigureWindow();
        RootGrid.Loaded += OnRootGridLoaded;
    }

    private void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootGridLoaded;
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        var width = (int)Math.Round(SplashImageWidth * scale);
        var height = (int)Math.Round(SplashImageHeight * scale);
        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        if (appWindow.Presenter is OverlappedPresenter overlappedPresenter)
        {
            overlappedPresenter.IsAlwaysOnTop = true;
            overlappedPresenter.IsResizable = false;
            overlappedPresenter.IsMaximizable = false;
            overlappedPresenter.IsMinimizable = false;
        }
        else
        {
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            appWindow.SetPresenter(presenter);
        }

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            var workArea = displayArea.WorkArea;
            var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            appWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }

        var titleBar = appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        titleBar.ButtonPressedBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = Colors.Transparent;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ForegroundColor = Colors.Transparent;
        titleBar.InactiveForegroundColor = Colors.Transparent;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(RootGrid);
    }
}
