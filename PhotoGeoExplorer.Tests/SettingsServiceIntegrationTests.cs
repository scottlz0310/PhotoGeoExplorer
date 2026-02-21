using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Tests;

public sealed class SettingsServiceIntegrationTests
{
    [Fact]
    public async Task SaveLoadRoundTripsSettings()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var service = new SettingsService(path);
            var settings = new AppSettings
            {
                LastFolderPath = "C:\\Photos",
                ShowImagesOnly = false,
                FileViewMode = FileViewMode.List,
                ShowDetailsModifiedColumn = false,
                ShowDetailsResolutionColumn = false,
                ShowDetailsSizeColumn = false,
                ShowDetailsTakenAtColumn = true,
                ShowDetailsLocationColumn = true,
                Language = "en-US",
                Theme = ThemePreference.Light,
                MapDefaultZoomLevel = 12,
                PaneLayoutPreset = PaneLayoutPreset.LeftSplitAndRight,
                PaneRegion1View = PaneViewType.Map,
                PaneRegion2View = PaneViewType.File,
                PaneRegion3View = PaneViewType.Preview
            };

            await service.SaveAsync(settings).ConfigureAwait(true);
            var loaded = await service.LoadAsync().ConfigureAwait(true);

            Assert.Equal(settings.LastFolderPath, loaded.LastFolderPath);
            Assert.Equal(settings.ShowImagesOnly, loaded.ShowImagesOnly);
            Assert.Equal(settings.FileViewMode, loaded.FileViewMode);
            Assert.Equal(settings.ShowDetailsModifiedColumn, loaded.ShowDetailsModifiedColumn);
            Assert.Equal(settings.ShowDetailsResolutionColumn, loaded.ShowDetailsResolutionColumn);
            Assert.Equal(settings.ShowDetailsSizeColumn, loaded.ShowDetailsSizeColumn);
            Assert.Equal(settings.ShowDetailsTakenAtColumn, loaded.ShowDetailsTakenAtColumn);
            Assert.Equal(settings.ShowDetailsLocationColumn, loaded.ShowDetailsLocationColumn);
            Assert.Equal(settings.Language, loaded.Language);
            Assert.Equal(settings.Theme, loaded.Theme);
            Assert.Equal(settings.MapDefaultZoomLevel, loaded.MapDefaultZoomLevel);
            Assert.Equal(settings.PaneLayoutPreset, loaded.PaneLayoutPreset);
            Assert.Equal(settings.PaneRegion1View, loaded.PaneRegion1View);
            Assert.Equal(settings.PaneRegion2View, loaded.PaneRegion2View);
            Assert.Equal(settings.PaneRegion3View, loaded.PaneRegion3View);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LoadLanguageOverrideNormalizesTags()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var service = new SettingsService(path);
            var settings = new AppSettings
            {
                Language = "ja"
            };

            await service.SaveAsync(settings).ConfigureAwait(true);

            var language = service.LoadLanguageOverride();

            Assert.Equal("ja-JP", language);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LoadLanguageOverrideReturnsNullForSystem()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var service = new SettingsService(path);
            var settings = new AppSettings
            {
                Language = "system"
            };

            await service.SaveAsync(settings).ConfigureAwait(true);

            var language = service.LoadLanguageOverride();

            Assert.Null(language);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
