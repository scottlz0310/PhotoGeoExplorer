using System.Globalization;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Tests;

[TestClass]
public sealed class PhotoMetadataTests
{
    [TestMethod]
    public void CameraSummaryReturnsNullWhenMakeAndModelMissing()
    {
        var metadata = new PhotoMetadata(null, null, null, null, null);

        Assert.IsNull(metadata.CameraSummary);
    }

    [TestMethod]
    public void CameraSummaryReturnsMakeWhenModelMissing()
    {
        var metadata = new PhotoMetadata(null, "Canon", null, null, null);

        Assert.AreEqual("Canon", metadata.CameraSummary);
    }

    [TestMethod]
    public void CameraSummaryReturnsModelWhenMakeMissing()
    {
        var metadata = new PhotoMetadata(null, null, "X100V", null, null);

        Assert.AreEqual("X100V", metadata.CameraSummary);
    }

    [TestMethod]
    public void CameraSummaryReturnsCombinedWhenMakeAndModelPresent()
    {
        var metadata = new PhotoMetadata(null, "Fujifilm", "X100V", null, null);

        Assert.AreEqual("Fujifilm X100V", metadata.CameraSummary);
    }

    [TestMethod]
    public void TakenAtTextFormatsTimestamp()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var takenAt = new DateTimeOffset(2024, 1, 2, 3, 4, 0, TimeSpan.Zero);
        var metadata = new PhotoMetadata(takenAt, null, null, null, null);

        Assert.AreEqual("2024-01-02 03:04", metadata.TakenAtText);
    }
}
