using System.Text;

namespace PhotoGeoExplorer.Services.Export;

/// <summary>
/// 字地名などの文字列を、Windows のフォルダー名・ファイル名として使える形にする
/// </summary>
internal static class ExportNameSanitizer
{
    internal const int DefaultMaxLength = 80;
    internal const string Replacement = "_";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// 使えない文字を <c>_</c> に置き換え、前後の空白とドットを取り、予約名を避け、長さを <paramref name="maxLength"/> 以内にする。
    /// 結果は空にならない
    /// </summary>
    public static string SanitizeSegment(string? name, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Replacement;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsControl(character) || Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        var cleaned = Truncate(builder.ToString().Trim().Trim('.').Trim(), maxLength);
        if (cleaned.Length == 0)
        {
            return Replacement;
        }

        // 予約名は、拡張子がついていても使えない（CON.txt など）
        var stem = cleaned.Contains('.', StringComparison.Ordinal) ? cleaned[..cleaned.IndexOf('.', StringComparison.Ordinal)] : cleaned;
        return ReservedNames.Contains(stem.TrimEnd()) ? Truncate(Replacement + cleaned, maxLength) : cleaned;
    }

    // UTF-16 のサロゲートペアの途中で切らない
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..length].TrimEnd().TrimEnd('.');
    }
}
