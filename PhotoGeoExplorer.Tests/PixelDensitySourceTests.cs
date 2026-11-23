using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 実行中に表示倍率を変えたときにだけ起きる、画素密度の取得元の誤りを防ぐ。
/// Mapsui の <c>MapControl.GetPixelDensity()</c> は「描画面の画素幅 ÷ 表示幅」で、表示倍率の変更の直後は
/// 2 つの値の更新が追いつかない。<c>XamlRoot.Changed</c> で読むと古い値や中途半端な比になり、ベクター地図が
/// 古い密度で描かれたままになる（#358）。画素密度は <c>XamlRoot.RasterizationScale</c> から取る
/// </summary>
public static partial class PixelDensitySourceTests
{
    [Fact]
    public static void AppCodeDoesNotCallGetPixelDensityOnAnotherObject()
    {
        var appDirectory = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer");
        var sources = Directory.EnumerateFiles(appDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();
        var violations = sources
            .Where(path => CallsGetPixelDensityOnAnotherObject(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.NotEmpty(sources);
        Assert.True(
            violations.Count == 0,
            "GetPixelDensity() を他のオブジェクトから呼んでいます（Mapsui の MapControl の値は、表示倍率の変更の直後は古い値を返します。"
            + "XamlRoot.RasterizationScale を使ってください）: " + string.Join(", ", violations));
    }

    // 実際のソース走査（上のテスト）と同じ経路（コメントの除去 → 複数行にまたがる照合 → 自前の呼び出しの除外）を通す
    [Theory]
    [InlineData("var density = ((IMapControl)MapControl).GetPixelDensity();")]
    [InlineData("var density = ((IMapControl)map).GetPixelDensity() ?? 1;")]
    [InlineData("var density = MapControl.GetPixelDensity();")]
    [InlineData("var density = _mapControl . GetPixelDensity ( );")]
    [InlineData("var density = mapControl?.GetPixelDensity();")]
    [InlineData("IMapControl control = MapControl; var density = control.GetPixelDensity();")]
    [InlineData("var density = GetControl()!.GetPixelDensity();")]
    [InlineData("var density = controls[0].GetPixelDensity();")]
    [InlineData("var density = mapControl\n    .GetPixelDensity();")]
    [InlineData("var density = mapControl?\r\n    .GetPixelDensity();")]
    [InlineData("var density = mapControl\r\n    ?.GetPixelDensity();")]
    [InlineData("// MapControl.GetPixelDensity() は使わない\nvar density = control.GetPixelDensity();")]
    [InlineData("var url = \"https://example.com/\"; var density = control.GetPixelDensity();")]
    public static void TheScanDetectsCallsOnAnotherObject(string source)
        => Assert.True(CallsGetPixelDensityOnAnotherObject(source));

    [Theory]
    [InlineData("private double GetPixelDensity() => Volatile.Read(ref _pixelDensity);")]
    [InlineData("private double? GetPixelDensity() => XamlRoot?.RasterizationScale;")]
    [InlineData("if (_viewModel is not null && GetPixelDensity() is { } pixelDensity)")]
    [InlineData("if (_viewModel is not null && this.GetPixelDensity() is { } pixelDensity)")]
    [InlineData("var density = this\n    .GetPixelDensity();")]
    [InlineData("newBackground.Attach(_map, GetPixelDensity);")]
    [InlineData("_getPixelDensity = getPixelDensity;")]
    [InlineData("var density = XamlRoot.RasterizationScale;")]
    [InlineData("// Mapsui の MapControl.GetPixelDensity() は更新が追いつかない")]
    [InlineData("/* mapControl.GetPixelDensity() は使わない */ var density = GetPixelDensity();")]
    [InlineData("/*\n * control.GetPixelDensity() の説明\n */\nvar density = GetPixelDensity();")]
    [InlineData("var density = GetPixelDensity(); // MapControl.GetPixelDensity() は使わない")]
    public static void TheScanIgnoresTheOwnMethodCommentsAndOtherMembers(string source)
        => Assert.False(CallsGetPixelDensityOnAnotherObject(source));

    /// <summary>
    /// ソースが、他のオブジェクトに対する <c>GetPixelDensity()</c> の呼び出しを含むか。
    /// コメントは対象にしない（理由を説明するコメントが API 名に触れるため）。自前のメソッド（View・ViewModel の private な
    /// <c>GetPixelDensity</c>）は、レシーバーなしか <c>this</c> / <c>base</c> で呼ぶので許容し、Mapsui の MapControl / IMapControl の値は、
    /// 名前に依らず（フィールド名・ローカル変数名が何であっても）、<c>.</c>・<c>?.</c>、キャスト・インデクサー・<c>!</c> の後、
    /// 複数行にまたがる呼び出しも検出する
    /// </summary>
    internal static bool CallsGetPixelDensityOnAnotherObject(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var code = CommentPattern().Replace(source, " ");
        return ReceiverQualifiedCallPattern().Matches(code)
            .Any(match => match.Groups["receiver"].Value is not ("this" or "base"));
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // ブロックコメントと行コメント。URL の `://` を行コメントと取り違えないよう、直前が `:` や `"` の `//` は対象にしない
    [GeneratedRegex(@"/\*.*?\*/|(?<![:""])//[^\r\n]*", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    // レシーバー付きの呼び出し。レシーバーは、識別子、または `)` `]` `!`（キャスト・インデクサー・null 許容の後）で終わる式
    [GeneratedRegex(@"(?<receiver>[\w\)\]!]+)\s*\??\s*\.\s*GetPixelDensity\s*\(")]
    private static partial Regex ReceiverQualifiedCallPattern();
}
