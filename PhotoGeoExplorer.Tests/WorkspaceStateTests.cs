using PhotoGeoExplorer.State;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// WorkspaceState のテスト
/// </summary>
public class WorkspaceStateTests
{
    [Fact]
    public void CurrentFolderPath_DefaultsToNull()
    {
        // Arrange & Act
        var state = new WorkspaceState();

        // Assert
        Assert.Null(state.CurrentFolderPath);
    }

    [Fact]
    public void CurrentFolderPath_CanBeSet()
    {
        // Arrange
        var state = new WorkspaceState();
        const string testPath = @"C:\TestFolder";

        // Act
        state.CurrentFolderPath = testPath;

        // Assert
        Assert.Equal(testPath, state.CurrentFolderPath);
    }

    [Fact]
    public void CurrentFolderPath_RaisesPropertyChanged()
    {
        // Arrange
        var state = new WorkspaceState();
        var propertyChangedRaised = false;
        state.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceState.CurrentFolderPath))
            {
                propertyChangedRaised = true;
            }
        };

        // Act
        state.CurrentFolderPath = @"C:\TestFolder";

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void SelectedPhotoCount_DefaultsToZero()
    {
        // Arrange & Act
        var state = new WorkspaceState();

        // Assert
        Assert.Equal(0, state.SelectedPhotoCount);
    }

    [Fact]
    public void SelectedPhotoCount_CanBeSet()
    {
        // Arrange
        var state = new WorkspaceState();

        // Act
        state.SelectedPhotoCount = 5;

        // Assert
        Assert.Equal(5, state.SelectedPhotoCount);
    }

    [Fact]
    public void SelectedPhotoCount_RaisesPropertyChanged()
    {
        // Arrange
        var state = new WorkspaceState();
        var propertyChangedRaised = false;
        state.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceState.SelectedPhotoCount))
            {
                propertyChangedRaised = true;
            }
        };

        // Act
        state.SelectedPhotoCount = 10;

        // Assert
        Assert.True(propertyChangedRaised);
    }
}
