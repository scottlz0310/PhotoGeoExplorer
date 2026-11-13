using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 起動時にしか発覚しない、リソースキーと x:Uid の衝突を防ぐ。
/// x:Uid="MapHeader" の要素は、リソースの "MapHeader.&lt;プロパティ&gt;" を、その要素のプロパティとして読み込む。
/// コードから引くキーが "x:Uid.複数の名前" の形だと、"Unable to resolve property" で XAML の読み込みに失敗する
/// </summary>
public static partial class ResourceKeyCollisionTests
{
    [Fact]
    public static void KeysUsedFromCodeDoNotStartWithAnXamlUid()
    {
        var appDirectory = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer");
        var uids = Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => XamlUidPattern().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        var collisions = new List<string>();
        foreach (var path in Directory.EnumerateFiles(appDirectory, "*.cs", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path)))
        {
            foreach (Match match in CodeKeyPattern().Matches(File.ReadAllText(path)))
            {
                var key = match.Groups[1].Value;
                var dot = key.IndexOf('.', StringComparison.Ordinal);

                // "MenuSettings.Title" のような、x:Uid の要素のプロパティ（単一の名前）を読む使い方は正しい
                if (dot > 0 && uids.Contains(key[..dot]) && key[(dot + 1)..].Contains('.', StringComparison.Ordinal)
                    && !key[(dot + 1)..].StartsWith("ToolTipService.", StringComparison.Ordinal)
                    && !key[(dot + 1)..].StartsWith("AutomationProperties.", StringComparison.Ordinal))
                {
                    collisions.Add($"{key}（{Path.GetFileName(path)}）");
                }
            }
        }

        Assert.True(uids.Count > 0);
        Assert.True(collisions.Count == 0, "x:Uid のプロパティとして解釈されるキーをコードから引いています: " + string.Join(", ", collisions.Distinct()));
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [GeneratedRegex("x:Uid=\"([^\"]+)\"")]
    private static partial Regex XamlUidPattern();

    [GeneratedRegex(@"LocalizationService\.(?:GetString|Format)\(\s*""([^""]+)""")]
    private static partial Regex CodeKeyPattern();
}
