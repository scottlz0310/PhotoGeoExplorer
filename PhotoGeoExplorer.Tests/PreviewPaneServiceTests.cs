using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Panes.Preview;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// PreviewPaneService のテスト
/// </summary>
public class PreviewPaneServiceTests
{
    [Fact]
    public void CalculateFitZoomFactor_ValidInput_ReturnsCorrectZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        double imageWidth = 1920;
        double imageHeight = 1080;
        double viewportWidth = 960;
        double viewportHeight = 540;

        // Act
        var zoom = service.CalculateFitZoomFactor(imageWidth, imageHeight, viewportWidth, viewportHeight, 0.1f, 6.0f);

        // Assert
        Assert.Equal(0.5f, zoom, 3); // 960/1920 = 0.5, 540/1080 = 0.5
    }

    [Fact]
    public void CalculateFitZoomFactor_PortraitImage_ReturnsCorrectZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        double imageWidth = 1080;
        double imageHeight = 1920;
        double viewportWidth = 540;
        double viewportHeight = 960;

        // Act
        var zoom = service.CalculateFitZoomFactor(imageWidth, imageHeight, viewportWidth, viewportHeight, 0.1f, 6.0f);

        // Assert
        Assert.Equal(0.5f, zoom, 3);
    }

    [Fact]
    public void CalculateFitZoomFactor_SmallImage_ClampsToMinZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        double imageWidth = 100;
        double imageHeight = 100;
        double viewportWidth = 10;
        double viewportHeight = 10;

        // Act
        var zoom = service.CalculateFitZoomFactor(imageWidth, imageHeight, viewportWidth, viewportHeight, 0.1f, 6.0f);

        // Assert
        Assert.Equal(0.1f, zoom, 3); // クランプされて 0.1
    }

    [Fact]
    public void CalculateFitZoomFactor_LargeViewport_ClampsToMaxZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        double imageWidth = 100;
        double imageHeight = 100;
        double viewportWidth = 1000;
        double viewportHeight = 1000;

        // Act
        var zoom = service.CalculateFitZoomFactor(imageWidth, imageHeight, viewportWidth, viewportHeight, 0.1f, 6.0f);

        // Assert
        Assert.Equal(6.0f, zoom, 3); // クランプされて 6.0
    }

    [Fact]
    public void CalculateFitZoomFactor_ZeroImageSize_ReturnsDefaultZoom()
    {
        // Arrange
        var service = new PreviewPaneService();

        // Act
        var zoom = service.CalculateFitZoomFactor(0, 100, 100, 100, 0.1f, 6.0f);

        // Assert
        Assert.Equal(1.0f, zoom);
    }

    [Fact]
    public void CalculateFitZoomFactor_ZeroViewportSize_ReturnsDefaultZoom()
    {
        // Arrange
        var service = new PreviewPaneService();

        // Act
        var zoom = service.CalculateFitZoomFactor(100, 100, 0, 100, 0.1f, 6.0f);

        // Assert
        Assert.Equal(1.0f, zoom);
    }

    [Fact]
    public void CalculateDpiCorrectedZoom_ValidInput_ReturnsCorrectZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        float currentZoom = 2.0f;
        double oldScale = 1.0;
        double newScale = 1.5;

        // Act
        var correctedZoom = service.CalculateDpiCorrectedZoom(currentZoom, oldScale, newScale, 0.1f, 6.0f);

        // Assert
        // 2.0 * 1.0 / 1.5 = 1.333...
        Assert.Equal(1.333f, correctedZoom, 3);
    }

    [Fact]
    public void CalculateDpiCorrectedZoom_ClampsToMinZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        float currentZoom = 0.2f;
        double oldScale = 1.0;
        double newScale = 3.0;

        // Act
        var correctedZoom = service.CalculateDpiCorrectedZoom(currentZoom, oldScale, newScale, 0.1f, 6.0f);

        // Assert
        // 0.2 * 1.0 / 3.0 = 0.0666... -> クランプされて 0.1
        Assert.Equal(0.1f, correctedZoom, 3);
    }

    [Fact]
    public void CalculateDpiCorrectedZoom_ClampsToMaxZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        float currentZoom = 5.0f;
        double oldScale = 2.0;
        double newScale = 1.0;

        // Act
        var correctedZoom = service.CalculateDpiCorrectedZoom(currentZoom, oldScale, newScale, 0.1f, 6.0f);

        // Assert
        // 5.0 * 2.0 / 1.0 = 10.0 -> クランプされて 6.0
        Assert.Equal(6.0f, correctedZoom, 3);
    }

    [Fact]
    public void CalculateDpiCorrectedZoom_ZeroOldScale_ReturnsCurrentZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        float currentZoom = 2.0f;

        // Act
        var correctedZoom = service.CalculateDpiCorrectedZoom(currentZoom, 0, 1.5, 0.1f, 6.0f);

        // Assert
        Assert.Equal(currentZoom, correctedZoom);
    }

    [Fact]
    public void CalculateDpiCorrectedZoom_ZeroNewScale_ReturnsCurrentZoom()
    {
        // Arrange
        var service = new PreviewPaneService();
        float currentZoom = 2.0f;

        // Act
        var correctedZoom = service.CalculateDpiCorrectedZoom(currentZoom, 1.0, 0, 0.1f, 6.0f);

        // Assert
        Assert.Equal(currentZoom, correctedZoom);
    }

    [Fact]
    public async Task LoadImageAsync_NullFilePath_ReturnsNull()
    {
        // Arrange
        var service = new PreviewPaneService();

        // Act
        var result = await service.LoadImageAsync(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadImageAsync_EmptyFilePath_ReturnsNull()
    {
        // Arrange
        var service = new PreviewPaneService();

        // Act
        var result = await service.LoadImageAsync(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadImageAsync_NonExistentFile_ReturnsNull()
    {
        // Arrange
        var service = new PreviewPaneService();
        var nonExistentPath = "/tmp/nonexistent_image.jpg";

        // Act
        var result = await service.LoadImageAsync(nonExistentPath);

        // Assert
        Assert.Null(result);
    }
}
