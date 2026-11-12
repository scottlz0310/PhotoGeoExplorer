namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 座標から、その地点を含みうる都道府県を求める
/// </summary>
internal interface IPrefectureLocator
{
    /// <returns>候補の都道府県コード（2 桁）。県境付近では隣接する都道府県も含む。日本国外なら空</returns>
    IReadOnlyList<string> FindCandidates(double latitude, double longitude);
}
