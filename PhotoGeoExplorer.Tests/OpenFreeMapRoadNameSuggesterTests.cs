using PhotoGeoExplorer.Services.RoadNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class OpenFreeMapRoadNameSuggesterTests
{
    [Fact]
    public void DefaultCacheDirectoryIsSeparateFromTheMapTileCache()
    {
        var directory = OpenFreeMapRoadNameSuggester.DefaultCacheDirectory;

        Assert.EndsWith(Path.Combine("PhotoGeoExplorer", "Cache", "RoadTiles"), directory, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "PhotoGeoExplorer/1.0 (contact)")]
    [InlineData(" ", "PhotoGeoExplorer/1.0 (contact)")]
    [InlineData("cache", "")]
    [InlineData("cache", " ")]
    public void ConstructionRejectsABlankCacheDirectoryOrUserAgent(string cacheDirectory, string userAgent)
    {
        Assert.ThrowsAny<ArgumentException>(() => new OpenFreeMapRoadNameSuggester(cacheDirectory, userAgent));
    }

    [Fact]
    public async Task ACoordinateThatCannotBeProjectedNeverReachesTheNetwork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pge-road-suggester-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var suggester = new OpenFreeMapRoadNameSuggester(directory, "PhotoGeoExplorer.Tests/1.0 (test)");

            var candidates = await suggester.SuggestAsync(90d, 0d, CancellationToken.None).ConfigureAwait(true);

            Assert.Empty(candidates);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void DisposeCanBeCalledTwice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pge-road-suggester-" + Guid.NewGuid().ToString("N"));
        try
        {
            var suggester = new OpenFreeMapRoadNameSuggester(directory, "PhotoGeoExplorer.Tests/1.0 (test)");

            suggester.Dispose();
            suggester.Dispose();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
