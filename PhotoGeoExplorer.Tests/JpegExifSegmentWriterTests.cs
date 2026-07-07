using System.Collections.Generic;
using System.IO;
using System.Threading;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class JpegExifSegmentWriterTests
{
    private static readonly byte[] Soi = { 0xFF, 0xD8 };
    private static readonly byte[] Eoi = { 0xFF, 0xD9 };

    private static readonly byte[] App0Segment = BuildSegment(0xE0, new byte[]
    {
        (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
    });

    private static readonly byte[] ScanHeader = { 0x00, 0x01, 0x00, 0x00, 0x00 };
    private static readonly byte[] EntropyData = { 0x12, 0x34, 0x56 };
    private static readonly byte[] SosAndEoi = Concat(BuildSegment(0xDA, ScanHeader), EntropyData, Eoi);

    private static readonly byte[] OldExifPayload = BuildExifPayload(new byte[] { 0xAA, 0xBB });
    private static readonly byte[] OldExifSegment = BuildSegment(0xE1, OldExifPayload);

    [Fact]
    public void Write_InsertsExifSegment_AfterApp0_WhenNoExifPresent()
    {
        var input = Concat(Soi, App0Segment, SosAndEoi);
        var exifPayload = BuildExifPayload(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        var expected = Concat(Soi, App0Segment, BuildSegment(0xE1, exifPayload), SosAndEoi);

        var actual = RunWrite(input, exifPayload);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Write_InsertsExifSegment_BeforeFirstNonApp0Segment_WhenApp0Present()
    {
        var app2Segment = BuildSegment(0xE2, new byte[] { 0x01, 0x02 });
        var input = Concat(Soi, App0Segment, app2Segment, SosAndEoi);
        var exifPayload = BuildExifPayload(new byte[] { 0x0A, 0x0B });
        var expected = Concat(Soi, App0Segment, BuildSegment(0xE1, exifPayload), app2Segment, SosAndEoi);

        var actual = RunWrite(input, exifPayload);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Write_ReplacesExistingExifSegment_WithNewPayload()
    {
        var input = Concat(Soi, App0Segment, OldExifSegment, SosAndEoi);
        var newExifPayload = BuildExifPayload(new byte[] { 0x99 });
        var expected = Concat(Soi, App0Segment, BuildSegment(0xE1, newExifPayload), SosAndEoi);

        var actual = RunWrite(input, newExifPayload);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Write_RemovesExistingExifSegment_WhenPayloadIsNull()
    {
        var input = Concat(Soi, App0Segment, OldExifSegment, SosAndEoi);
        var expected = Concat(Soi, App0Segment, SosAndEoi);

        var actual = RunWrite(input, exifPayload: null);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Write_CopiesStreamUnchanged_WhenNoExifPresentAndPayloadIsNull()
    {
        var input = Concat(Soi, App0Segment, SosAndEoi);

        var actual = RunWrite(input, exifPayload: null);

        Assert.Equal(input, actual);
    }

    [Fact]
    public void Write_InsertsExifSegment_BeforeEoi_WhenNoOtherSegmentsPresent()
    {
        var input = Concat(Soi, Eoi);
        var exifPayload = BuildExifPayload(new byte[] { 0x01 });
        var expected = Concat(Soi, BuildSegment(0xE1, exifPayload), Eoi);

        var actual = RunWrite(input, exifPayload);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Write_ReturnsFalse_WhenExifPayloadExceedsSegmentSizeLimit()
    {
        var input = Concat(Soi, Eoi);
        var oversizedPayload = new byte[ushort.MaxValue];

        var result = TryRunWrite(input, oversizedPayload, out _);

        Assert.False(result);
    }

    public static IEnumerable<object[]> MalformedOrTruncatedStreams()
    {
        yield return new object[] { System.Array.Empty<byte>(), "empty stream" };
        yield return new object[] { new byte[] { 0x00, 0x01, 0x02, 0x03 }, "missing SOI marker" };
        yield return new object[] { new byte[] { 0xFF, 0xD8 }, "SOI only, no marker follows" };
        yield return new object[] { new byte[] { 0xFF, 0xD8, 0x12, 0x34 }, "marker prefix is not 0xFF" };
        yield return new object[] { Concat(Soi, new byte[] { 0xFF, 0xE0, 0x00 }), "segment length truncated" };
        yield return new object[] { Concat(Soi, new byte[] { 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }), "segment data truncated" };
        yield return new object[] { Concat(Soi, new byte[] { 0xFF, 0xDA, 0x00, 0x0C, 0x00, 0x01 }), "scan header truncated" };
    }

    [Theory]
    [MemberData(nameof(MalformedOrTruncatedStreams))]
    public void Write_ReturnsFalse_ForMalformedOrTruncatedStreams(byte[] input, string reason)
    {
        var result = TryRunWrite(input, exifPayload: null, out _);

        Assert.False(result, reason);
    }

    private static byte[] RunWrite(byte[] input, byte[]? exifPayload)
    {
        var result = TryRunWrite(input, exifPayload, out var output);
        Assert.True(result);
        return output;
    }

    private static bool TryRunWrite(byte[] input, byte[]? exifPayload, out byte[] output)
    {
        using var inputStream = new MemoryStream(input);
        using var outputStream = new MemoryStream();
        var result = JpegExifSegmentWriter.Write(inputStream, outputStream, exifPayload, CancellationToken.None);
        output = outputStream.ToArray();
        return result;
    }

    private static byte[] BuildExifPayload(byte[] tiffBytes) => Concat(JpegExifSegmentWriter.ExifHeader, tiffBytes);

    private static byte[] BuildSegment(byte marker, byte[] data)
    {
        var length = (ushort)(data.Length + 2);
        return Concat(new byte[] { 0xFF, marker, (byte)(length >> 8), (byte)length }, data);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new List<byte>();
        foreach (var part in parts)
        {
            result.AddRange(part);
        }

        return result.ToArray();
    }
}
