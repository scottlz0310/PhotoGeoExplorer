using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// カバレッジの計測対象からコードを外す設定を固定する（#303）。
/// 除外を増やす・変えるときは、このファイルの許可リストを理由つきで更新する。
/// 方針（#171）: View 層の UI 構築コードは除外し、純粋関数・ViewModel・Service は計測対象に残す
/// </summary>
public partial class CoverageExclusionRatchetTests
{
    // codecov.yml の ignore。キーは codecov.yml に書いた文字列そのもの
    private static readonly Dictionary<string, string> AllowedCodecovIgnores = new(StringComparer.Ordinal)
    {
        ["PhotoGeoExplorer.Tests/**"] = "テストコード",
        ["PhotoGeoExplorer.E2E/**"] = "テストコード",
        ["PhotoGeoExplorer/Panes/**/*.xaml.cs"] = "ペインの View のコードビハインド。WinUI の実行環境が必要で単体テストできない",
        ["PhotoGeoExplorer/Panes/FileBrowser/FileBrowserDialogs.cs"] = "View 層の UI 構築ヘルパー。WinRT 型を直接生成する（#171）",
        ["PhotoGeoExplorer/Panes/FileBrowser/FileBrowserDragDropHandler.cs"] = "View 層の UI 構築ヘルパー。WinRT 型を直接生成する（#171）",
        ["PhotoGeoExplorer/Panes/FileBrowser/FileBrowserMenuBuilder.cs"] = "View 層の UI 構築ヘルパー。WinRT 型を直接生成する（#171）",
        ["PhotoGeoExplorer/Panes/Map/PriorityCollapsePanel.cs"] = "WinUI の Panel。測定・配置は単体テストできない。幅の割り当ては PriorityCollapseLayout に分けてテストしている"
    };

    // [ExcludeFromCodeCoverage] を付けてよい型・メンバー。キーは型の完全名、メンバーは「型の完全名.メンバー名」
    private static readonly Dictionary<string, string> AllowedExcludedMembers = new(StringComparer.Ordinal)
    {
        ["PhotoGeoExplorer.App"] = "アプリの起動処理。WinUI の実行環境が必要",
        ["PhotoGeoExplorer.MainWindow"] = "Shell のウィンドウ。WinUI の実行環境が必要",
        ["PhotoGeoExplorer.Program"] = "エントリーポイント",
        ["PhotoGeoExplorer.Services.CrashReportDialogService"] = "ダイアログの表示。実ウィンドウが必要",
        ["PhotoGeoExplorer.Services.DialogService.ShowMapImageSaveFilePickerAsync"] = "OS の保存ダイアログ。実ウィンドウとユーザー操作が必要",
        ["PhotoGeoExplorer.Services.HelpHtmlWindowController"] = "ヘルプウィンドウの制御。実ウィンドウが必要",
        ["PhotoGeoExplorer.Services.HelpService"] = "ヘルプウィンドウの表示。実ウィンドウが必要",
        ["PhotoGeoExplorer.Services.UpdateCheckDialogService"] = "ダイアログの表示。実ウィンドウが必要"
    };

    // coverlet.runsettings の除外設定。値は設定ファイルに書いた文字列そのもの
    private static readonly Dictionary<string, string> AllowedCoverletExclusions = new(StringComparer.Ordinal)
    {
        ["Exclude"] = "[xunit.*]*,[*.Tests]*,[*.E2E]*",
        ["ExcludeByAttribute"] = "ExcludeFromCodeCoverage,GeneratedCodeAttribute,CompilerGeneratedAttribute",
        ["ExcludeByFile"] = "**/*.g.cs;**/*.g.i.cs;**/*.designer.cs"
    };

