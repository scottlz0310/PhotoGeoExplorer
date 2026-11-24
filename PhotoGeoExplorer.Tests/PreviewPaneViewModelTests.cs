using System;
using PhotoGeoExplorer.Panes.Preview;
using PhotoGeoExplorer.Services.PanZoom;
using PhotoGeoExplorer.State;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// PreviewPaneViewModel のテスト
/// </summary>
public class PreviewPaneViewModelTests
{
    private sealed class MockPreviewPaneService : IPreviewPaneService
    {
        public System.Threading.Tasks.Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> LoadImageAsync(string filePath)
        {
            return System.Threading.Tasks.Task.FromResult<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>(null);
        }

        public System.Threading.Tasks.Task<PhotoGeoExplorer.Models.PhotoMetadata?> GetMetadataAsync(
            string filePath,
            System.Threading.CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult<PhotoGeoExplorer.Models.PhotoMetadata?>(null);
    }

    [Fact]
    public void ConstructorSetsTitle()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        var vm = new PreviewPaneViewModel(service, workspaceState);

        // Assert
        Assert.Equal("Preview", vm.Title);
    }

    [Fact]
    public void CurrentImageDefaultValue()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        var vm = new PreviewPaneViewModel(service, workspaceState);

        // Assert
        Assert.Null(vm.CurrentImage);
    }

    [Fact]
    public void PlaceholderVisibilityDefaultValue()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        var vm = new PreviewPaneViewModel(service, workspaceState);

        // Assert
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, vm.PlaceholderVisibility);
    }

    private const double Precision = 9;

    private static PreviewPaneViewModel CreateLaidOut(
        double viewportWidth = 800,
        double viewportHeight = 600,
        double imageWidth = 800,
        double imageHeight = 600)
    {
        var vm = new PreviewPaneViewModel(new MockPreviewPaneService(), new WorkspaceState());
        vm.OnViewportSizeChanged(viewportWidth, viewportHeight);
        vm.OnImageOpened(imageWidth, imageHeight);
        return vm;
    }

    [Fact]
    public void PanZoomStartsFitted()
    {
        var vm = new PreviewPaneViewModel(new MockPreviewPaneService(), new WorkspaceState());

        Assert.Equal(PanZoomTransform.Identity, vm.PanZoom.Transform);
    }

    [Fact]
    public void ZoomInCommandMagnifiesAroundTheViewportCenter()
    {
        var vm = CreateLaidOut();
        var center = new PanZoomPoint(400, 300);
        var contentBefore = vm.PanZoom.Transform.ToContent(center);

        vm.ZoomInCommand.Execute(null);

        Assert.Equal(1.2, vm.PanZoom.Transform.Scale, Precision);
        var viewAfter = vm.PanZoom.Transform.ToView(contentBefore);
        Assert.Equal(center.X, viewAfter.X, Precision);
        Assert.Equal(center.Y, viewAfter.Y, Precision);
    }

    [Fact]
    public void ZoomOutCommandReversesZoomInAndStopsAtFit()
    {
        var vm = CreateLaidOut();
        vm.ZoomInCommand.Execute(null);

        vm.ZoomOutCommand.Execute(null);
        vm.ZoomOutCommand.Execute(null);

        Assert.Equal(PanZoomTransform.Identity, vm.PanZoom.Transform);
    }

    [Fact]
    public void ZoomInCommandStopsAtTheMaximumScale()
    {
        var vm = CreateLaidOut();

        for (var i = 0; i < 30; i++)
        {
            vm.ZoomInCommand.Execute(null);
        }

        Assert.Equal(6.0, vm.PanZoom.Transform.Scale, Precision);
    }

    [Fact]
    public void FitCommandReturnsToTheFittedState()
    {
        var vm = CreateLaidOut();
        vm.ZoomInCommand.Execute(null);
        vm.PanZoom.PanBy(-50, -30);

        vm.FitCommand.Execute(null);

        Assert.Equal(PanZoomTransform.Identity, vm.PanZoom.Transform);
    }

    [Fact]
    public void ZoomCommandsAreIgnoredUntilTheImageSizeIsKnown()
    {
        var vm = new PreviewPaneViewModel(new MockPreviewPaneService(), new WorkspaceState());
        vm.OnViewportSizeChanged(800, 600);

        vm.ZoomInCommand.Execute(null);

        Assert.Equal(PanZoomTransform.Identity, vm.PanZoom.Transform);
    }

    [Fact]
    public void OnImageOpenedResetsAnExistingZoomToFit()
    {
        var vm = CreateLaidOut();
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);

        vm.OnImageOpened(1920, 1080);

        Assert.Equal(PanZoomTransform.Identity, vm.PanZoom.Transform);
    }

    [Theory]
    [InlineData(1600, 1200)]
    [InlineData(400, 300)]
    public void OnViewportSizeChangedKeepsTheZoomAndTheViewedPosition(double newWidth, double newHeight)
    {
        var vm = CreateLaidOut();
        vm.PanZoom.ZoomAt(3.0, new PanZoomPoint(700, 500));
        var centerBefore = NormalizedViewportCenter(vm.PanZoom);

        vm.OnViewportSizeChanged(newWidth, newHeight);

        var centerAfter = NormalizedViewportCenter(vm.PanZoom);
        Assert.Equal(3.0, vm.PanZoom.Transform.Scale, Precision);
        Assert.Equal(centerBefore.X, centerAfter.X, Precision);
        Assert.Equal(centerBefore.Y, centerAfter.Y, Precision);
    }

    private static PanZoomPoint NormalizedViewportCenter(PanZoomController controller)
    {
        var content = controller.Transform.ToContent(new PanZoomPoint(controller.Viewport.Width / 2, controller.Viewport.Height / 2));
        var bounds = controller.ContentBounds;
        return new PanZoomPoint((content.X - bounds.X) / bounds.Width, (content.Y - bounds.Y) / bounds.Height);
    }

    [Fact]
    public void CleanupRemovesCurrentImage()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);

        // Act
        vm.Cleanup();

        // Assert
        Assert.Null(vm.CurrentImage);
    }

    [Fact]
    public void NextCommandCallsWorkspaceStateSelectNext()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var selectNextCalled = false;
        workspaceState.SelectNextAction = () => selectNextCalled = true;
        var vm = new PreviewPaneViewModel(service, workspaceState);

        // Act
        vm.NextCommand.Execute(null);

        // Assert
        Assert.True(selectNextCalled);
    }

    [Fact]
    public void PreviousCommandCallsWorkspaceStateSelectPrevious()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var selectPreviousCalled = false;
        workspaceState.SelectPreviousAction = () => selectPreviousCalled = true;
        var vm = new PreviewPaneViewModel(service, workspaceState);

        // Act
        vm.PreviousCommand.Execute(null);

        // Assert
        Assert.True(selectPreviousCalled);
    }
}
