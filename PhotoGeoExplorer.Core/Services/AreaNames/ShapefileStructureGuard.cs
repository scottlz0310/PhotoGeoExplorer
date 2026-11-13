using System.Buffers.Binary;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// Shapefile を読み込むライブラリに渡す前に、ファイルの中身が自身のサイズや値の範囲と矛盾していないかを確かめる。
/// 破損・悪意のあるファイルでは、点数やレコード数が大きい、座標が NaN や巨大な値になるなどして、
/// ライブラリがそれをもとに巨大な配列を確保しメモリを使い果たす（ランダムにバイトを壊す実験では、1 回の読み込みで 38GB）。
/// 確保するサイズがファイルのサイズを超えないこと、座標が緯度経度の範囲に収まることを、先に保証する。
/// 対象は、小地域の境界データであるポリゴン（Polygon・PolygonZ・PolygonM）だけ
/// </summary>
internal static class ShapefileStructureGuard
{
    private const int ShpHeaderLength = 100;
    private const int ShpFileCode = 9994;
    private const int ShpRecordHeaderLength = 8;
    private const int PolygonContentPrefixLength = 44;
    private const int DbfFixedHeaderLength = 32;
    private const int ChunkBytes = 16 * 1024;

    // Polygon、PolygonZ、PolygonM
    private static readonly int[] PolygonShapeTypes = [5, 15, 25];

    /// <exception cref="AreaDataFormatException">ファイルの中身が、自身のサイズや値の範囲と矛盾している</exception>
    /// <exception cref="IOException">ファイルを読めなかった</exception>
    public static void Validate(string shapefilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(shapefilePath);

        ValidateShp(shapefilePath);
        ValidateDbf(Path.ChangeExtension(shapefilePath, ".dbf"));
    }

    private static void ValidateShp(string path)
    {
        using var stream = File.OpenRead(path);
        var length = stream.Length;
        if (length < ShpHeaderLength)
        {
            throw Corrupt(path, "the .shp file is shorter than its header");
        }

        Span<byte> header = stackalloc byte[ShpHeaderLength];
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadInt32BigEndian(header) != ShpFileCode)
        {
            throw Corrupt(path, "the .shp file does not start with the shapefile file code");
        }

        // ヘッダーのファイル長（16 ビットワード単位）が実際より長ければ、末尾が切り詰められている
        var declaredBytes = (long)BinaryPrimitives.ReadInt32BigEndian(header[24..]) * 2;
        if (declaredBytes < ShpHeaderLength || declaredBytes > length)
        {
            throw Corrupt(path, "the file length in the .shp header does not match the file");
        }

        var fileShapeType = BinaryPrimitives.ReadInt32LittleEndian(header[32..]);
        if (!PolygonShapeTypes.Contains(fileShapeType))
        {
            throw new AreaDataFormatException(
                AreaDataFormatError.NoRegions,
                $"The shapefile is not a polygon shapefile (shape type {fileShapeType}): {path}");
        }

        if (!AreBoundsValid(header[36..]))
        {
            throw Corrupt(path, "the bounding box in the .shp header is outside the valid longitude/latitude range");
        }

        var offset = (long)ShpHeaderLength;
        Span<byte> record = stackalloc byte[ShpRecordHeaderLength + PolygonContentPrefixLength];
        while (offset + ShpRecordHeaderLength <= length)
        {
            stream.Position = offset;
            stream.ReadExactly(record[..ShpRecordHeaderLength]);

            // レコードの内容の長さは 16 ビットワード単位
            var contentBytes = (long)BinaryPrimitives.ReadInt32BigEndian(record[4..]) * 2;
            var contentStart = offset + ShpRecordHeaderLength;
            if (contentBytes < 4 || contentStart + contentBytes > length)
            {
                throw Corrupt(path, $"a record at offset {offset} does not fit in the .shp file");
            }

            var prefix = (int)Math.Min(contentBytes, PolygonContentPrefixLength);
            var content = record.Slice(ShpRecordHeaderLength, prefix);
            stream.ReadExactly(content);
            ValidatePolygonRecord(path, offset, stream, contentStart, contentBytes, content, fileShapeType);
            offset = contentStart + contentBytes;
        }

