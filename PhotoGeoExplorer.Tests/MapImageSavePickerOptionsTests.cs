using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class MapImageSavePickerOptionsTests
{
    [Theory]
    [InlineData(@"C:\photos", true, @"C:\photos")]
    [InlineData(@"C:\missing", false, @"C:\Pictures")]
    [InlineData(null, false, @"C:\Pictures")]
    [InlineData("", false, @"C:\Pictures")]
    [InlineData("   ", false, @"C:\Pictures")]
    public void CreateSelectsExpectedStartFolder(
        string? imageSourceFolderPath,
        bool sourceFolderExists,
        string expected)
    {
        var options = MapImageSavePickerOptions.Create(
            imageSourceFolderPath,
            "map.png",
            _ => sourceFolderExists,
            () => @"C:\Pictures");

        Assert.Equal(expected, options.SuggestedStartFolder);
        Assert.Equal("map.png", options.SuggestedFileName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsMissingSuggestedFileName(string? suggestedFileName)
    {
        Assert.Throws<ArgumentException>(() => MapImageSavePickerOptions.Create(
            @"C:\photos",
            suggestedFileName!,
            _ => true,
            () => @"C:\Pictures"));
    }

    [Fact]
    public void MapImagePickerUsesDedicatedPngConfiguration()
    {
        Assert.Equal("PhotoGeoExplorer.MapImageExport", MapImageSavePickerOptions.SettingsIdentifier);
        Assert.Equal("PNG", MapImageSavePickerOptions.FileTypeLabel);
        Assert.Equal(".png", MapImageSavePickerOptions.DefaultFileExtension);
    }
}
