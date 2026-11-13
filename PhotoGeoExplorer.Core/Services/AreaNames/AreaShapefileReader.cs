using System.Globalization;
using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using NetTopologySuite.IO.Esri.Shapefiles.Readers;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// e-Stat「国勢調査 小地域（町丁・字等別）境界データ」の Shapefile を読む。
/// 対象は世界測地系・緯度経度の版（平面直角座標系の版は受け付けない）
/// </summary>
internal static class AreaShapefileReader
{
    private const string KeyCodeAttribute = "KEY_CODE";
    private const string PrefectureAttribute = "PREF_NAME";
    private const string CityAttribute = "CITY_NAME";
    private const string TownAttribute = "S_NAME";
    private const string CategoryAttribute = "HCODE";

    // HCODE の分類コード。8101 = 町丁・字等、8154 = 水面調査区
    private const int WaterCategory = 8154;

    private static readonly string[] RequiredAttributes =
    [
        KeyCodeAttribute, PrefectureAttribute, CityAttribute, TownAttribute, CategoryAttribute
    ];

    static AreaShapefileReader()
    {
        // 属性（.dbf）は Shift_JIS。.NET は既定で Shift_JIS を持たない
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 都道府県を KEY_CODE（先頭 2 桁）から判別して読む。全レコードが同じ都道府県でなければ拒否する
    /// </summary>
    /// <exception cref="AreaDataFormatException">小地域の境界データとして読める形式ではない</exception>
    /// <exception cref="IOException">ファイルを読めなかった</exception>
    public static AreaDataset Read(string shapefilePath, int censusYear)
        => ReadCore(shapefilePath, expectedPrefectureCode: null, censusYear);

    /// <summary>
    /// 指定した都道府県のデータとして読む。KEY_CODE が別の都道府県を示すレコードがあれば拒否する
    /// </summary>
    /// <exception cref="AreaDataFormatException">小地域の境界データとして読める形式ではない</exception>
    /// <exception cref="IOException">ファイルを読めなかった</exception>
    public static AreaDataset Read(string shapefilePath, string prefectureCode, int censusYear)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefectureCode);
        return ReadCore(shapefilePath, prefectureCode, censusYear);
    }

    private static AreaDataset ReadCore(string shapefilePath, string? expectedPrefectureCode, int censusYear)
    {
        ArgumentException.ThrowIfNullOrEmpty(shapefilePath);

        try
        {
            return ReadRegions(shapefilePath, expectedPrefectureCode, censusYear);
        }
        catch (Exception ex) when (ex is ShapefileException or NotSupportedException or InvalidDataException)
        {
            // 壊れたファイルを読むと、ライブラリは IOException ではなくこれらを投げる。
            // 呼び出し元が IOException だけを扱えば足りるよう、AreaDataFormatException（IOException）に揃える
            throw new AreaDataFormatException(
                AreaDataFormatError.CorruptData,
                $"The shapefile is corrupt or in an unsupported format: {shapefilePath}",
                ex);
        }
    }

    private static AreaDataset ReadRegions(string shapefilePath, string? expectedPrefectureCode, int censusYear)
    {
        // 壊れた図形は読み飛ばさずに修復する（1 件の不正で都道府県全体を読めなくしない）
        var options = new ShapefileReaderOptions { GeometryBuilderMode = GeometryBuilderMode.FixInvalidShapes };
        using var reader = Shapefile.OpenRead(shapefilePath, options);
        ValidateProjection(reader.Projection, shapefilePath);
        ValidateAttributes(reader, shapefilePath);

        var regions = new List<AreaRegion>(reader.RecordCount);
        while (reader.Read(out var deleted, out Feature feature))
        {
            if (deleted || feature.Geometry is not (Polygon or MultiPolygon) || feature.Geometry.IsEmpty)
            {
                continue;
            }

            regions.Add(new AreaRegion(
                ReadText(feature, KeyCodeAttribute),
                ReadText(feature, PrefectureAttribute),
                ReadText(feature, CityAttribute),
                ReadText(feature, TownAttribute),
                isWater: ReadCategory(feature) == WaterCategory,
                feature.Geometry));
        }

        if (regions.Count == 0)
        {
            throw new AreaDataFormatException(
                AreaDataFormatError.NoRegions,
                $"The shapefile contains no area polygons: {shapefilePath}");
        }

        var prefectureCode = DeterminePrefectureCode(regions, expectedPrefectureCode, shapefilePath);
        return new AreaDataset(prefectureCode, censusYear, regions);
    }

    // KEY_CODE は「都道府県 2 桁 + 市区町村 3 桁 + 町丁字」。先頭 2 桁を都道府県コードとして使う
    private static string DeterminePrefectureCode(List<AreaRegion> regions, string? expected, string shapefilePath)
    {
        var codes = regions
            .Select(region => region.KeyCode.Length >= 2 ? region.KeyCode[..2] : string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (codes.Exists(code => !PrefectureCatalog.IsValidCode(code)))
        {
            throw new AreaDataFormatException(
                AreaDataFormatError.InvalidKeyCode,
                $"The shapefile has a KEY_CODE that does not start with a prefecture code (01-47): {shapefilePath}");
        }

        if (codes.Count > 1 || (expected is not null && codes[0] != expected))
        {
            var expectation = expected is null ? string.Empty : $" ({expected})";
            throw new AreaDataFormatException(
                AreaDataFormatError.MixedPrefectures,
                $"The shapefile does not consist of a single prefecture{expectation}: {shapefilePath}");
        }

        return codes[0];
    }

    private static void ValidateProjection(string? projection, string shapefilePath)
    {
        if (string.IsNullOrWhiteSpace(projection))
        {
            throw new AreaDataFormatException(
                AreaDataFormatError.MissingProjection,
                $"The shapefile has no .prj file, so its coordinate system cannot be verified: {shapefilePath}");
        }

        // 緯度経度の座標系は GEOGCS（WKT1）または GEOGCRS（WKT2）で始まる。投影座標系は PROJCS / PROJCRS
        var trimmed = projection.TrimStart();
        if (!trimmed.StartsWith("GEOGCS", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("GEOGCRS", StringComparison.OrdinalIgnoreCase))
        {
            throw new AreaDataFormatException(
                AreaDataFormatError.UnsupportedProjection,
                $"The shapefile is not in a geographic (latitude/longitude) coordinate system: {shapefilePath}");
        }
    }

    private static void ValidateAttributes(ShapefileReader reader, string shapefilePath)
    {
        var names = reader.Fields.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = RequiredAttributes.Where(name => !names.Contains(name)).ToList();
        if (missing.Count > 0)
        {
            throw new AreaDataFormatException(
                AreaDataFormatError.MissingAttribute,
                $"The shapefile lacks required attributes ({string.Join(", ", missing)}): {shapefilePath}");
        }
    }

    private static string ReadText(Feature feature, string name)
        => Convert.ToString(feature.Attributes[name], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static int ReadCategory(Feature feature)
        => int.TryParse(ReadText(feature, CategoryAttribute), NumberStyles.Number, CultureInfo.InvariantCulture, out var category)
            ? category
            : 0;
}
