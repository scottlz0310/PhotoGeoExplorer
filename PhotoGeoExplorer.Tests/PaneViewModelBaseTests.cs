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
    public void TitleCanBeSet()
    {
        // Arrange & Act
        var vm = new TestPaneViewModel();

        // Assert
        Assert.Equal("Test Pane", vm.Title);
    }

    [Fact]
    public void IsActiveDefaultsToFalse()
    {
        // Arrange & Act
        var vm = new TestPaneViewModel();

        // Assert
        Assert.False(vm.IsActive);
    }

    [Fact]
    public void IsActiveCanBeSet()
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
    public async Task InitializeAsyncCallsOnInitializeAsync()
    {
        // Arrange
        var vm = new TestPaneViewModel();

        // Act
        await vm.InitializeAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(1, vm.InitializeCallCount);
    }

    [Fact]
    public async Task InitializeAsyncOnlyInitializesOnce()
    {
        // Arrange
        var vm = new TestPaneViewModel();

        // Act
        await vm.InitializeAsync().ConfigureAwait(true);
        await vm.InitializeAsync().ConfigureAwait(true);
        await vm.InitializeAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(1, vm.InitializeCallCount);
    }

    [Fact]
    public void CleanupCallsOnCleanup()
    {
        // Arrange
        var vm = new TestPaneViewModel();

        // Act
        vm.Cleanup();

        // Assert
        Assert.Equal(1, vm.CleanupCallCount);
    }

    [Fact]
    public async Task CleanupResetsInitializedState()
    {
        // Arrange
        var vm = new TestPaneViewModel();
        await vm.InitializeAsync().ConfigureAwait(true);

        // Act
        vm.Cleanup();
        await vm.InitializeAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(2, vm.InitializeCallCount);
    }
}
