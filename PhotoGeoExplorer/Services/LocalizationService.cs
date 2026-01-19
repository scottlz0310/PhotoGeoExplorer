using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace PhotoGeoExplorer.Services;

internal static class LocalizationService
{
    private const string ResourcesPrefix = "Resources/";
    private const string LanguageQualifier = "Language";
    private static readonly object SyncLock = new();
    private static ResourceManager? _manager;
    private static ResourceContext? _context;
    private static string? _cachedLanguageOverride;
    private static bool _managerInitialized;

    public static string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var normalizedKey = NormalizeKey(key);
        try
        {
            var (manager, context) = GetManagerAndContext();
            if (manager is null || context is null)
            {
                return key;
            }

            var candidate = manager.MainResourceMap.TryGetValue($"{ResourcesPrefix}{normalizedKey}", context);
            if (candidate is null)
            {
                return key;
            }

            var value = candidate.ValueAsString;
            return string.IsNullOrWhiteSpace(value) ? key : value;
        }
        catch (COMException)
        {
            return key;
        }
    }

    public static string Format(string key, params object[] args)
    {
        var format = GetString(key);
        return string.Format(CultureInfo.CurrentCulture, format, args);
    }

    private static string NormalizeKey(string key)
    {
        return key.Replace('.', '/');
    }

    private static (ResourceManager? Manager, ResourceContext? Context) GetManagerAndContext()
    {
        lock (SyncLock)
        {
            var currentLanguageOverride = ApplicationLanguages.PrimaryLanguageOverride;

            if (!_managerInitialized)
            {
                _manager = CreateResourceManager();
                _managerInitialized = true;
            }

            if (_manager is null)
            {
                return (null, null);
            }

            if (_context is null || !string.Equals(_cachedLanguageOverride, currentLanguageOverride, StringComparison.Ordinal))
            {
                _context = CreateContext(_manager, currentLanguageOverride);
                _cachedLanguageOverride = currentLanguageOverride;
            }

            return (_manager, _context);
        }
    }

    private static ResourceManager? CreateResourceManager()
    {
        if (IsTestHost())
        {
            return null;
        }

        try
        {
            return new ResourceManager();
        }
        catch (COMException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static ResourceContext CreateContext(ResourceManager manager, string? languageOverride)
    {
        var context = manager.CreateResourceContext();
        if (!string.IsNullOrWhiteSpace(languageOverride))
        {
            try
            {
                context.QualifierValues[LanguageQualifier] = languageOverride;
            }
            catch (ArgumentException ex)
            {
                AppLog.Error($"Failed to set language qualifier: {languageOverride}", ex);
            }
        }

        return context;
    }

    private static bool IsTestHost()
    {
        if (IsCiEnvironment())
        {
            return true;
        }

        var name = AppDomain.CurrentDomain.FriendlyName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Contains("testhost", StringComparison.OrdinalIgnoreCase)
            || name.Contains("vstest", StringComparison.OrdinalIgnoreCase)
            || name.Contains("xunit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCiEnvironment()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }
}
