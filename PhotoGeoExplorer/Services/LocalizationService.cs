using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.Resources;

namespace PhotoGeoExplorer.Services;

internal static class LocalizationService
{
    private static readonly ResourceLoader Loader = new();

    public static string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var normalizedKey = NormalizeKey(key);
        try
        {
            var value = Loader.GetString(normalizedKey);
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
}
