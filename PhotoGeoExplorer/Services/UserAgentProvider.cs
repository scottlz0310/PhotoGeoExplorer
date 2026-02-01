using System;
using System.Reflection;

namespace PhotoGeoExplorer.Services;

internal static class UserAgentProvider
{
    private const string Contact = "scott.lz0310@gmail.com";

    internal static string UserAgent => $"PhotoGeoExplorer/{GetVersion()} ({Contact})";

    private static string GetVersion()
    {
        var assembly = typeof(UserAgentProvider).Assembly;
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            var trimmed = infoVersion;
            var plusIndex = trimmed.IndexOf('+', StringComparison.Ordinal);
            if (plusIndex > 0)
            {
                trimmed = trimmed[..plusIndex];
            }

            return trimmed;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
