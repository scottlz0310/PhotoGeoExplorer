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
        var oversizedPayload = BuildExifPayload(new byte[ushort.MaxValue - JpegExifSegmentWriter.ExifHeader.Length]);

        var result = TryRunWrite(input, oversizedPayload, out _);

        Assert.False(result);
    }

    [Fact]
    public void Write_ReturnsFalse_WhenExifPayloadDoesNotStartWithExifHeader()
    {
        var input = Concat(Soi, Eoi);
        var invalidPayload = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        var result = TryRunWrite(input, invalidPayload, out _);

        Assert.False(result);
    }

    [Fact]
    public void Write_ThrowsOperationCanceledException_WhenCancelledDuringScanDataCopy()
    {
        var largeEntropyData = new byte[20000];
        var input = Concat(Soi, BuildSegment(0xDA, ScanHeader), largeEntropyData, Eoi);

        using var cts = new CancellationTokenSource();
        using var baseInputStream = new MemoryStream(input);
        using var inputStream = new CancelOnNthChunkReadStream(baseInputStream, cts, cancelOnReadNumber: 2);
        using var outputStream = new MemoryStream();

        Assert.Throws<OperationCanceledException>(() =>
            JpegExifSegmentWriter.Write(inputStream, outputStream, exifPayload: null, cts.Token));
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

    /// <summary>
    /// チャンク単位（count &gt; 1）の Read 呼び出しを数え、指定回数目でキャンセルを発火させる。
    /// ReadByte 由来の count=1 呼び出しは JPEG マーカー走査で多数発生するため対象外とする。
    /// </summary>
    private sealed class CancelOnNthChunkReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly int _cancelOnReadNumber;
        private int _chunkReadCount;

        public CancelOnNthChunkReadStream(Stream inner, CancellationTokenSource cancellationTokenSource, int cancelOnReadNumber)
        {
            _inner = inner;
            _cancellationTokenSource = cancellationTokenSource;
            _cancelOnReadNumber = cancelOnReadNumber;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _inner.Read(buffer, offset, count);
            if (count > 1 && ++_chunkReadCount == _cancelOnReadNumber)
            {
                _cancellationTokenSource.Cancel();
            }

            return bytesRead;
        }

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _cancellationTokenSource.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
