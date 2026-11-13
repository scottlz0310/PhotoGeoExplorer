using System.Linq;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class PrefectureCatalogTests
{
    [Theory]
    [InlineData("01", "北海道")]
    [InlineData("13", "東京都")]
    [InlineData("26", "京都府")]
    [InlineData("27", "大阪府")]
    [InlineData("31", "鳥取県")]
    [InlineData("47", "沖縄県")]
    public void TryGetNameReturnsTheNameForAValidCode(string code, string expectedName)
    {
        Assert.True(PrefectureCatalog.TryGetName(code, out var name));
        Assert.Equal(expectedName, name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("001")]
    [InlineData("00")]
    [InlineData("48")]
    [InlineData("99")]
    [InlineData("ab")]
    [InlineData("1a")]
    [InlineData(" 1")]
    // 全角数字は受け付けない
    [InlineData("０１")]
    [InlineData("../")]
    public void TryGetNameRejectsAnythingButATwoDigitCodeFrom01To47(string? code)
    {
        Assert.False(PrefectureCatalog.TryGetName(code, out var name));
        Assert.Equal(string.Empty, name);
        Assert.False(PrefectureCatalog.IsValidCode(code));
    }

    [Fact]
    public void CodesCoversAll47PrefecturesInOrder()
    {
        var codes = PrefectureCatalog.Codes.ToList();

        Assert.Equal(47, codes.Count);
        Assert.Equal("01", codes[0]);
        Assert.Equal("47", codes[^1]);
        Assert.All(codes, code => Assert.True(PrefectureCatalog.IsValidCode(code)));
        Assert.Equal(47, codes.Distinct().Count());
    }
}
