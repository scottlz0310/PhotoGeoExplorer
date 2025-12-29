using System;
using Xunit;

namespace PhotoGeoExplorer.E2E;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = "PHOTO_GEO_EXPLORER_RUN_E2E=1 が未設定のためスキップします。";
        }
    }

    private static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable("PHOTO_GEO_EXPLORER_RUN_E2E"),
            "1",
            StringComparison.OrdinalIgnoreCase);
}
