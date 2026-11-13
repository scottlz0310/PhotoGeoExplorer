using System.Globalization;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaTree;

namespace PhotoGeoExplorer.Services.Export;

/// <summary>
/// 写真を書き出すときの、出力先からの相対パスを決める。ファイルの実処理（コピー）は行わない
/// </summary>
internal static class ExportPlanBuilder
{
    // リネーム出力のファイル名の、字名の部分の長さの上限（名前全体が長くなりすぎないように）
    private const int RenameLabelMaxLength = 40;

    private const string DateFormat = "yyyyMMdd";

    /// <summary>
    /// 字地名別のフォルダーへのコピーの計画。「市区町村 / 町丁・字 / 元のファイル名」。
    /// 字地名がない写真は、水面・地域データなし・位置情報なしの専用のフォルダーへ入れる。
    /// 同名のファイルが同じフォルダーに重なるときは、「名前 (2).jpg」のように番号を足す
    /// </summary>
    public static IReadOnlyList<ExportPlanItem> BuildFolderCopyPlan(IReadOnlyList<PhotoGeoRecord> records, ExportFolderNames names)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(names);

        var tree = AreaTreeBuilder.Build(records.Select(ToEntry));
        var items = new List<ExportPlanItem>(records.Count);
        foreach (var node in tree)
        {
            if (node.Kind == AreaTreeNodeKind.City)
            {
                var city = ExportNameSanitizer.SanitizeSegment(node.Label);
                foreach (var town in node.Children)
                {
                    var townFolder = ExportNameSanitizer.SanitizeSegment(string.IsNullOrWhiteSpace(town.Label) ? names.Unnamed : town.Label);
                    AddFolderItems(items, town.FilePaths, city, townFolder);
                }
            }
            else
            {
                AddFolderItems(items, node.FilePaths, ExportNameSanitizer.SanitizeSegment(GetSpecialFolderName(node.Kind, names)));
            }
        }

        return MakeUnique(items);
    }

    /// <summary>
    /// リネーム出力の計画。「{日付}_{字名}_{道路名}_{連番}{拡張子}」。日付は撮影日（yyyyMMdd）、字名は町丁・字の名称、
    /// 道路名は利用者が確定した道路名（未確定・なしは <see cref="ExportRenameNames.NoRoad"/>。第 1 候補は、確定した値と見分けられないので入れない）。
    /// 連番は、同じ日付・字名・道路名の写真の中で、撮影日時・パスの順に 001 から振る。
    /// 撮影日時がなければ日付の代わりに <see cref="ExportRenameNames.NoDate"/>、字地名がなければ種類に応じた字名を使う
    /// </summary>
    public static IReadOnlyList<ExportPlanItem> BuildRenamePlan(IReadOnlyList<PhotoGeoRecord> records, ExportRenameNames names)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(names);

        var keyed = records
            .Select(record => (Record: record, Date: FormatDate(record, names), Label: GetRenameLabel(record, names), Road: GetRoadLabel(record, names)))
            .OrderBy(item => item.Record.TakenAt ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Record.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Windows は大文字小文字を区別しないので、字名・道路名が大文字小文字だけ違う写真も、同じ連番を共有する
        var sequences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ExportPlanItem>(records.Count);
        foreach (var (record, date, label, road) in keyed)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{date}\u0000{label}\u0000{road}");
            var sequence = sequences.GetValueOrDefault(key) + 1;
            sequences[key] = sequence;
            var extension = Path.GetExtension(record.FilePath);
            var fileName = string.Create(CultureInfo.InvariantCulture, $"{date}_{label}_{road}_{sequence:D3}{extension}");
            items.Add(new ExportPlanItem(record.FilePath, fileName));
        }

        // 連番で重ならないはずだが、最終的な出力先が重ならないことを、フォルダーコピーと同じ手順で保証する
        return MakeUnique(items);
    }

    private static string FormatDate(PhotoGeoRecord record, ExportRenameNames names)
        => record.TakenAt is { } takenAt
            ? takenAt.ToString(DateFormat, CultureInfo.InvariantCulture)
            : ExportNameSanitizer.SanitizeSegment(names.NoDate, RenameLabelMaxLength);

    private static string GetRenameLabel(PhotoGeoRecord record, ExportRenameNames names)
    {
        var label = record switch
        {
            { Kind: PhotoAreaKind.NoLocation } => names.NoLocation,
            { Kind: PhotoAreaKind.NoAreaData } => names.NoAreaData,
            { Area: { IsWater: true } } => names.Water,
            { Area: { } area } => string.IsNullOrWhiteSpace(area.Town) ? names.Unnamed : area.Town,
            _ => names.NoAreaData
        };
        return ExportNameSanitizer.SanitizeSegment(label, RenameLabelMaxLength);
    }

    private static string GetRoadLabel(PhotoGeoRecord record, ExportRenameNames names)
    {
        var label = record.Road is { Status: RoadNameStatus.Confirmed } road
            ? road.Format(names.FormatRouteNumber)
            : names.NoRoad;
        return ExportNameSanitizer.SanitizeSegment(label, RenameLabelMaxLength);
    }

    private static string GetSpecialFolderName(AreaTreeNodeKind kind, ExportFolderNames names)
        => kind switch
        {
            AreaTreeNodeKind.Water => names.Water,
            AreaTreeNodeKind.NoAreaData => names.NoAreaData,
            _ => names.NoLocation
        };

    private static PhotoAreaEntry ToEntry(PhotoGeoRecord record) => new(record.FilePath, record.Kind, record.Area);

    private static void AddFolderItems(List<ExportPlanItem> items, IReadOnlyList<string> filePaths, params string[] folders)
    {
        foreach (var filePath in filePaths)
        {
            items.Add(new ExportPlanItem(filePath, Path.Combine([.. folders, Path.GetFileName(filePath)])));
        }
    }

    // 同じ相対パスが重なったら、「名前 (2).jpg」「名前 (3).jpg」にする（大文字小文字は区別しない）
    private static List<ExportPlanItem> MakeUnique(List<ExportPlanItem> items)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ExportPlanItem>(items.Count);
        foreach (var item in items)
        {
            var candidate = item.RelativePath;
            var directory = Path.GetDirectoryName(candidate) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(candidate);
            var extension = Path.GetExtension(candidate);
            for (var number = 2; !used.Add(candidate); number++)
            {
                candidate = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{stem} ({number}){extension}"));
            }

            result.Add(item with { RelativePath = candidate });
        }

        return result;
    }
}
