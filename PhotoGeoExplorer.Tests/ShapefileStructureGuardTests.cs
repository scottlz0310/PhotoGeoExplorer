using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;
using static PhotoGeoExplorer.Tests.AreaShapefileTestData;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 破損・悪意のある Shapefile を、ライブラリに渡す前に弾けることを確かめる。
/// ランダムにバイトを壊す実験（約 9,600 件）では、点数や座標の値が壊れると、1 回の読み込みで 38GB を確保した。
/// ここでは、その型ごとに、壊し方を決めて固定する
/// </summary>
public sealed class ShapefileStructureGuardTests : IDisposable
{
    // ライブラリが書き出す、ポリゴン 1 件目のレコード（.shp の 100 バイト目から）の構造:
    //   100: レコード番号(4)  104: 内容の長さ(4)  108: 図形種別(4)  112: 外接矩形(32)
    //   144: 部分数(4)  148: 点数(4)  152: 部分の開始位置(4 × 部分数)  156: 点(16 × 点数)
    private const int RecordContentLengthOffset = 104;
    private const int RecordShapeTypeOffset = 108;
    private const int RecordBoundsOffset = 112;
    private const int PartCountOffset = 144;
    private const int PointCountOffset = 148;
    private const int FirstPartStartOffset = 152;
    private const int FirstPointOffset = 156;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_ShapefileStructureGuardTests_" + Guid.NewGuid().ToString("N"));

    public ShapefileStructureGuardTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ValidDataPasses()
    {
        var path = WriteValidShapefile();

        var dataset = AreaShapefileReader.Read(path, "31", 2020);

        Assert.Equal(2, dataset.RegionCount);
    }