        // 最後のレコードの後に、レコードにならない端数のバイトが残っていない
        if (offset != length)
        {
            throw Corrupt(path, $"the .shp file has {length - offset} trailing bytes that do not form a record");
        }
    }

    private static void ValidatePolygonRecord(
        string path,
        long recordOffset,
        FileStream stream,
        long contentStart,
        long contentBytes,
        ReadOnlySpan<byte> prefix,
        int fileShapeType)
    {
        var shapeType = BinaryPrimitives.ReadInt32LittleEndian(prefix);

        // 空の図形（Null）は許される。それ以外は、ファイルの図形種別と同じでなければならない
        if (shapeType == 0)
        {
            return;
        }

        if (shapeType != fileShapeType)
        {
            throw Corrupt(path, $"the record at offset {recordOffset} has a different shape type ({shapeType}) than the file ({fileShapeType})");
        }

        // 種別(4) + 外接矩形(32) + 部分数(4) + 点数(4) + 部分の開始位置(4 × 部分数) + 点(16 × 点数)
        if (prefix.Length < PolygonContentPrefixLength)
        {
            throw Corrupt(path, $"the record at offset {recordOffset} is too short for a polygon");
        }

        var parts = BinaryPrimitives.ReadInt32LittleEndian(prefix[36..]);
        var points = BinaryPrimitives.ReadInt32LittleEndian(prefix[40..]);
        if (parts < 0 || points < 0 || PolygonContentPrefixLength + (parts * 4L) + (points * 16L) > contentBytes)
        {
            throw Corrupt(path, $"the record at offset {recordOffset} claims more parts or points than it contains");
        }

        if (!AreBoundsValid(prefix[4..]))
        {
            throw Corrupt(path, $"the bounding box of the record at offset {recordOffset} is outside the valid longitude/latitude range");
        }

        var partsStart = contentStart + PolygonContentPrefixLength;
        ValidatePartStarts(path, recordOffset, stream, partsStart, parts, points);
        ValidatePoints(path, recordOffset, stream, partsStart + (parts * 4L), points);
    }

    // 部分の開始位置は、0 から始まり、増え続け、点の範囲に収まる
    private static void ValidatePartStarts(string path, long recordOffset, FileStream stream, long start, int parts, int points)
    {
        if (parts == 0)
        {
            return;
        }

        stream.Position = start;
        var buffer = new byte[Math.Min(parts * 4L, ChunkBytes)];
        var previous = -1;
        var remaining = parts;
        while (remaining > 0)
        {
            var count = Math.Min(remaining, buffer.Length / 4);
            stream.ReadExactly(buffer, 0, count * 4);
            for (var i = 0; i < count; i++)
            {
                var index = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(i * 4));
                var isFirst = previous < 0;
                if ((isFirst && index != 0) || (!isFirst && index <= previous) || index >= points)
                {
                    throw Corrupt(path, $"the part start positions of the record at offset {recordOffset} are invalid");
                }

                previous = index;
            }

            remaining -= count;
        }
    }

    private static void ValidatePoints(string path, long recordOffset, FileStream stream, long start, int points)
    {
        if (points == 0)
        {
            return;
        }

        stream.Position = start;
        var buffer = new byte[Math.Min(points * 16L, ChunkBytes)];
        var remaining = points;
        while (remaining > 0)
        {
            var count = Math.Min(remaining, buffer.Length / 16);
            stream.ReadExactly(buffer, 0, count * 16);
            for (var i = 0; i < count; i++)
            {
                var x = BinaryPrimitives.ReadDoubleLittleEndian(buffer.AsSpan(i * 16));
                var y = BinaryPrimitives.ReadDoubleLittleEndian(buffer.AsSpan((i * 16) + 8));
                if (!IsLongitude(x) || !IsLatitude(y))
                {
                    throw Corrupt(path, $"a coordinate of the record at offset {recordOffset} is outside the valid longitude/latitude range");
                }
            }

            remaining -= count;
        }
    }

    // 外接矩形（Xmin、Ymin、Xmax、Ymax の 4 つの倍精度）
    private static bool AreBoundsValid(ReadOnlySpan<byte> bounds)
        => IsLongitude(BinaryPrimitives.ReadDoubleLittleEndian(bounds))
            && IsLatitude(BinaryPrimitives.ReadDoubleLittleEndian(bounds[8..]))
            && IsLongitude(BinaryPrimitives.ReadDoubleLittleEndian(bounds[16..]))
            && IsLatitude(BinaryPrimitives.ReadDoubleLittleEndian(bounds[24..]));

    // NaN と無限大は、範囲の比較がどちらも成り立たないので、あわせて弾かれる
    private static bool IsLongitude(double value) => value is >= -180 and <= 180;

    private static bool IsLatitude(double value) => value is >= -90 and <= 90;

    private static void ValidateDbf(string path)
    {
        using var stream = File.OpenRead(path);
        var length = stream.Length;
        if (length < DbfFixedHeaderLength + 1)
        {
            throw Corrupt(path, "the .dbf file is shorter than its header");
        }

        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        var recordCount = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
        if (headerLength <= DbfFixedHeaderLength || headerLength > length)
        {
            throw Corrupt(path, "the .dbf header length does not fit in the file");
        }

        if (recordLength == 0 || headerLength + ((long)recordCount * recordLength) > length)
        {
            throw Corrupt(path, "the .dbf record count and record length do not fit in the file");
        }
    }

    private static AreaDataFormatException Corrupt(string path, string reason)
        => new(AreaDataFormatError.CorruptData, $"The shapefile is corrupt ({reason}): {path}");
}
