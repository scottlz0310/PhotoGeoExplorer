using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Globalization;

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
    private readonly HelpHtmlWindowController _helpHtmlWindowController;
    private bool _disposed;

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
        ArgumentNullException.ThrowIfNull(showNotification);
        _helpHtmlWindowController = new HelpHtmlWindowController(_getExternalContentBaseUrl, showNotification);
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

        var externalBaseUri = externalUri is null
            ? null
            : GetExternalContentBaseUri(_getExternalContentBaseUrl());

        _helpHtmlWindowController.ShowOrNavigate(
            uri,
            localFallbackUri,
            externalBaseUri,
            LocalizationService.GetString("Dialog.Help.Html.Title"));
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
        _helpHtmlWindowController.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _helpHtmlWindowController.Dispose();
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
}
