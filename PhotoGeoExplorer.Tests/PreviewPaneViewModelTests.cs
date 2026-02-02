using System;
using PhotoGeoExplorer.Panes.Preview;
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

        public float CalculateFitZoomFactor(
            double imageWidth,
            double imageHeight,
            double viewportWidth,
            double viewportHeight,
            float minZoom,
            float maxZoom)
        {
            if (imageWidth <= 0 || imageHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            {
                return 1.0f;
            }

            var scaleX = viewportWidth / imageWidth;
            var scaleY = viewportHeight / imageHeight;
            var target = (float)Math.Min(scaleX, scaleY);
            return Math.Clamp(target, minZoom, maxZoom);
        }

        public float CalculateDpiCorrectedZoom(
            float currentZoom,
            double oldScale,
            double newScale,
            float minZoom,
            float maxZoom)
        {
            if (oldScale <= 0 || newScale <= 0)
            {
                return currentZoom;
            }

            var correctedZoom = (float)(currentZoom * oldScale / newScale);
            return Math.Clamp(correctedZoom, minZoom, maxZoom);
        }
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

    [Fact]
    public void FitToWindowDefaultValue()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        var vm = new PreviewPaneViewModel(service, workspaceState);

        // Assert
        Assert.True(vm.FitToWindow);
    }

    [Fact]
    public void ZoomFactorDefaultValue()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();

        // Act
        var vm = new PreviewPaneViewModel(service, workspaceState);

        // Assert
        Assert.Equal(1.0f, vm.ZoomFactor);
    }

    [Fact]
    public void ExecuteFitSetsFitToWindowTrue()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        vm.FitToWindow = false;

        // Act
        vm.FitCommand.Execute(null);

        // Assert
        Assert.True(vm.FitToWindow);
    }

    [Fact]
    public void ExecuteZoomInIncrementsZoomFactor()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        var initialZoom = vm.ZoomFactor;

        // Act
        vm.ZoomInCommand.Execute(null);

        // Assert
        Assert.True(vm.ZoomFactor > initialZoom);
        Assert.False(vm.FitToWindow);
    }

    [Fact]
    public void ExecuteZoomOutDecrementsZoomFactor()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        vm.ZoomFactor = 2.0f;

        // Act
        vm.ZoomOutCommand.Execute(null);

        // Assert
        Assert.True(vm.ZoomFactor < 2.0f);
        Assert.False(vm.FitToWindow);
    }

    [Fact]
    public void AdjustZoomMultipliesZoomFactor()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        vm.ZoomFactor = 1.0f;

        // Act
        vm.AdjustZoom(2.0f);

        // Assert
        Assert.Equal(2.0f, vm.ZoomFactor);
        Assert.False(vm.FitToWindow);
    }

    [Fact]
    public void AdjustZoomClampsToMinZoom()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        vm.ZoomFactor = 0.2f;

        // Act
        vm.AdjustZoom(0.1f); // 0.2 * 0.1 = 0.02 -> クランプされて 0.1

        // Assert
        Assert.Equal(0.1f, vm.ZoomFactor);
    }

    [Fact]
    public void AdjustZoomClampsToMaxZoom()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        vm.ZoomFactor = 5.0f;

        // Act
        vm.AdjustZoom(2.0f); // 5.0 * 2.0 = 10.0 -> クランプされて 6.0

        // Assert
        Assert.Equal(6.0f, vm.ZoomFactor);
    }

    [Fact]
    public void OnRasterizationScaleChangedUpdatesDpiScale()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        vm.RasterizationScale = 1.0;

        // Act
        vm.OnRasterizationScaleChanged(1.5);

        // Assert
        Assert.Equal(1.5, vm.RasterizationScale);
    }

    [Fact]
    public void OnRasterizationScaleChangedWithFitToWindowDoesNotCorrectZoom()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        vm.FitToWindow = true;
        vm.ZoomFactor = 2.0f;
        vm.RasterizationScale = 1.0;

        // Act
        vm.OnRasterizationScaleChanged(1.5);

        // Assert - FitToWindow モードではズームファクター補正しない
        Assert.Equal(2.0f, vm.ZoomFactor);
    }

    [Fact]
    public void OnRasterizationScaleChangedWithoutFitToWindowCorrectsZoom()
    {
        // Arrange
        var service = new MockPreviewPaneService();
        var workspaceState = new WorkspaceState();
        var vm = new PreviewPaneViewModel(service, workspaceState);
        vm.FitToWindow = false;
        vm.ZoomFactor = 2.0f;
        vm.RasterizationScale = 1.0;

        // Act
        vm.OnRasterizationScaleChanged(1.5);

        // Assert - 2.0 * 1.0 / 1.5 = 1.333...
        Assert.InRange(vm.ZoomFactor, 1.33f, 1.34f);
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
