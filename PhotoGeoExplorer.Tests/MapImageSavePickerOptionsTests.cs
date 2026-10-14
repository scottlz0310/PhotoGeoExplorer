using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class MapImageSavePickerOptionsTests
{
    [Theory]
    [InlineData(null, @"C:\photos", true, @"C:\photos")]
    [InlineData(null, @"C:\missing", false, @"C:\Pictures")]
    [InlineData(null, null, false, @"C:\Pictures")]
    [InlineData(null, "", false, @"C:\Pictures")]
    [InlineData(null, "   ", false, @"C:\Pictures")]
    [InlineData(@"C:\exports", @"C:\photos", true, @"C:\exports")]
    [InlineData(@"C:\exports", @"C:\missing", false, @"C:\exports")]
    [InlineData(@"C:\exports", null, false, @"C:\exports")]
    [InlineData("", @"C:\photos", true, @"C:\photos")]
    [InlineData("   ", @"C:\photos", true, @"C:\photos")]
    public void CreateSelectsExpectedStartFolder(
        string? lastSavedFolderPath,
        string? imageSourceFolderPath,
        bool sourceFolderExists,
        string expected)
    {
        var options = MapImageSavePickerOptions.Create(
            lastSavedFolderPath,
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
            null,
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
