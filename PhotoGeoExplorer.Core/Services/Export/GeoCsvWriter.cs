using System.Buffers;
using System.Globalization;
using System.Text;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaTree;

namespace PhotoGeoExplorer.Services.Export;

/// <summary>
/// CSV の見出しと、「判定」列の値（表示言語に合わせて呼び出し側が与える）
/// </summary>
internal sealed record GeoCsvLabels(
    string FileName,
    string Prefecture,
    string City,
    string Town,
    string KeyCode,
    string Latitude,
    string Longitude,
    string TakenAt,
    string Status,
    string StatusResolved,
    string StatusWater,
    string StatusNoAreaData,
    string StatusNoLocation);

/// <summary>
/// 台帳用の写真のメタデータ（ファイル名・字地名・緯度経度・撮影日時）を CSV に書き出す。
/// Excel で文字化けしないよう UTF-8（BOM つき）、行末は CRLF。列の追加は末尾にする（取り込み側の列の位置を変えない）
/// </summary>
internal static class GeoCsvWriter
{
    // 数式として解釈されうる先頭の文字。Excel で開いたときに、セルの内容が実行されないようにする
    private static readonly SearchValues<char> FormulaPrefixes = SearchValues.Create("=+-@\t\r");
    private static readonly SearchValues<char> SpecialCharacters = SearchValues.Create(",\"\r\n");

    public static void Write(Stream output, IEnumerable<PhotoGeoRecord> records, GeoCsvLabels labels)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(labels);

        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true) { NewLine = "\r\n" };
        WriteRow(
            writer,
            [
                Text(labels.FileName), Text(labels.Prefecture), Text(labels.City), Text(labels.Town), Text(labels.KeyCode),
                Text(labels.Latitude), Text(labels.Longitude), Text(labels.TakenAt), Text(labels.Status)
            ]);
        foreach (var record in records)
        {
            var area = GetLandArea(record);
            WriteRow(
                writer,
                [
                    Text(Path.GetFileName(record.FilePath)),
                    Text(area?.Prefecture),
                    Text(area?.City),
                    Text(area?.Town),
                    Text(area?.KeyCode),
                    Number(record.Coordinate?.Latitude),
                    Number(record.Coordinate?.Longitude),
                    Text(record.TakenAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    Text(GetStatus(record, labels))
                ]);
        }
    }

    // 水面には住所としての字地名がないので、住所の列は空にする
    private static AreaName? GetLandArea(PhotoGeoRecord record)
        => record is { Kind: PhotoAreaKind.Resolved, Area: { IsWater: false } land } ? land : null;

    private static string GetStatus(PhotoGeoRecord record, GeoCsvLabels labels)
        => record switch
        {
            { Kind: PhotoAreaKind.NoLocation } => labels.StatusNoLocation,
            { Kind: PhotoAreaKind.NoAreaData } => labels.StatusNoAreaData,
            { Area: { IsWater: true } } => labels.StatusWater,
            _ => labels.StatusResolved
        };

    // 文字列の列。数式として解釈されうる先頭には、' を足して無効にする
    private static string Text(string? value)
        => value is { Length: > 0 } text && FormulaPrefixes.Contains(text[0]) ? "'" + text : value ?? string.Empty;

    // 数値の列（負の緯度経度の - は、数値なので無効にしない）。小数点は常にドット
    private static string Number(double? value)
        => value is { } number ? number.ToString("F6", CultureInfo.InvariantCulture) : string.Empty;

    private static void WriteRow(StreamWriter writer, string[] cells)
        => writer.WriteLine(string.Join(',', cells.Select(Escape)));

    private static string Escape(string value)
        => value.AsSpan().IndexOfAny(SpecialCharacters) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
