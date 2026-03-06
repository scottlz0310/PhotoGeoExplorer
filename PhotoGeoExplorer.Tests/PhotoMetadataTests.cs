using System.Globalization;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Tests;

public sealed class PhotoMetadataTests
{
    [Fact]
    public void CameraSummaryReturnsNullWhenMakeAndModelMissing()
    {
        var metadata = new PhotoMetadata(null, null, null, null, null);

        Assert.Null(metadata.CameraSummary);
    }

    [Fact]
    public void CameraSummaryReturnsMakeWhenModelMissing()
    {
        var metadata = new PhotoMetadata(null, "Canon", null, null, null);

        Assert.Equal("Canon", metadata.CameraSummary);
    }

    [Fact]
    public void CameraSummaryReturnsModelWhenMakeMissing()
    {
        var metadata = new PhotoMetadata(null, null, "X100V", null, null);

        Assert.Equal("X100V", metadata.CameraSummary);
    }

    [Fact]
    public void CameraSummaryReturnsCombinedWhenMakeAndModelPresent()
    {
        var metadata = new PhotoMetadata(null, "Fujifilm", "X100V", null, null);

        Assert.Equal("Fujifilm X100V", metadata.CameraSummary);
    }

    [Fact]
    public void TakenAtTextFormatsTimestamp()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var takenAt = new DateTimeOffset(2024, 1, 2, 3, 4, 0, TimeSpan.Zero);
        var metadata = new PhotoMetadata(takenAt, null, null, null, null);

        Assert.Equal("2024-01-02 03:04", metadata.TakenAtText);
    }

    [Fact]
    public void HasValidLocationReturnsFalseForZeroCoordinates()
    {
        var metadata = new PhotoMetadata(null, null, null, 0, 0, hasGpsData: true);

        Assert.True(metadata.HasLocation);
        Assert.False(metadata.HasValidLocation);
        Assert.True(metadata.IsLikelyLocationFixFailed);
    }

    [Fact]
    public void HasValidLocationReturnsTrueForNonZeroCoordinates()
    {
        var metadata = new PhotoMetadata(null, null, null, 35.681236, 139.767125, hasGpsData: true);

        Assert.True(metadata.HasLocation);
        Assert.True(metadata.HasValidLocation);
        Assert.False(metadata.IsLikelyLocationFixFailed);
    }
}
