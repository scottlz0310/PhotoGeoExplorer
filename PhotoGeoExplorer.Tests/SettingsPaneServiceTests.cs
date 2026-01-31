using System;
using System.IO;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// SettingsPaneService のテスト
/// </summary>
public class SettingsPaneServiceTests
{
    [Fact]
    public void ConstructorThrowsWhenSettingsServiceIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SettingsPaneService(null!));
    }

    [Fact]
    public async Task LoadSettingsAsyncReturnsSettings()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
        var settingsService = new SettingsService(tempPath);
        var service = new SettingsPaneService(settingsService);

        // Act
        var settings = await service.LoadSettingsAsync().ConfigureAwait(false);

        // Assert
        Assert.NotNull(settings);
    }

    [Fact]
    public async Task SaveSettingsAsyncThrowsWhenSettingsIsNull()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
        var settingsService = new SettingsService(tempPath);
        var service = new SettingsPaneService(settingsService);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SaveSettingsAsync(null!).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [Fact]
    public async Task SaveSettingsAsyncSavesSettings()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
        var settingsService = new SettingsService(tempPath);
        var service = new SettingsPaneService(settingsService);
        var settings = new AppSettings
        {
            Language = "ja-JP",
            Theme = ThemePreference.Dark
        };

        // Act
        await service.SaveSettingsAsync(settings).ConfigureAwait(false);

        // Assert
        var loaded = await service.LoadSettingsAsync().ConfigureAwait(false);
        Assert.Equal("ja-JP", loaded.Language);
        Assert.Equal(ThemePreference.Dark, loaded.Theme);

        // Cleanup
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void CreateDefaultSettingsReturnsNewSettings()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
        var settingsService = new SettingsService(tempPath);
        var service = new SettingsPaneService(settingsService);

        // Act
        var settings = service.CreateDefaultSettings();

        // Assert
        Assert.NotNull(settings);
        Assert.Null(settings.Language);
        Assert.Equal(ThemePreference.System, settings.Theme);
    }

    [Fact]
    public async Task ExportSettingsAsyncThrowsWhenSettingsIsNull()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
        var settingsService = new SettingsService(tempPath);
        var service = new SettingsPaneService(settingsService);
        var exportPath = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid()}.json");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.ExportSettingsAsync(null!, exportPath).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExportSettingsAsyncThrowsWhenFilePathIsEmpty()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
        var settingsService = new SettingsService(tempPath);
        var service = new SettingsPaneService(settingsService);
        var settings = new AppSettings();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.ExportSettingsAsync(settings, string.Empty).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [Fact]
    public async Task ImportSettingsAsyncThrowsWhenFilePathIsEmpty()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
        var settingsService = new SettingsService(tempPath);
        var service = new SettingsPaneService(settingsService);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.ImportSettingsAsync(string.Empty).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExportAndImportSettingsWorkTogether()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
        var exportPath = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid()}.json");
        var settingsService = new SettingsService(tempPath);
        var service = new SettingsPaneService(settingsService);
        var settings = new AppSettings
        {
            Language = "en-US",
            Theme = ThemePreference.Light,
            MapDefaultZoomLevel = 12
        };

        try
        {
            // Act
            await service.ExportSettingsAsync(settings, exportPath).ConfigureAwait(false);
            var imported = await service.ImportSettingsAsync(exportPath).ConfigureAwait(false);

            // Assert
            Assert.NotNull(imported);
            Assert.Equal("en-US", imported!.Language);
            Assert.Equal(ThemePreference.Light, imported.Theme);
            Assert.Equal(12, imported.MapDefaultZoomLevel);
        }
        finally
        {
            // Cleanup
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
