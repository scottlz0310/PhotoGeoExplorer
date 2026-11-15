namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定の「地域データ」で、取得する都道府県を選ぶ項目
/// </summary>
internal sealed record PrefectureOption(string Code, string Name)
{
    public override string ToString() => Name;
}
