using System.Threading.Tasks;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// PaneViewModelBase のテスト
/// </summary>
public class PaneViewModelBaseTests
{
    private sealed class TestPaneViewModel : PaneViewModelBase
    {
        public int InitializeCallCount { get; private set; }
        public int CleanupCallCount { get; private set; }
        public int ActiveChangedCallCount { get; private set; }

        public TestPaneViewModel()
        {
            Title = "Test Pane";
        }

        protected override Task OnInitializeAsync()
        {
            InitializeCallCount++;
            return Task.CompletedTask;
        }

        protected override void OnCleanup()
        {
            CleanupCallCount++;
        }

        protected override void OnActiveChanged()
        {
            ActiveChangedCallCount++;
        }
    }

    [Fact]
    public void Title_CanBeSet()
    {
        // Arrange & Act
        var vm = new TestPaneViewModel();

        // Assert
        Assert.Equal("Test Pane", vm.Title);
    }

    [Fact]
    public void IsActive_DefaultsToFalse()
    {
        // Arrange & Act
        var vm = new TestPaneViewModel();

        // Assert
        Assert.False(vm.IsActive);
    }

    [Fact]
    public void IsActive_CanBeSet()
    {
        // Arrange
        var vm = new TestPaneViewModel();

        // Act
        vm.IsActive = true;

        // Assert
        Assert.True(vm.IsActive);
        Assert.Equal(1, vm.ActiveChangedCallCount);
    }

    [Fact]
    public async Task InitializeAsync_CallsOnInitializeAsync()
    {
        // Arrange
        var vm = new TestPaneViewModel();

        // Act
        await vm.InitializeAsync();

        // Assert
        Assert.Equal(1, vm.InitializeCallCount);
    }

    [Fact]
    public async Task InitializeAsync_OnlyInitializesOnce()
    {
        // Arrange
        var vm = new TestPaneViewModel();

        // Act
        await vm.InitializeAsync();
        await vm.InitializeAsync();
        await vm.InitializeAsync();

        // Assert
        Assert.Equal(1, vm.InitializeCallCount);
    }

    [Fact]
    public void Cleanup_CallsOnCleanup()
    {
        // Arrange
        var vm = new TestPaneViewModel();

        // Act
        vm.Cleanup();

        // Assert
        Assert.Equal(1, vm.CleanupCallCount);
    }

    [Fact]
    public async Task Cleanup_ResetsInitializedState()
    {
        // Arrange
        var vm = new TestPaneViewModel();
        await vm.InitializeAsync();

        // Act
        vm.Cleanup();
        await vm.InitializeAsync();

        // Assert
        Assert.Equal(2, vm.InitializeCallCount);
    }
}
