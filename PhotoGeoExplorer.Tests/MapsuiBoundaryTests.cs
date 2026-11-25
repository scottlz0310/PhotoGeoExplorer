using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 地図ライブラリ（Mapsui・BruTile）の型は <c>Panes/Map/MapsuiAdapter</c> の中だけで使う（#362 §11.6）。
/// 境界の外へ新しく漏れたら、ここで検出する。
/// </summary>
public static partial class MapsuiBoundaryTests
{
    [Fact]
    public static void MapLibraryTypesStayInsideTheAdapter()
    {
        var root = RepositoryPaths.FindRoot();
        var appDirectory = Path.Combine(root, "PhotoGeoExplorer");
        var coreDirectory = Path.Combine(root, "PhotoGeoExplorer.Core");
        var sources = new[] { appDirectory, coreDirectory }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))
            .ToList();

        var violations = sources
            .Select(path => (Path: RelativePath(root, path), Source: File.ReadAllText(path)))
            .Where(file => !IsInsideAdapter(file.Path))
            .Where(file => UsesMapLibrary(file.Source))
            .Select(file => file.Path)
            .ToList();

        Assert.NotEmpty(sources);
        Assert.True(
            violations.Count == 0,
            "Mapsui・BruTile の型を、Panes/Map/MapsuiAdapter の外で使っています（IMapSurface 経由にしてください）: "
            + string.Join(", ", violations));
    }

    [Theory]
    [InlineData("using Mapsui;")]
    [InlineData("using Mapsui.Layers;")]
    [InlineData("private Mapsui.Map? _map;")]
    [InlineData("using BruTile.Cache;")]
    [InlineData("xmlns:mapsui=\"using:Mapsui.UI.WinUI\"")]
    [InlineData("var x = Mapsui\n    .Map.Create();")]
    public static void TheScanDetectsMapLibraryUsage(string source)
        => Assert.True(UsesMapLibrary(source));

    [Theory]
    [InlineData("// Mapsui の型は出さない")]
    [InlineData("/// <see cref=\"Mapsui.Map\"/> ではない")]
    [InlineData("/* BruTile を使う */ var x = 1;")]
    [InlineData("private IMapSurface? _surface; // Mapsui に依存しない")]
    [InlineData("var name = MapsuiAdapterName;")]
    [InlineData("internal sealed class MyMapsuiThing { }")]
    public static void TheScanIgnoresCommentsAndSimilarNames(string source)
        => Assert.False(UsesMapLibrary(source));

    private static bool UsesMapLibrary(string source)
        => MapLibraryPattern().IsMatch(StripComments(source));

    // C# のコメント（// と /* */）だけを除く。XAML の属性値（xmlns）は残す
    private static string StripComments(string source)
        => LineCommentPattern().Replace(BlockCommentPattern().Replace(source, string.Empty), string.Empty);

    private static bool IsInsideAdapter(string relativePath)
        => relativePath.StartsWith("PhotoGeoExplorer/Panes/Map/MapsuiAdapter/", StringComparison.Ordinal);

    private static string RelativePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsBuildOutput(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return path.Split(separators).Any(segment => segment is "bin" or "obj");
    }

    [GeneratedRegex(@"\b(?:Mapsui|BruTile)\b|xmlns:mapsui=", RegexOptions.CultureInvariant)]
    private static partial Regex MapLibraryPattern();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockCommentPattern();

    [GeneratedRegex(@"(?<!:)//[^\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex LineCommentPattern();
}
