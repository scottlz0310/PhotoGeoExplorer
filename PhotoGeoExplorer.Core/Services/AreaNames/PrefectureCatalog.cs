namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 都道府県コード（JIS X 0401。2 桁）と名称
/// </summary>
internal static class PrefectureCatalog
{
    private static readonly string[] Names =
    [
        "北海道", "青森県", "岩手県", "宮城県", "秋田県", "山形県", "福島県",
        "茨城県", "栃木県", "群馬県", "埼玉県", "千葉県", "東京都", "神奈川県",
        "新潟県", "富山県", "石川県", "福井県", "山梨県", "長野県", "岐阜県", "静岡県", "愛知県",
        "三重県", "滋賀県", "京都府", "大阪府", "兵庫県", "奈良県", "和歌山県",
        "鳥取県", "島根県", "岡山県", "広島県", "山口県",
        "徳島県", "香川県", "愛媛県", "高知県",
        "福岡県", "佐賀県", "長崎県", "熊本県", "大分県", "宮崎県", "鹿児島県", "沖縄県"
    ];

    public static IEnumerable<string> Codes
        => Enumerable.Range(1, Names.Length).Select(number => number.ToString("00", System.Globalization.CultureInfo.InvariantCulture));

    /// <returns>コードが 01〜47 の 2 桁でなければ false</returns>
    public static bool TryGetName(string? code, out string name)
    {
        name = string.Empty;
        if (code is not { Length: 2 }
            || !char.IsAsciiDigit(code[0])
            || !char.IsAsciiDigit(code[1]))
        {
            return false;
        }

        var number = ((code[0] - '0') * 10) + (code[1] - '0');
        if (number < 1 || number > Names.Length)
        {
            return false;
        }

        name = Names[number - 1];
        return true;
    }

    public static bool IsValidCode(string? code) => TryGetName(code, out _);
}
