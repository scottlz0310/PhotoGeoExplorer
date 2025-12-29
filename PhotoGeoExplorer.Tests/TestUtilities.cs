using System.Globalization;

namespace PhotoGeoExplorer.Tests;

internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly CultureInfo _originalUiCulture;

    public CultureScope(CultureInfo culture)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }
}

internal static class TestEnvironment
{
    public static void SkipIfCi(string reason)
    {
        if (IsCi)
        {
            Assert.Inconclusive(reason);
        }
    }

    private static bool IsCi
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
           || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
}
