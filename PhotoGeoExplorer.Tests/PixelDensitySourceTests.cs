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
    public static void AppCodeDoesNotReadThePixelDensityFromTheMapsuiMapControl()
    {
        var appDirectory = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer");
        var sources = Directory.EnumerateFiles(appDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();
        var violations = new List<string>();
        foreach (var path in sources)
        {
            // 理由を説明するコメントが API 名に触れるため、コメント行は対象にしない
            var code = File.ReadLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));
            if (code.Any(MapControlPixelDensityPattern().IsMatch))
            {
                violations.Add(Path.GetFileName(path));
            }
        }

        Assert.NotEmpty(sources);
        Assert.True(
            violations.Count == 0,
            "Mapsui の MapControl.GetPixelDensity() を使っています（表示倍率の変更の直後は古い値を返します。"
            + "XamlRoot.RasterizationScale を使ってください）: " + string.Join(", ", violations));
    }

    [Theory]
    [InlineData("var density = ((IMapControl)MapControl).GetPixelDensity();")]
    [InlineData("var density = ((IMapControl)map).GetPixelDensity() ?? 1;")]
    [InlineData("var density = MapControl.GetPixelDensity();")]
    [InlineData("var density = _mapControl . GetPixelDensity ( );")]
    public static void ThePatternDetectsReadsFromTheMapControl(string code)
        => Assert.Matches(MapControlPixelDensityPattern(), code);

    [Theory]
    [InlineData("private double GetPixelDensity() => Volatile.Read(ref _pixelDensity);")]
    [InlineData("private double? GetPixelDensity() => XamlRoot?.RasterizationScale;")]
    [InlineData("_getPixelDensity = getPixelDensity;")]
    [InlineData("var density = background.GetPixelDensity();")]
    [InlineData("var density = XamlRoot.RasterizationScale;")]
    public static void ThePatternIgnoresOtherPixelDensityMembers(string code)
        => Assert.DoesNotMatch(MapControlPixelDensityPattern(), code);

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // Mapsui の描画コントロール（MapControl / IMapControl）に対する GetPixelDensity の呼び出し
    [GeneratedRegex(@"(?:IMapControl\s*\)\s*\w+\s*\)|\b\w*MapControl)\s*\.\s*GetPixelDensity\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex MapControlPixelDensityPattern();
}
