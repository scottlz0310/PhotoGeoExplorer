namespace PhotoGeoExplorer.Models;

internal enum AreaTreeNodeKind
{
    /// <summary>市区町村</summary>
    City,

    /// <summary>町丁・字</summary>
    Town,

    /// <summary>水面（海・湖など）。住所としての字地名がない</summary>
    Water,

    /// <summary>位置情報はあるが、字地名を判定できない（地域データ未導入・日本国外など）</summary>
    NoAreaData,

    /// <summary>位置情報がない</summary>
    NoLocation
}

/// <summary>
/// 字地名のツリーの 1 ノード
/// </summary>
/// <param name="Kind">ノードの種類</param>
/// <param name="Key">同じ種類の兄弟の中で一意なキー（市区町村は KEY_CODE の先頭 5 桁、町丁・字は KEY_CODE）。特別なノードは空</param>
/// <param name="Label">表示名。市区町村が複数の都道府県にまたがって同名のときは「中央区（東京都）」のように都道府県名を添える。特別なノードは空（表示側が文言を持つ）</param>
/// <param name="FilePaths">このノードとその子孫に含まれる写真のパス（昇順）</param>
/// <param name="Children">子ノード</param>
internal sealed record AreaTreeNode(
    AreaTreeNodeKind Kind,
    string Key,
    string Label,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<AreaTreeNode> Children)
{
    public int Count => FilePaths.Count;
}