    [Fact]
    public void CodecovIgnoreMatchesTheAllowlist()
    {
        var actual = ReadCodecovIgnores(File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "codecov.yml")));

        AssertSameSet(AllowedCodecovIgnores.Keys, actual, "codecov.yml の ignore");
    }

    [Fact]
    public void ExcludeFromCodeCoverageAttributeIsUsedOnlyOnAllowlistedMembers()
    {
        var actual = new[] { typeof(MapPaneViewModel).Assembly, typeof(AppSettings).Assembly }
            .SelectMany(FindExcludedMembers)
            .ToArray();

        AssertSameSet(AllowedExcludedMembers.Keys, actual, "[ExcludeFromCodeCoverage]");
    }

    [Fact]
    public void CoverletExclusionsMatchTheAllowlist()
    {
        var configuration = XDocument.Load(Path.Combine(FindRepositoryRoot(), "coverlet.runsettings"))
            .Descendants("Configuration")
            .Single();
        var actual = configuration.Elements()
            .Where(element => element.Name.LocalName.StartsWith("Exclude", StringComparison.Ordinal))
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);

        AssertSameSet(
            AllowedCoverletExclusions.Select(pair => $"{pair.Key}={pair.Value}"),
            actual.Select(pair => $"{pair.Key}={pair.Value}"),
            "coverlet.runsettings の除外設定");
    }

    [Fact]
    public void EveryAllowlistEntryHasAReason()
    {
        Assert.All(
            AllowedCodecovIgnores.Concat(AllowedExcludedMembers),
            pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{pair.Key} の理由が空です。"));
    }

    [Theory]
    [InlineData("ignore:\n  - \"a/**\"\n  # comment\n  - 'b.cs'\n  - c.cs\n\nflags:\n  - \"not-ignore\"", "a/**|b.cs|c.cs")]
    [InlineData("coverage:\n  status: {}\nignore:\n  - \"only.cs\"", "only.cs")]
    [InlineData("coverage:\n  status: {}", "")]
    public void ReadCodecovIgnoresReadsOnlyTheTopLevelIgnoreList(string yaml, string expected)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var actual = ReadCodecovIgnores(yaml.Split('\n'));

        Assert.Equal(expected, string.Join('|', actual));
    }

    [Theory]
    [InlineData("ignore:\n  - [\"a\", \"b\"]")]
    [InlineData("ignore: [\"a\"]")]
    [InlineData("ignore:\n  nested:\n    - a")]
    public void ReadCodecovIgnoresRejectsFormsItCannotRead(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        Assert.Throws<InvalidDataException>(() => ReadCodecovIgnores(yaml.Split('\n')));
    }

    // YAML 全体は解釈しない。codecov.yml で使っている「トップレベルの ignore: と、その下の文字列の並び」だけを読み、
    // それ以外の書き方は読み落とさずに失敗させる
    private static List<string> ReadCodecovIgnores(IEnumerable<string> lines)
    {
        var ignores = new List<string>();
        var inIgnore = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                if (line.StartsWith("ignore:", StringComparison.Ordinal) && line != "ignore:")
                {
                    throw new InvalidDataException($"codecov.yml の ignore を読めません: {line}");
                }

                inIgnore = line == "ignore:";
                continue;
            }

            if (!inIgnore)
            {
                continue;
            }

            var match = IgnoreItemPattern().Match(line);
            if (!match.Success)
            {
                throw new InvalidDataException($"codecov.yml の ignore を読めません: {line}");
            }

            ignores.Add(match.Groups["value"].Value);
        }

        return ignores;
    }

    private static IEnumerable<string> FindExcludedMembers(Assembly assembly)
    {
        const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsDefined(typeof(ExcludeFromCodeCoverageAttribute), inherit: false))
            {
                yield return type.FullName!;
            }

            foreach (var member in type.GetMembers(DeclaredMembers))
            {
                if (member is not Type && member.IsDefined(typeof(ExcludeFromCodeCoverageAttribute), inherit: false))
                {
                    yield return $"{type.FullName}.{member.Name}";
                }
            }
        }
    }

    private static void AssertSameSet(IEnumerable<string> allowed, IEnumerable<string> actual, string description)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var actualList = actual.ToList();
        var notAllowed = actualList.Where(item => !allowedSet.Contains(item)).Order(StringComparer.Ordinal).ToList();
        var stale = allowedSet.Except(actualList, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var duplicated = actualList.GroupBy(item => item, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(
            notAllowed.Count == 0 && stale.Count == 0 && duplicated.Count == 0,
            $"{description} が許可リストと一致しません。除外を変える場合は CoverageExclusionRatchetTests の許可リストを理由つきで更新してください。"
            + $" 許可リストにない除外: [{string.Join(", ", notAllowed)}]"
            + $" / 許可リストにあるが使われていない: [{string.Join(", ", stale)}]"
            + $" / 重複: [{string.Join(", ", duplicated)}]");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PhotoGeoExplorer.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"PhotoGeoExplorer.sln が見つかりません（起点: {AppContext.BaseDirectory}）。");
    }

    [GeneratedRegex("""^\s+-\s+(?:"(?<value>[^"]+)"|'(?<value>[^']+)'|(?<value>[^\s"'\[\]{}#][^#]*?))\s*$""")]
    private static partial Regex IgnoreItemPattern();
}
