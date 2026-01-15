using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.Resources;

namespace PhotoGeoExplorer.Services;

internal static class LocalizationService
{
    private static readonly Lazy<ResourceLoader?> Loader = new(CreateLoader);

    public static string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var normalizedKey = NormalizeKey(key);
        try
        {
            var loader = Loader.Value;
            if (loader is null)
            {
                return key;
            }

            var value = loader.GetString(normalizedKey);
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

    private static ResourceLoader? CreateLoader()
    {
        if (IsTestHost())
        {
            return null;
        }

        try
        {
            return new ResourceLoader();
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
