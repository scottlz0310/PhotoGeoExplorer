using PhotoGeoExplorer.State;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// WorkspaceState のテスト
/// </summary>
public class WorkspaceStateTests
{
    [Fact]
    public void CurrentFolderPathDefaultsToNull()
    {
        // Arrange & Act
        var state = new WorkspaceState();

        // Assert
        Assert.Null(state.CurrentFolderPath);
    }

    [Fact]
    public void CurrentFolderPathCanBeSet()
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
    public void CurrentFolderPathRaisesPropertyChanged()
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
    public void SelectedPhotoCountDefaultsToZero()
    {
        // Arrange & Act
        var state = new WorkspaceState();

        // Assert
        Assert.Equal(0, state.SelectedPhotoCount);
    }

    [Fact]
    public void SelectedPhotoCountCanBeSet()
    {
        // Arrange
        var state = new WorkspaceState();

        // Act
        state.SelectedPhotoCount = 5;

        // Assert
        Assert.Equal(5, state.SelectedPhotoCount);
    }

    [Fact]
    public void SelectedPhotoCountRaisesPropertyChanged()
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

    [Fact]
    public void PhotoListCountDefaultsToZero()
    {
        // Arrange & Act
        var state = new WorkspaceState();

        // Assert
        Assert.Equal(0, state.PhotoListCount);
    }

    [Fact]
    public void PhotoListCountCanBeSet()
    {
        // Arrange
        var state = new WorkspaceState();

        // Act
        state.PhotoListCount = 100;

        // Assert
        Assert.Equal(100, state.PhotoListCount);
    }

    [Fact]
    public void CurrentPhotoIndexDefaultsToNegativeOne()
    {
        // Arrange & Act
        var state = new WorkspaceState();

        // Assert
        Assert.Equal(-1, state.CurrentPhotoIndex);
    }

    [Fact]
    public void CurrentPhotoIndexCanBeSet()
    {
        // Arrange
        var state = new WorkspaceState();

        // Act
        state.CurrentPhotoIndex = 5;

        // Assert
        Assert.Equal(5, state.CurrentPhotoIndex);
    }

    [Fact]
    public void CanSelectNextReturnsFalseWhenNoPhotoSelected()
    {
        // Arrange
        var state = new WorkspaceState
        {
            PhotoListCount = 10,
            CurrentPhotoIndex = -1
        };

        // Assert
        Assert.False(state.CanSelectNext);
    }

    [Fact]
    public void CanSelectNextReturnsTrueWhenNotAtLastPhoto()
    {
        // Arrange
        var state = new WorkspaceState
        {
            PhotoListCount = 10,
            CurrentPhotoIndex = 5
        };

        // Assert
        Assert.True(state.CanSelectNext);
    }

    [Fact]
    public void CanSelectNextReturnsFalseWhenAtLastPhoto()
    {
        // Arrange
        var state = new WorkspaceState
        {
            PhotoListCount = 10,
            CurrentPhotoIndex = 9
        };

        // Assert
        Assert.False(state.CanSelectNext);
    }

    [Fact]
    public void CanSelectPreviousReturnsFalseWhenAtFirstPhoto()
    {
        // Arrange
        var state = new WorkspaceState
        {
            PhotoListCount = 10,
            CurrentPhotoIndex = 0
        };

        // Assert
        Assert.False(state.CanSelectPrevious);
    }

    [Fact]
    public void CanSelectPreviousReturnsTrueWhenNotAtFirstPhoto()
    {
        // Arrange
        var state = new WorkspaceState
        {
            PhotoListCount = 10,
            CurrentPhotoIndex = 5
        };

        // Assert
        Assert.True(state.CanSelectPrevious);
    }

    [Fact]
    public void SelectNextInvokesAction()
    {
        // Arrange
        var state = new WorkspaceState();
        var actionInvoked = false;
        state.SelectNextAction = () => actionInvoked = true;

        // Act
        state.SelectNext();

        // Assert
        Assert.True(actionInvoked);
    }

    [Fact]
    public void SelectPreviousInvokesAction()
    {
        // Arrange
        var state = new WorkspaceState();
        var actionInvoked = false;
        state.SelectPreviousAction = () => actionInvoked = true;

        // Act
        state.SelectPrevious();

        // Assert
        Assert.True(actionInvoked);
    }

    [Fact]
    public void SelectNextDoesNotThrowWhenActionIsNull()
    {
        // Arrange
        var state = new WorkspaceState();

        // Act & Assert - should not throw
        state.SelectNext();
    }

    [Fact]
    public void SelectPreviousDoesNotThrowWhenActionIsNull()
    {
        // Arrange
        var state = new WorkspaceState();

        // Act & Assert - should not throw
        state.SelectPrevious();
    }
}
