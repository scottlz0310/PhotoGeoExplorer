using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed partial class MainWindow : Window
{
    private bool _mapInitialized;
    private WebView2? _mapWebView;
    private bool _windowSized;

    public MainWindow()
    {
        InitializeComponent();
        AppLog.Info("MainWindow constructed.");
        Activated += OnActivated;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_mapInitialized)
        {
            return;
        }

        EnsureWindowSize();
        _mapInitialized = true;
        AppLog.Info("MainWindow activated.");
        await InitializeMapAsync().ConfigureAwait(false);
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

    private async Task InitializeMapAsync()
    {
        if (_mapWebView is not null)
        {
            return;
        }

        try
        {
            var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            if (!File.Exists(indexPath))
            {
                AppLog.Error($"Map page not found: {indexPath}");
                ShowMapStatus("Map page missing. See log.");
                return;
            }

            AppLog.Info("Initializing WebView2.");
            var webView = new WebView2();
            MapHost.Children.Clear();
            MapHost.Children.Add(webView);
            _mapWebView = webView;
            await webView.EnsureCoreWebView2Async();
            webView.Source = new Uri(indexPath);
            MapStatusText.Visibility = Visibility.Collapsed;
            AppLog.Info("WebView2 initialized.");
        }
        catch (TypeLoadException ex)
        {
            AppLog.Error("Map WebView2 type load failed.", ex);
            ShowMapStatus("WebView2 unavailable. See log.");
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
        catch (IOException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
        catch (UriFormatException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
    }

    private void ShowMapStatus(string message)
    {
        MapStatusText.Text = message;
        MapStatusText.Visibility = Visibility.Visible;
    }
}
