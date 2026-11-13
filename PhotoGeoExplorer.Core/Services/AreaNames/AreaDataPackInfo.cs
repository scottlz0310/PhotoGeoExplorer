namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 端末に取り込み済みの、1 都道府県分の地域データ
/// </summary>
/// <param name="PrefectureCode">都道府県コード（2 桁）</param>
/// <param name="PrefectureName">都道府県名</param>
/// <param name="CensusYear">国勢調査の年（西暦）</param>
/// <param name="SizeBytes">端末上のサイズ</param>
internal sealed record AreaDataPackInfo(string PrefectureCode, string PrefectureName, int CensusYear, long SizeBytes);
