using System.Threading.Tasks;
using PhotoGeoExplorer.Panes.Settings;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// SettingsPaneViewModel のテスト（サンプル実装）
/// </summary>
public class SettingsPaneViewModelTests
{
    [Fact]
    public void ConstructorSetsTitle()
    {
        // Arrange & Act
        var vm = new SettingsPaneViewModel();

        // Assert
        Assert.Equal("Settings", vm.Title);
    }

    [Fact]
    public void LanguageSettingDefaultValue()
    {
        // Arrange & Act
        var vm = new SettingsPaneViewModel();

        // Assert
        Assert.Equal("System Default", vm.LanguageSetting);
    }

    [Fact]
    public void LanguageSettingCanBeSet()
    {
        // Arrange
        var vm = new SettingsPaneViewModel();

        // Act
        vm.LanguageSetting = "English";

        // Assert
        Assert.Equal("English", vm.LanguageSetting);
    }

    [Fact]
    public void LanguageSettingRaisesPropertyChanged()
    {
        // Arrange
        var vm = new SettingsPaneViewModel();
        var propertyChangedRaised = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(SettingsPaneViewModel.LanguageSetting))
            {
                propertyChangedRaised = true;
            }
        };

        // Act
        vm.LanguageSetting = "Japanese";

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void ThemeSettingDefaultValue()
    {
        // Arrange & Act
        var vm = new SettingsPaneViewModel();

        // Assert
        Assert.Equal("System Default", vm.ThemeSetting);
    }

    [Fact]
    public void ThemeSettingCanBeSet()
    {
        // Arrange
        var vm = new SettingsPaneViewModel();

        // Act
        vm.ThemeSetting = "Dark";

        // Assert
        Assert.Equal("Dark", vm.ThemeSetting);
    }

    [Fact]
    public void ThemeSettingRaisesPropertyChanged()
    {
        // Arrange
        var vm = new SettingsPaneViewModel();
        var propertyChangedRaised = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(SettingsPaneViewModel.ThemeSetting))
            {
                propertyChangedRaised = true;
            }
        };

        // Act
        vm.ThemeSetting = "Light";

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public async Task InitializeAsyncSucceeds()
    {
        // Arrange
        var vm = new SettingsPaneViewModel();

        // Act & Assert (should not throw)
        await vm.InitializeAsync().ConfigureAwait(true);
    }

    [Fact]
    public void CleanupDoesNotThrow()
    {
        // Arrange
        var vm = new SettingsPaneViewModel();

        // Act & Assert (should not throw)
        vm.Cleanup();
    }

    [Fact]
    public void IsActiveCanBeToggled()
    {
        // Arrange
        var vm = new SettingsPaneViewModel();

        // Act
        vm.IsActive = true;

        // Assert
        Assert.True(vm.IsActive);

        // Act
        vm.IsActive = false;

        // Assert
        Assert.False(vm.IsActive);
    }
}