    [Theory]
    // 点数・部分数が、レコードの長さに収まらない（巨大・負・1 つ多い）
    [InlineData(PointCountOffset, int.MaxValue)]
    [InlineData(PointCountOffset, -1)]
    [InlineData(PointCountOffset, 6)]
    [InlineData(PartCountOffset, int.MaxValue)]
    [InlineData(PartCountOffset, -1)]
    [InlineData(PartCountOffset, 2)]
    // 部分の開始位置: 最初が 0 ではない、点の範囲を超える、負
    [InlineData(FirstPartStartOffset, 1)]
    [InlineData(FirstPartStartOffset, 5)]
    [InlineData(FirstPartStartOffset, -1)]
    // レコードの長さ（16 ビットワード単位）が、ファイルを超える・短すぎる・負
    [InlineData(RecordContentLengthOffset, int.MaxValue, true)]
    [InlineData(RecordContentLengthOffset, 1, true)]
    [InlineData(RecordContentLengthOffset, -1, true)]
    // 図形種別: 未知、ファイルの種別と異なる
    [InlineData(RecordShapeTypeOffset, 99)]
    [InlineData(RecordShapeTypeOffset, 3)]
    public void ShpIntegerFieldsThatContradictTheFileAreRejectedBeforeReading(int offset, int value, bool bigEndian = false)
    {
        var path = WriteValidShapefile();
        Mutate(path, ".shp", bytes => WriteInt32(bytes, offset, value, bigEndian));

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    // NaN・無限大・巨大な値・緯度経度の範囲外の座標（最初の点の X と Y）
    [InlineData(FirstPointOffset, double.NaN)]
    [InlineData(FirstPointOffset, double.PositiveInfinity)]
    [InlineData(FirstPointOffset, double.NegativeInfinity)]
    [InlineData(FirstPointOffset, 1e300)]
    [InlineData(FirstPointOffset, 180.0001)]
    [InlineData(FirstPointOffset, -180.0001)]
    [InlineData(FirstPointOffset + 8, double.NaN)]
    [InlineData(FirstPointOffset + 8, 90.0001)]
    [InlineData(FirstPointOffset + 8, -90.0001)]
    [InlineData(FirstPointOffset + 8, 1e300)]
    // 外接矩形（レコードの Xmin）と、ファイルのヘッダーの外接矩形（Xmin）
    [InlineData(RecordBoundsOffset, double.NaN)]
    [InlineData(RecordBoundsOffset, 1e300)]
    [InlineData(36, double.NaN)]
    [InlineData(36, 1e300)]
    public void CoordinatesOutsideTheLongitudeAndLatitudeRangeAreRejectedBeforeReading(int offset, double value)
    {
        var path = WriteValidShapefile();
        Mutate(path, ".shp", bytes => BitConverter.GetBytes(value).CopyTo(bytes, offset));

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    // 境界ちょうどの値は有効
    [InlineData(FirstPointOffset, 180.0)]
    [InlineData(FirstPointOffset, -180.0)]
    [InlineData(FirstPointOffset + 8, 90.0)]
    [InlineData(FirstPointOffset + 8, -90.0)]
    public void CoordinatesOnTheBoundaryOfTheRangeAreAccepted(int offset, double value)
    {
        var path = WriteValidShapefile();
        Mutate(path, ".shp", bytes => BitConverter.GetBytes(value).CopyTo(bytes, offset));

        // ガードを通る（その後のジオメトリの修復で、どう扱われるかは問わない）
        var exception = Record.Exception(() => ShapefileStructureGuard.Validate(path));

        Assert.Null(exception);
    }

    [Theory]
    // ヘッダーが短すぎる・ファイルコードが違う・ファイル長が実際より長い（切り詰め）
    [InlineData("truncated-before-the-header")]
    [InlineData("truncated-inside-the-first-record-header")]
    [InlineData("truncated-inside-the-first-record")]
    [InlineData("truncated-between-records")]
    [InlineData("trailing-bytes")]
    [InlineData("wrong-file-code")]
    [InlineData("declared-length-too-long")]
    [InlineData("declared-length-too-short-for-the-header")]
    public void ShpFilesThatAreTruncatedOrInconsistentWithTheirHeaderAreRejected(string damage)
    {
        var path = WriteValidShapefile();
        var shp = Path.ChangeExtension(path, ".shp");
        var original = File.ReadAllBytes(shp);
        var damaged = damage switch
        {
            "truncated-before-the-header" => original[..50],
            "truncated-inside-the-first-record-header" => original[..102],

            // ヘッダーのファイル長は、元のまま（実際より長い）
            "truncated-inside-the-first-record" => original[..200],
            "truncated-between-records" => original[..(100 + 8 + 128)],
            "trailing-bytes" => [.. original, 1, 2, 3],
            "wrong-file-code" => WithInt32(original, 0, 1234, bigEndian: true),
            "declared-length-too-long" => WithInt32(original, 24, int.MaxValue, bigEndian: true),
            "declared-length-too-short-for-the-header" => WithInt32(original, 24, 10, bigEndian: true),
            _ => throw new ArgumentOutOfRangeException(nameof(damage))
        };
        File.WriteAllBytes(shp, damaged);

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
    }

    [Theory]
    // ポリゴンの Shapefile ではない（点・線）。小地域のポリゴンがない、として拒否する
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(31)]
    public void ShpFilesOfAnotherShapeTypeAreRejectedAsHavingNoPolygons(int shapeType)
    {
        var path = WriteValidShapefile();
        Mutate(path, ".shp", bytes => WriteInt32(bytes, 32, shapeType, bigEndian: false));

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.NoRegions, exception.Error);
    }

    [Theory]
    // レコード数・ヘッダー長・レコード長が、ファイルのサイズと矛盾する
    [InlineData(4, uint.MaxValue, 4)]
    [InlineData(4, 100_000u, 4)]
    [InlineData(8, 0xFFFFu, 2)]
    [InlineData(8, 32u, 2)]
    [InlineData(8, 0u, 2)]
    [InlineData(10, 0u, 2)]
    [InlineData(10, 0xFFFFu, 2)]
    public void DbfHeaderValuesThatContradictTheFileAreRejectedBeforeReading(int offset, uint value, int size)
    {
        var path = WriteValidShapefile();
        Mutate(path, ".dbf", bytes =>
        {
            var encoded = BitConverter.GetBytes(value);
            Array.Copy(encoded, 0, bytes, offset, size);
        });

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(31)]
    public void DbfFilesThatAreTooShortForTheirHeaderAreRejected(int length)
    {
        var path = WriteValidShapefile();
        var dbf = Path.ChangeExtension(path, ".dbf");
        File.WriteAllBytes(dbf, File.ReadAllBytes(dbf)[..length]);

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
    }

