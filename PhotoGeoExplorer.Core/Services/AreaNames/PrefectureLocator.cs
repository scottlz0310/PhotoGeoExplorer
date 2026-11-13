using System.Reflection;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.IO;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 同梱した簡略化ポリゴンで、座標から候補の都道府県を求める。
/// ポリゴンは元の範囲より外側へ膨らませてあるため、県境付近の地点は隣接する都道府県の両方が候補になる
/// </summary>
internal sealed class PrefectureLocator : IPrefectureLocator
{
    internal const string EmbeddedResourceName = "PhotoGeoExplorer.Core.Resources.prefecture-polygons.tsv";

    private readonly Lazy<STRtree<PrefectureArea>> _index;

    /// <param name="openData">「都道府県コード、タブ、WKT」を 1 行ずつ並べたデータを開く。最初の検索まで呼ばれない</param>
    public PrefectureLocator(Func<TextReader> openData)
    {
        ArgumentNullException.ThrowIfNull(openData);
        _index = new Lazy<STRtree<PrefectureArea>>(() => Load(openData));
    }

    /// <summary>アプリに同梱したデータを使う</summary>
    public static PrefectureLocator CreateFromEmbeddedData()
        => new(() =>
        {
            var stream = typeof(PrefectureLocator).Assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidOperationException($"Embedded resource not found: {EmbeddedResourceName}");
            return new StreamReader(stream);
        });

    public IReadOnlyList<string> FindCandidates(double latitude, double longitude)
    {
        var coordinate = new Coordinate(longitude, latitude);
        var nearby = _index.Value.Query(new Envelope(coordinate));
        if (nearby.Count == 0)
        {
            return [];
        }

        var point = nearby[0].Polygon.Geometry.Factory.CreatePoint(coordinate);
        return nearby
            .Where(area => area.Polygon.Covers(point))
            .Select(area => area.Code)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static STRtree<PrefectureArea> Load(Func<TextReader> openData)
    {
        var index = new STRtree<PrefectureArea>();
        var wktReader = new WKTReader();
        using var reader = openData();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('\t', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new InvalidDataException($"Invalid prefecture polygon data at line {lineNumber}.");
            }

            Geometry geometry;
            try
            {
                geometry = wktReader.Read(line[(separator + 1)..]);
            }
            catch (ParseException ex)
            {
                throw new InvalidDataException($"Invalid prefecture polygon data at line {lineNumber}.", ex);
            }

            index.Insert(geometry.EnvelopeInternal, new PrefectureArea(line[..separator], PreparedGeometryFactory.Prepare(geometry)));
        }

        // 最初の検索で構築されると、複数スレッドからの同時検索で競合する
        index.Build();
        return index;
    }

    private sealed record PrefectureArea(string Code, IPreparedGeometry Polygon);
}
