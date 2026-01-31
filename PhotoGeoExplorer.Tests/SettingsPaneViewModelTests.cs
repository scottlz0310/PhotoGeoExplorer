using System.IO;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// SettingsPaneViewModel のテスト
/// </summary>
public class SettingsPaneViewModelTests
{
    private sealed class MockSettingsPaneService : SettingsPaneService
    {
        private AppSettings _settings = new();

        public MockSettingsPaneService()
            : base(new SettingsService(Path.Combine(Path.GetTempPath(), $"test-settings-{System.Guid.NewGuid()}.json")))
        {
        }

        public new Task<AppSettings> LoadSettingsAsync()
        {
            return Task.FromResult(_settings);
        }

        public new Task SaveSettingsAsync(AppSettings settings)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        public new AppSettings CreateDefaultSettings()
        {
            return new AppSettings();
        }
    }

    [Fact]
    public void ConstructorSetsTitle()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.Equal("Settings", vm.Title);
    }

    [Fact]
    public void LanguagePropertyDefaultValue()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.Null(vm.Language);
    }

    [Fact]
    public void LanguagePropertyCanBeSet()
    {
        // Arrange
        var service = new MockSettingsPaneService();
        var vm = new SettingsPaneViewModel(service);

        // Act
        vm.Language = "en-US";

        // Assert
        Assert.Equal("en-US", vm.Language);
    }

    [Fact]
    public void LanguagePropertyRaisesPropertyChanged()
    {
        // Arrange
        var service = new MockSettingsPaneService();
        var vm = new SettingsPaneViewModel(service);
        var propertyChangedRaised = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(SettingsPaneViewModel.Language))
            {
                propertyChangedRaised = true;
            }
        };

        // Act
        vm.Language = "ja-JP";

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void ThemePropertyDefaultValue()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.Equal(ThemePreference.System, vm.Theme);
    }

    [Fact]
    public void ThemePropertyCanBeSet()
    {
        // Arrange
        var service = new MockSettingsPaneService();
        var vm = new SettingsPaneViewModel(service);

        // Act
        vm.Theme = ThemePreference.Dark;

        // Assert
        Assert.Equal(ThemePreference.Dark, vm.Theme);
    }

    [Fact]
    public void ThemePropertyRaisesPropertyChanged()
    {
        // Arrange
        var service = new MockSettingsPaneService();
        var vm = new SettingsPaneViewModel(service);
        var propertyChangedRaised = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(SettingsPaneViewModel.Theme))
            {
                propertyChangedRaised = true;
            }
        };

        // Act
        vm.Theme = ThemePreference.Light;

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void MapDefaultZoomLevelPropertyDefaultValue()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.Equal(14, vm.MapDefaultZoomLevel);
    }

    [Fact]
    public void MapDefaultZoomLevelPropertyCanBeSet()
    {
        // Arrange
        var service = new MockSettingsPaneService();
        var vm = new SettingsPaneViewModel(service);

        // Act
        vm.MapDefaultZoomLevel = 12;

        // Assert
        Assert.Equal(12, vm.MapDefaultZoomLevel);
    }

    [Fact]
    public void IsDirtyIsFalseByDefault()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void SettingPropertyChangeMarksAsDirty()
    {
        // Arrange
        var service = new MockSettingsPaneService();
        var vm = new SettingsPaneViewModel(service);

        // Act
        vm.Language = "en-US";

        // Assert
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void CleanupDoesNotThrow()
    {
        // Arrange
        var service = new MockSettingsPaneService();
        var vm = new SettingsPaneViewModel(service);

        // Act & Assert (should not throw)
        vm.Cleanup();
    }

    [Fact]
    public void IsActiveCanBeToggled()
    {
        // Arrange
        var service = new MockSettingsPaneService();
        var vm = new SettingsPaneViewModel(service);

        // Act
        vm.IsActive = true;

        // Assert
        Assert.True(vm.IsActive);

        // Act
        vm.IsActive = false;

        // Assert
        Assert.False(vm.IsActive);
    }

    [Fact]
    public void SaveCommandIsNotNull()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.NotNull(vm.SaveCommand);
    }

    [Fact]
    public void ResetCommandIsNotNull()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.NotNull(vm.ResetCommand);
    }

    [Fact]
    public void ExportCommandIsNotNull()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.NotNull(vm.ExportCommand);
    }

    [Fact]
    public void ImportCommandIsNotNull()
    {
        // Arrange
        var service = new MockSettingsPaneService();

        // Act
        var vm = new SettingsPaneViewModel(service);

        // Assert
        Assert.NotNull(vm.ImportCommand);
    }
}
