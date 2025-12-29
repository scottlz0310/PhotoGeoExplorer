using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Tests;

[TestClass]
public sealed class SettingsServiceIntegrationTests
{
    [TestMethod]
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
                Language = "en-US",
                Theme = ThemePreference.Light
            };

            await service.SaveAsync(settings).ConfigureAwait(true);
            var loaded = await service.LoadAsync().ConfigureAwait(true);

            Assert.AreEqual(settings.LastFolderPath, loaded.LastFolderPath);
            Assert.AreEqual(settings.ShowImagesOnly, loaded.ShowImagesOnly);
            Assert.AreEqual(settings.FileViewMode, loaded.FileViewMode);
            Assert.AreEqual(settings.Language, loaded.Language);
            Assert.AreEqual(settings.Theme, loaded.Theme);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [TestMethod]
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

            Assert.AreEqual("ja-JP", language);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [TestMethod]
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

            Assert.IsNull(language);
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
