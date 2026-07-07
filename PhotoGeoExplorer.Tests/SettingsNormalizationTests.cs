using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class SettingsNormalizationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("system", null)]
    [InlineData("SYSTEM", null)]
    [InlineData("ja", "ja-JP")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("JA-JP", "ja-JP")]
    [InlineData("en", "en-US")]
    [InlineData("en-us", "en-US")]
    [InlineData(" en ", "en-US")]
    [InlineData("fr-FR", "fr-FR")]
    [InlineData(" fr-FR ", "fr-FR")]
    public void NormalizeLanguageSettingReturnsExpectedValue(string? input, string? expected)
    {
        var actual = SettingsNormalization.NormalizeLanguageSetting(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(14, 14)]
    [InlineData(18, 18)]
    [InlineData(7, 14)]
    [InlineData(20, 14)]
    [InlineData(0, 14)]
    [InlineData(-1, 14)]
    [InlineData(int.MaxValue, 14)]
    public void NormalizeMapZoomLevelReturnsExpectedValue(int input, int expected)
    {
        var actual = SettingsNormalization.NormalizeMapZoomLevel(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(14, 14)]
    [InlineData(18, 18)]
    [InlineData(7, 8)]
    [InlineData(0, 8)]
    [InlineData(-100, 8)]
    [InlineData(9, 8)]
    [InlineData(11, 10)]
    [InlineData(13, 12)]
    [InlineData(15, 14)]
    [InlineData(17, 16)]
    [InlineData(19, 18)]
    [InlineData(100, 18)]
    public void SnapMapZoomLevelToNearestReturnsExpectedValue(int input, int expected)
    {
        var actual = SettingsNormalization.SnapMapZoomLevelToNearest(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("not a url", null)]
    [InlineData("https://photogeoexplorer.pages.dev", "https://photogeoexplorer.pages.dev")]
    [InlineData("https://photogeoexplorer.pages.dev/", "https://photogeoexplorer.pages.dev")]
    [InlineData(" https://example.com/help ", "https://example.com/help")]
    [InlineData("http://example.com/help", "http://example.com/help")]
    [InlineData("ftp://example.com", null)]
    [InlineData("file:///C:/temp", null)]
    public void NormalizeExternalContentBaseUrlReturnsExpectedValue(string? input, string? expected)
    {
        var actual = SettingsNormalization.NormalizeExternalContentBaseUrl(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NormalizePaneLayoutPresetReturnsInputWhenDefined(int input)
    {
        var preset = (PaneLayoutPreset)input;
        var actual = SettingsNormalization.NormalizePaneLayoutPreset(preset);
        Assert.Equal(preset, actual);
    }

    [Fact]
    public void NormalizePaneLayoutPresetReturnsDefaultForInvalidValue()
    {
        var actual = SettingsNormalization.NormalizePaneLayoutPreset((PaneLayoutPreset)999);
        Assert.Equal(AppSettings.DefaultPaneLayoutPreset, actual);
    }

    [Fact]
    public void NormalizePaneRegionViewsReturnsSameValuesWhenDistinct()
    {
        var actual = SettingsNormalization.NormalizePaneRegionViews(
            PaneViewType.Map,
            PaneViewType.Preview,
            PaneViewType.File);

        Assert.Equal(PaneViewType.Map, actual.Region1View);
        Assert.Equal(PaneViewType.Preview, actual.Region2View);
        Assert.Equal(PaneViewType.File, actual.Region3View);
    }

    [Fact]
    public void NormalizePaneRegionViewsReplacesDuplicateWithUnusedValue()
    {
        var actual = SettingsNormalization.NormalizePaneRegionViews(
            PaneViewType.File,
            PaneViewType.File,
            PaneViewType.Map);

        Assert.Equal(PaneViewType.File, actual.Region1View);
        Assert.Equal(PaneViewType.Preview, actual.Region2View);
        Assert.Equal(PaneViewType.Map, actual.Region3View);
    }

    [Fact]
    public void NormalizePaneRegionViewsNormalizesInvalidAndRemovesDuplicates()
    {
        var actual = SettingsNormalization.NormalizePaneRegionViews(
            (PaneViewType)99,
            (PaneViewType)100,
            PaneViewType.File);

        Assert.Equal(PaneViewType.File, actual.Region1View);
        Assert.Equal(PaneViewType.Preview, actual.Region2View);
        Assert.Equal(PaneViewType.Map, actual.Region3View);
    }
}
