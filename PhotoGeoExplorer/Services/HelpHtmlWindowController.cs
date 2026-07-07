using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// HTML ヘルプウィンドウ（WebView2）のライフサイクル・ナビゲーション制御を担う
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class HelpHtmlWindowController : IDisposable
{
    private readonly Func<string?> _getExternalContentBaseUrl;
    private readonly Action<string, InfoBarSeverity> _showNotification;
    private bool _disposed;
    private Window? _window;
    private WebView2? _webView;
    private Uri? _localFallbackUri;
    private Uri? _externalBaseUri;

    public HelpHtmlWindowController(
        Func<string?> getExternalContentBaseUrl,
        Action<string, InfoBarSeverity> showNotification)
    {
        _getExternalContentBaseUrl = getExternalContentBaseUrl ?? throw new ArgumentNullException(nameof(getExternalContentBaseUrl));
        _showNotification = showNotification ?? throw new ArgumentNullException(nameof(showNotification));
    }

    public void ShowOrNavigate(Uri uri, Uri? localFallbackUri, Uri? externalBaseUri, string windowTitle)
    {
        _localFallbackUri = localFallbackUri;
        _externalBaseUri = externalBaseUri;

        if (_window is not null)
        {
            _window.Title = windowTitle;
            if (_webView is not null)
            {
                _webView.Source = uri;
            }

            _window.Activate();
            return;
        }

        var webView = CreateHelpHtmlWebView(uri);
        _webView = webView;

        var container = new Grid();
        container.Children.Add(webView);

        var window = new Window
        {
            Title = windowTitle,
            Content = container
        };
        window.Closed += (_, _) => CleanupHelpHtmlWindow();

        _window = window;
        window.Activate();
        TryResizeHelpWindow(window, 980, 720);
    }

    public void Close()
    {
        CloseHelpHtmlWindow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();
        GC.SuppressFinalize(this);
    }

    private WebView2 CreateHelpHtmlWebView(Uri uri)
    {
        var webView = new WebView2
        {
            Source = uri,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        webView.NavigationStarting += OnHelpWebViewNavigationStarting;
        webView.NavigationCompleted += OnHelpWebViewNavigationCompleted;
        webView.CoreWebView2Initialized += OnHelpWebViewInitialized;
        return webView;
    }

    private void CleanupHelpHtmlWindow()
    {
        CloseHelpHtmlWebView();
        _window = null;
    }

    private void CloseHelpHtmlWindow()
    {
        if (_window is null)
        {
            CleanupHelpHtmlWindow();
            return;
        }

        try
        {
            _window.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException)
        {
            AppLog.Error("Failed to close help window.", ex);
            CleanupHelpHtmlWindow();
        }
    }

    private void CloseHelpHtmlWebView()
    {
        if (_webView is null)
        {
            return;
        }

        try
        {
            _webView.NavigationStarting -= OnHelpWebViewNavigationStarting;
            _webView.NavigationCompleted -= OnHelpWebViewNavigationCompleted;
            _webView.CoreWebView2Initialized -= OnHelpWebViewInitialized;
            if (_webView.CoreWebView2 is not null)
            {
                _webView.CoreWebView2.NewWindowRequested -= OnHelpWebViewNewWindowRequested;
            }

            _webView.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            AppLog.Error("Failed to close help WebView2.", ex);
        }
        finally
        {
            _webView = null;
            _localFallbackUri = null;
            _externalBaseUri = null;
        }
    }

    private void OnHelpWebViewInitialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (args.Exception is not null)
        {
            AppLog.Error("Help WebView2 initialization failed.", args.Exception);
            return;
        }

        sender.CoreWebView2.NewWindowRequested += OnHelpWebViewNewWindowRequested;
    }

    private void OnHelpWebViewNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (TryGetExternalUri(args.Uri, out var uri) && uri is not null)
        {
            if (ShouldOpenOutsideHelpWindow(uri))
            {
                args.Handled = true;
                _ = OpenExternalUriAsync(ResolveExternalUri(uri));
                return;
            }

            args.Handled = true;
            if (_webView is not null)
            {
                _webView.Source = uri;
            }
        }
    }

    private void OnHelpWebViewNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (TryGetExternalUri(args.Uri, out var uri) && uri is not null)
        {
            if (ShouldOpenOutsideHelpWindow(uri))
            {
                args.Cancel = true;
                _ = OpenExternalUriAsync(ResolveExternalUri(uri));
            }
        }
    }

    private void OnHelpWebViewNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess || _externalBaseUri is null || _localFallbackUri is null)
        {
            return;
        }

        if (sender.Source is null || AreSameUri(sender.Source, _localFallbackUri))
        {
            return;
        }

        AppLog.Info("External help page load failed. Falling back to local help content.");
        _externalBaseUri = null;
        sender.Source = _localFallbackUri;
    }

    private bool ShouldOpenOutsideHelpWindow(Uri uri)
    {
        if (_externalBaseUri is null)
        {
            return true;
        }

        return !AreSameOrigin(uri, _externalBaseUri);
    }

    private static bool AreSameOrigin(Uri left, Uri right)
    {
        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port;
    }

    private static bool AreSameUri(Uri left, Uri right)
    {
        return string.Equals(left.AbsoluteUri, right.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    private Uri ResolveExternalUri(Uri uri)
    {
        if (uri.AbsolutePath.Contains("privacy-policy", StringComparison.OrdinalIgnoreCase))
        {
            return HelpService.GetExternalPrivacyPolicyUri(_getExternalContentBaseUrl()) ?? uri;
        }

        return uri;
    }

    private static bool TryGetExternalUri(string? uriString, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(uriString))
        {
            return false;
        }

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        return false;
    }

    private async Task OpenExternalUriAsync(Uri uri)
    {
        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException
            or ArgumentException)
        {
            AppLog.Error("Failed to launch help link.", ex);
            _showNotification(
                LocalizationService.GetString("Message.LaunchBrowserFailed"),
                InfoBarSeverity.Error);
        }
    }

    private static void TryResizeHelpWindow(Window window, int width, int height)
    {
        try
        {
            var appWindow = GetAppWindow(window);
            appWindow.Resize(new SizeInt32(width, height));
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            AppLog.Error("Failed to resize help window.", ex);
        }
    }

    private static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }
}