    // 以下は、ライブラリが読む途中で、IOException ではない例外を投げる破損。ガードでは見つからないので、変換で拾う

    [Fact]
    public void ADbfWithAnImpossibleUpdateDateIsReportedAsCorrupt()
    {
        var path = WriteValidShapefile();

        // 更新日（年 = 1900 + 126、月 = 99、日 = 99）
        Mutate(path, ".dbf", bytes =>
        {
            bytes[1] = 126;
            bytes[2] = 99;
            bytes[3] = 99;
        });

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void ADbfWithDuplicateFieldNamesIsReportedAsCorrupt()
    {
        var path = WriteValidShapefile();

        // 2 つ目のフィールドの名前（11 バイト）を、1 つ目と同じにする
        Mutate(path, ".dbf", bytes => Array.Copy(bytes, 32, bytes, 32 + 32, 11));

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void ADbfWithANumericFieldThatIsNotANumberIsReportedAsCorrupt()
    {
        var path = WriteValidShapefile();

        // 1 件目の HCODE（数値フィールド）を、数字ではない文字にする
        Mutate(path, ".dbf", bytes =>
        {
            var (offset, length) = FindFieldInFirstRecord(bytes, "HCODE");
            for (var i = 0; i < length; i++)
            {
                bytes[offset + i] = (byte)(i == length - 1 ? '@' : ' ');
            }
        });

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
        Assert.IsType<FormatException>(exception.InnerException);
    }

    [Fact]
    public void ADbfWithAnInvalidFieldLengthIsReportedAsCorrupt()
    {
        var path = WriteValidShapefile();

        // 1 つ目のフィールドの長さ（記述子の 16 バイト目）を 0 にする
        Mutate(path, ".dbf", bytes => bytes[32 + 16] = 0);

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
    }

    [Theory]
    // .shx と .prj の破損: .shx は使われない。.prj は座標系として拒否される
    [InlineData(".shx")]
    public void AGarbledShxDoesNotPreventReading(string extension)
    {
        var path = WriteValidShapefile();
        Mutate(path, extension, bytes => Array.Fill(bytes, (byte)0xFF));

        var dataset = AreaShapefileReader.Read(path, "31", 2020);

        Assert.Equal(2, dataset.RegionCount);
    }

    private string WriteValidShapefile()
        => WriteShapefile(
            _directory,
            "r2ka31",
            GeographicProjection,
            AllAttributes,
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0),
            CreateFeature("312010870", "東品治町", hcode: 8101, west: 134.1));

    private static void Mutate(string shapefilePath, string extension, Action<byte[]> mutate)
    {
        var target = Path.ChangeExtension(shapefilePath, extension);
        var bytes = File.ReadAllBytes(target);
        mutate(bytes);
        File.WriteAllBytes(target, bytes);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value, bool bigEndian)
    {
        var encoded = BitConverter.GetBytes(value);
        if (bigEndian)
        {
            Array.Reverse(encoded);
        }

        encoded.CopyTo(bytes, offset);
    }

    private static byte[] WithInt32(byte[] original, int offset, int value, bool bigEndian)
    {
        var copy = (byte[])original.Clone();
        WriteInt32(copy, offset, value, bigEndian);
        return copy;
    }

    // dBASE のフィールド記述子（32 バイトごと。名前 11 バイト、長さは 16 バイト目）から、1 件目のレコード内の位置を求める
    private static (int Offset, int Length) FindFieldInFirstRecord(byte[] dbf, string fieldName)
    {
        var headerLength = BitConverter.ToUInt16(dbf, 8);
        var offsetInRecord = 1;
        for (var descriptor = 32; descriptor + 32 <= headerLength - 1; descriptor += 32)
        {
            var name = Encoding.ASCII.GetString(dbf, descriptor, 11).TrimEnd('\0');
            var length = dbf[descriptor + 16];
            if (string.Equals(name, fieldName, StringComparison.Ordinal))
            {
                return (headerLength + offsetInRecord, length);
            }

            offsetInRecord += length;
        }

        throw new InvalidOperationException($"Field {fieldName} not found in the .dbf header.");
    }
}
