using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.Globalization;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace PhotoGeoExplorer.Services;

[ExcludeFromCodeCoverage]
internal sealed class HelpService : IHelpService
{
    private readonly IDialogService _dialogService;
    private readonly Func<string?> _getLanguageOverride;
    private readonly Func<string?> _getExternalContentBaseUrl;
    private readonly Func<bool> _getShowQuickStartOnStartup;
    private readonly Action<bool> _setShowQuickStartOnStartup;
    private readonly Func<Task> _saveQuickStartPreferenceAsync;
    private readonly bool _settingsFileExistsAtStartup;
    private readonly Action<string, InfoBarSeverity> _showNotification;
    private bool _disposed;
    private Window? _helpHtmlWindow;
    private WebView2? _helpHtmlWebView;
    private Uri? _helpHtmlLocalFallbackUri;
    private Uri? _helpHtmlExternalBaseUri;

    public HelpService(
        IDialogService dialogService,
        Func<string?> getLanguageOverride,
        Func<string?> getExternalContentBaseUrl,
        Func<bool> getShowQuickStartOnStartup,
        Action<bool> setShowQuickStartOnStartup,
        Func<Task> saveQuickStartPreferenceAsync,
        bool settingsFileExistsAtStartup,
        Action<string, InfoBarSeverity> showNotification)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _getLanguageOverride = getLanguageOverride ?? throw new ArgumentNullException(nameof(getLanguageOverride));
        _getExternalContentBaseUrl = getExternalContentBaseUrl ?? throw new ArgumentNullException(nameof(getExternalContentBaseUrl));
        _getShowQuickStartOnStartup = getShowQuickStartOnStartup ?? throw new ArgumentNullException(nameof(getShowQuickStartOnStartup));
        _setShowQuickStartOnStartup = setShowQuickStartOnStartup ?? throw new ArgumentNullException(nameof(setShowQuickStartOnStartup));
        _saveQuickStartPreferenceAsync = saveQuickStartPreferenceAsync ?? throw new ArgumentNullException(nameof(saveQuickStartPreferenceAsync));
        _settingsFileExistsAtStartup = settingsFileExistsAtStartup;
        _showNotification = showNotification ?? throw new ArgumentNullException(nameof(showNotification));
    }

    public Task ShowGettingStartedAsync()
    {
        return ShowHelpDialogAsync(
            "Dialog.Help.GettingStarted.Title",
            "Dialog.Help.GettingStarted.Detail",
            includeQuickStartToggle: true);
    }

    public Task ShowBasicsAsync()
    {
        return ShowHelpDialogAsync(
            "Dialog.Help.Basics.Title",
            "Dialog.Help.Basics.Detail");
    }

    public async Task ShowHelpHtmlWindowAsync()
    {
        var localFallbackUri = TryGetHelpHtmlUri();
        var externalUri = TryGetExternalHelpHtmlUri(
            _getExternalContentBaseUrl(),
            _getLanguageOverride(),
            ApplicationLanguages.Languages,
            CultureInfo.CurrentUICulture.Name);
        var uri = externalUri ?? localFallbackUri;
        if (uri is null)
        {
            await ShowHelpHtmlMissingDialogAsync().ConfigureAwait(true);
            return;
        }

        _helpHtmlLocalFallbackUri = localFallbackUri;
        _helpHtmlExternalBaseUri = externalUri is null
            ? null
            : GetExternalContentBaseUri(_getExternalContentBaseUrl());

        if (_helpHtmlWindow is not null)
        {
            if (_helpHtmlWebView is not null)
            {
                _helpHtmlWebView.Source = uri;
            }

            _helpHtmlWindow.Activate();
            return;
        }

        var webView = CreateHelpHtmlWebView(uri);
        _helpHtmlWebView = webView;

        var container = new Grid();
        container.Children.Add(webView);

        var window = new Window
        {
            Title = LocalizationService.GetString("Dialog.Help.Html.Title"),
            Content = container
        };
        window.Closed += (_, _) => CleanupHelpHtmlWindow();

        _helpHtmlWindow = window;
        window.Activate();
        TryResizeHelpWindow(window, 980, 720);
    }

    public async Task ShowAboutAsync()
    {
        var version = typeof(PhotoGeoExplorer.App).Assembly.GetName().Version?.ToString()
            ?? LocalizationService.GetString("Common.Unknown");

        await _dialogService.ShowMessageDialogAsync(
            LocalizationService.GetString("Dialog.About.Title"),
            LocalizationService.Format("Dialog.About.Detail", version)).ConfigureAwait(true);
    }

    public async Task ShowQuickStartIfNeededAsync()
    {
        if (_settingsFileExistsAtStartup && !_getShowQuickStartOnStartup())
        {
            return;
        }

        await ShowGettingStartedAsync().ConfigureAwait(true);
    }

    public void CloseHelpWindow()
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
        CloseHelpWindow();
        GC.SuppressFinalize(this);
    }

    internal static Uri? TryGetHelpHtmlUri(
        string baseDirectory,
        string? languageOverride,
        IReadOnlyList<string>? preferredLanguages,
        string currentUiCultureName)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(currentUiCultureName);

        var helpDirectory = Path.Combine(baseDirectory, "wwwroot", "help");
        var preferredFileName = GetHelpHtmlFileName(languageOverride, preferredLanguages, currentUiCultureName);
        var preferredPath = Path.Combine(helpDirectory, preferredFileName);
        if (File.Exists(preferredPath))
        {
            return new Uri(preferredPath);
        }

        var fallbackPath = Path.Combine(helpDirectory, "index.html");
        if (File.Exists(fallbackPath))
        {
            if (!string.Equals(preferredFileName, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Info($"Help HTML fallback to {fallbackPath}");
            }

            return new Uri(fallbackPath);
        }

        AppLog.Error($"Help HTML not found: {preferredPath}");
        return null;
    }

    internal static string GetHelpHtmlFileName(
        string? languageOverride,
        IReadOnlyList<string>? preferredLanguages,
        string currentUiCultureName)
    {
        ArgumentNullException.ThrowIfNull(currentUiCultureName);

        var language = languageOverride;
        if (string.IsNullOrWhiteSpace(language))
        {
            language = preferredLanguages is { Count: > 0 }
                ? preferredLanguages[0]
                : currentUiCultureName;
        }

        if (!string.IsNullOrWhiteSpace(language)
            && language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "index.en.html";
        }

        return "index.html";
    }

    internal static Uri? TryGetExternalHelpHtmlUri(
        string? externalContentBaseUrl,
        string? languageOverride,
        IReadOnlyList<string>? preferredLanguages,
        string currentUiCultureName)
    {
        ArgumentNullException.ThrowIfNull(currentUiCultureName);

        var baseUri = GetExternalContentBaseUri(externalContentBaseUrl);
        if (baseUri is null)
        {
            return null;
        }

        var fileName = GetHelpHtmlFileName(languageOverride, preferredLanguages, currentUiCultureName);
        return new Uri(baseUri, $"help/{fileName}");
    }

    internal static Uri? GetExternalPrivacyPolicyUri(string? externalContentBaseUrl)
    {
        var baseUri = GetExternalContentBaseUri(externalContentBaseUrl);
        return baseUri is null
            ? null
            : new Uri(baseUri, "privacy-policy");
    }

    private async Task ShowHelpDialogAsync(string titleKey, string detailKey, bool includeQuickStartToggle = false)
    {
        CheckBox? quickStartToggle = null;
        UIElement content = CreateHelpDialogContent(LocalizationService.GetString(detailKey));
        if (includeQuickStartToggle)
        {
            quickStartToggle = new CheckBox
            {
                Content = LocalizationService.GetString("Dialog.Help.QuickStartToggle"),
                IsChecked = _getShowQuickStartOnStartup()
            };

            var stack = new StackPanel
            {
                Spacing = 12
            };
            stack.Children.Add(content);
            stack.Children.Add(quickStartToggle);
            content = stack;
        }

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString(titleKey),
            Content = content,
            CloseButtonText = LocalizationService.GetString("Common.Ok")
        };

        var result = await _dialogService.ShowContentDialogAsync(dialog).ConfigureAwait(true);
        if (result is null)
        {
            AppLog.Info($"ShowHelpDialogAsync skipped because XamlRoot is unavailable: {titleKey}");
            return;
        }

        if (includeQuickStartToggle && quickStartToggle is not null)
        {
            _setShowQuickStartOnStartup(quickStartToggle.IsChecked ?? false);
            await _saveQuickStartPreferenceAsync().ConfigureAwait(true);
        }
    }

    private static ScrollViewer CreateHelpDialogContent(string message)
    {
        return new ScrollViewer
        {
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private Uri? TryGetHelpHtmlUri()
    {
        return TryGetHelpHtmlUri(
            AppContext.BaseDirectory,
            _getLanguageOverride(),
            ApplicationLanguages.Languages,
            CultureInfo.CurrentUICulture.Name);
    }

    private static Uri? GetExternalContentBaseUri(string? externalContentBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(externalContentBaseUrl))
        {
            return null;
        }

        var normalized = externalContentBaseUrl.Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        if (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return parsed;
        }

        return null;
    }

    private async Task ShowHelpHtmlMissingDialogAsync()
    {
        await _dialogService.ShowMessageDialogAsync(
            LocalizationService.GetString("Dialog.Help.HtmlMissing.Title"),
            LocalizationService.GetString("Dialog.Help.HtmlMissing.Detail")).ConfigureAwait(true);
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
        _helpHtmlWindow = null;
    }

    private void CloseHelpHtmlWindow()
    {
        if (_helpHtmlWindow is null)
        {
            CleanupHelpHtmlWindow();
            return;
        }

        try
        {
            _helpHtmlWindow.Close();
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
        if (_helpHtmlWebView is null)
        {
            return;
        }

        try
        {
            _helpHtmlWebView.NavigationStarting -= OnHelpWebViewNavigationStarting;
            _helpHtmlWebView.NavigationCompleted -= OnHelpWebViewNavigationCompleted;
            _helpHtmlWebView.CoreWebView2Initialized -= OnHelpWebViewInitialized;
            if (_helpHtmlWebView.CoreWebView2 is not null)
            {
                _helpHtmlWebView.CoreWebView2.NewWindowRequested -= OnHelpWebViewNewWindowRequested;
            }

            _helpHtmlWebView.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            AppLog.Error("Failed to close help WebView2.", ex);
        }
        finally
        {
            _helpHtmlWebView = null;
            _helpHtmlLocalFallbackUri = null;
            _helpHtmlExternalBaseUri = null;
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
            if (_helpHtmlWebView is not null)
            {
                _helpHtmlWebView.Source = uri;
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
        if (args.IsSuccess || _helpHtmlExternalBaseUri is null || _helpHtmlLocalFallbackUri is null)
        {
            return;
        }

        if (sender.Source is null || AreSameUri(sender.Source, _helpHtmlLocalFallbackUri))
        {
            return;
        }

        AppLog.Info("External help page load failed. Falling back to local help content.");
        _helpHtmlExternalBaseUri = null;
        sender.Source = _helpHtmlLocalFallbackUri;
    }

    private bool ShouldOpenOutsideHelpWindow(Uri uri)
    {
        if (_helpHtmlExternalBaseUri is null)
        {
            return true;
        }

        return !AreSameOrigin(uri, _helpHtmlExternalBaseUri);
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
            return GetExternalPrivacyPolicyUri(_getExternalContentBaseUrl()) ?? uri;
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
