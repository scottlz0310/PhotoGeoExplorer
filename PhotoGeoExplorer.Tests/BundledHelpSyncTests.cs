using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// アプリ同梱のヘルプ（オフライン時のフォールバック）が、公開サイトのヘルプ（docs）と同じ内容であることを保つ。
/// 同梱側は WebView2 の配色に合わせるため、&lt;style&gt; だけを別に持つ（docs はテーマ追随）。
/// </summary>
public sealed partial class BundledHelpSyncTests
{
    [GeneratedRegex("<style>.*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    [Theory]
    [InlineData("index.html")]
    [InlineData("index.en.html")]
    public void BundledHelpMatchesPublishedHelpExceptStyle(string fileName)
    {
        var root = RepositoryPaths.FindRoot();
        var published = ReadWithoutStyle(Path.Combine(root, "docs", "help", fileName));
        var bundled = ReadWithoutStyle(Path.Combine(root, "PhotoGeoExplorer", "wwwroot", "help", fileName));

        Assert.Equal(published, bundled);
    }

    private static string ReadWithoutStyle(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        return StyleBlock().Replace(text, "<style></style>");
    }
}
