using System.Globalization;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Tests;

[TestClass]
public sealed class PhotoItemTests
{
    [TestMethod]
    public void SizeTextReturnsEmptyForFolder()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var item = new PhotoItem("C:\\temp\\folder", 2048, DateTimeOffset.UtcNow, isFolder: true);

        Assert.AreEqual(string.Empty, item.SizeText);
    }

    [TestMethod]
    public void SizeTextFormatsBytes()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var item = new PhotoItem("C:\\temp\\file.txt", 1024, DateTimeOffset.UtcNow, isFolder: false);

        Assert.AreEqual("1 KB", item.SizeText);
    }

    [TestMethod]
    public void ResolutionTextReturnsEmptyWhenMissingDimensions()
    {
        var item = new PhotoItem("C:\\temp\\file.jpg", 100, DateTimeOffset.UtcNow, isFolder: false);

        Assert.AreEqual(string.Empty, item.ResolutionText);
    }

    [TestMethod]
    public void ResolutionTextFormatsDimensions()
    {
        using var _ = new CultureScope(CultureInfo.InvariantCulture);
        var item = new PhotoItem("C:\\temp\\file.jpg", 100, DateTimeOffset.UtcNow, isFolder: false, pixelWidth: 1920, pixelHeight: 1080);

        Assert.AreEqual("1920 x 1080", item.ResolutionText);
    }
}
