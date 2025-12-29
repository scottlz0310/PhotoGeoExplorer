using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Tests;

[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public async Task ExportImportRoundTripsSettings()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "settings.json");
        try
        {
            var settings = new AppSettings
            {
                LastFolderPath = "C:\\Photos",
                ShowImagesOnly = false,
                FileViewMode = FileViewMode.Icon,
                Language = "en-US",
                Theme = ThemePreference.Dark
            };

            await SettingsService.ExportAsync(settings, path).ConfigureAwait(true);
            var imported = await SettingsService.ImportAsync(path).ConfigureAwait(true);

            Assert.IsNotNull(imported);
            Assert.AreEqual(settings.LastFolderPath, imported!.LastFolderPath);
            Assert.AreEqual(settings.ShowImagesOnly, imported.ShowImagesOnly);
            Assert.AreEqual(settings.FileViewMode, imported.FileViewMode);
            Assert.AreEqual(settings.Language, imported.Language);
            Assert.AreEqual(settings.Theme, imported.Theme);
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
