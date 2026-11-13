using System;
using System.IO;
using System.Linq;
using PhotoGeoExplorer.Services.Export;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class ExportNameSanitizerTests
{
    [Theory]
    [InlineData("東品治町", "東品治町")]
    [InlineData("鳥取市", "鳥取市")]
    // 使えない文字は _ にする
    [InlineData("A/B", "A_B")]
    [InlineData("A\\B", "A_B")]
    [InlineData("a:b*c?d\"e<f>g|h", "a_b_c_d_e_f_g_h")]
    [InlineData("改行\r\nあり", "改行__あり")]
    // 前後の空白とドットは取る（Windows は末尾のドットと空白を許さない）
    [InlineData("  名前  ", "名前")]
    [InlineData("名前...", "名前")]
    [InlineData(". 名前 .", "名前")]
    // 空・空白・記号だけは _ になる（空にならない）
    [InlineData("", "_")]
    [InlineData("   ", "_")]
    [InlineData("...", "_")]
    [InlineData(null, "_")]
    public void SanitizeSegmentReplacesInvalidCharactersAndTrims(string? name, string expected)
    {
        Assert.Equal(expected, ExportNameSanitizer.SanitizeSegment(name));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    // 拡張子がついていても予約名
    [InlineData("CON.txt")]
    [InlineData("nul.jpg")]
    public void SanitizeSegmentAvoidsReservedDeviceNames(string name)
    {
        var sanitized = ExportNameSanitizer.SanitizeSegment(name);

        Assert.StartsWith("_", sanitized, StringComparison.Ordinal);
        Assert.Equal("_" + name, sanitized);
    }

    [Theory]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("CONSOLE")]
    [InlineData("LPT")]
    public void SanitizeSegmentKeepsNamesThatOnlyLookLikeReservedNames(string name)
    {
        Assert.Equal(name, ExportNameSanitizer.SanitizeSegment(name));
    }

    [Theory]
    [InlineData(5, "あいうえお")]
    [InlineData(3, "あいう")]
    [InlineData(1, "あ")]
    public void SanitizeSegmentTruncatesToTheMaximumLength(int maxLength, string expected)
    {
        Assert.Equal(expected, ExportNameSanitizer.SanitizeSegment("あいうえおかきくけこ", maxLength));
    }

    [Fact]
    public void SanitizeSegmentDoesNotSplitASurrogatePair()
    {
        // 「𠮷」（U+20BB7）は UTF-16 で 2 文字。2 文字目で切ると壊れる
        var sanitized = ExportNameSanitizer.SanitizeSegment("あ𠮷い", 2);

        Assert.Equal("あ", sanitized);
        Assert.DoesNotContain(sanitized, character => char.IsSurrogate(character));
    }

    [Fact]
    public void SanitizeSegmentTrimsAfterTruncating()
    {
        // 切った末尾が空白やドットだと、Windows で使えない名前になる
        Assert.Equal("あい", ExportNameSanitizer.SanitizeSegment("あい. う", 3));
    }

    [Fact]
    public void SanitizeSegmentAlwaysReturnsANameThatWindowsAccepts()
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var name in new[] { "a/b", "CON", "x:y", "  .  ", "ま\tち", string.Concat(Enumerable.Repeat("長い", 200)) })
        {
            var sanitized = ExportNameSanitizer.SanitizeSegment(name);

            Assert.NotEmpty(sanitized);
            Assert.True(sanitized.Length <= ExportNameSanitizer.DefaultMaxLength);
            Assert.DoesNotContain(sanitized, character => invalid.Contains(character));
            Assert.False(sanitized.EndsWith(' ') || sanitized.EndsWith('.'), $"末尾が空白かドット: {sanitized}");
        }
    }

    [Fact]
    public void SanitizeSegmentRejectsANonPositiveMaximumLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExportNameSanitizer.SanitizeSegment("a", 0));
    }
}
