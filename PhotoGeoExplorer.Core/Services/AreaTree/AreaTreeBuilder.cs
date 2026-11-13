using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.AreaTree;

/// <summary>
/// 写真の字地名の判定結果を、「市区町村 &gt; 町丁・字」のツリーに束ねる。
/// 市区町村・町丁・字は KEY_CODE の順、末尾に水面・地域データなし・位置情報なしのノードを置く
/// </summary>
internal static class AreaTreeBuilder
{
    // KEY_CODE は「都道府県 2 桁 + 市区町村 3 桁 + 町丁字」
    private const int CityCodeLength = 5;

    public static IReadOnlyList<AreaTreeNode> Build(IEnumerable<PhotoAreaEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var land = new List<(PhotoAreaEntry Entry, AreaName Area)>();
        var water = new List<string>();
        var noAreaData = new List<string>();
        var noLocation = new List<string>();
        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case PhotoAreaKind.NoLocation:
                    noLocation.Add(entry.FilePath);
                    break;
                case PhotoAreaKind.NoAreaData:
                    noAreaData.Add(entry.FilePath);
                    break;
                case PhotoAreaKind.Resolved when entry.Area is { IsWater: true }:
                    water.Add(entry.FilePath);
                    break;
                case PhotoAreaKind.Resolved when entry.Area is { } area:
                    land.Add((entry, area));
                    break;
                default:
                    throw new ArgumentException($"A resolved entry needs an area: {entry.FilePath}", nameof(entries));
            }
        }

        var nodes = BuildCities(land);
        AddSpecialNode(nodes, AreaTreeNodeKind.Water, water);
        AddSpecialNode(nodes, AreaTreeNodeKind.NoAreaData, noAreaData);
        AddSpecialNode(nodes, AreaTreeNodeKind.NoLocation, noLocation);
        return nodes;
    }

    private static List<AreaTreeNode> BuildCities(List<(PhotoAreaEntry Entry, AreaName Area)> land)
    {
        var cities = land
            .GroupBy(item => GetCityKey(item.Area.KeyCode), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (Key: group.Key, Items: group.ToList()))
            .ToList();

        // 同名の市区町村が複数の都道府県にまたがるとき（○○県中央区と△△県中央区など）だけ、都道府県名を添える
        var duplicatedNames = cities
            .GroupBy(city => city.Items[0].Area.City, StringComparer.Ordinal)
            .Where(group => group.Select(city => city.Items[0].Area.Prefecture).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        return cities
            .Select(city =>
            {
                var first = city.Items[0].Area;
                var label = duplicatedNames.Contains(first.City) ? $"{first.City}（{first.Prefecture}）" : first.City;
                var towns = city.Items
                    .GroupBy(item => item.Area.KeyCode, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new AreaTreeNode(
                        AreaTreeNodeKind.Town,
                        group.Key,
                        group.First().Area.Town,
                        SortPaths(group.Select(item => item.Entry.FilePath)),
                        []))
                    .ToList();
                return new AreaTreeNode(
                    AreaTreeNodeKind.City,
                    city.Key,
                    label,
                    SortPaths(city.Items.Select(item => item.Entry.FilePath)),
                    towns);
            })
            .ToList();
    }

    private static void AddSpecialNode(List<AreaTreeNode> nodes, AreaTreeNodeKind kind, List<string> paths)
    {
        if (paths.Count > 0)
        {
            nodes.Add(new AreaTreeNode(kind, string.Empty, string.Empty, SortPaths(paths), []));
        }
    }

    private static string GetCityKey(string keyCode)
        => keyCode.Length > CityCodeLength ? keyCode[..CityCodeLength] : keyCode;

    private static List<string> SortPaths(IEnumerable<string> paths)
        => paths.Order(StringComparer.OrdinalIgnoreCase).ToList();
}
