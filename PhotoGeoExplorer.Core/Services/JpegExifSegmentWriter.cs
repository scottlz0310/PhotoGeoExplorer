using System;
using System.IO;
using System.Threading;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// JPEG セグメント構造の走査・書き換えを担う（ImageSharp 非依存の純粋ストリーム処理）
/// </summary>
internal static class JpegExifSegmentWriter
{
    public static readonly byte[] ExifHeader = { 0x45, 0x78, 0x69, 0x66, 0x00, 0x00 };

    public static bool Write(
        Stream input,
        Stream output,
        byte[]? exifPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        if (!TryReadByte(input, out var firstByte) || !TryReadByte(input, out var secondByte))
        {
            return false;
        }

        if (firstByte != 0xFF || secondByte != 0xD8)
        {
            return false;
        }

        output.WriteByte((byte)firstByte);
        output.WriteByte((byte)secondByte);

        var exifPayloadAvailable = exifPayload is not null;
        var exifWritten = false;
        var insertedAfterApp0 = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryReadByte(input, out var markerPrefix))
            {
                return false;
            }

            if (markerPrefix != 0xFF)
            {
                return false;
            }

            int marker;
            do
            {
                if (!TryReadByte(input, out marker))
                {
                    return false;
                }
            }
            while (marker == 0xFF);

            if (marker == 0xD9)
            {
                if (!exifWritten && exifPayloadAvailable)
                {
                    if (!TryWriteExifSegment(output, exifPayload!))
                    {
                        return false;
                    }

                    exifWritten = true;
                }

                output.WriteByte(0xFF);
                output.WriteByte((byte)marker);
                return true;
            }

            if (marker == 0xDA)
            {
                if (!exifWritten && exifPayloadAvailable)
                {
                    if (!TryWriteExifSegment(output, exifPayload!))
                    {
                        return false;
                    }

                    exifWritten = true;
                }

                output.WriteByte(0xFF);
                output.WriteByte((byte)marker);

                if (!TryReadUInt16(input, out var scanLength) || scanLength < 2)
                {
                    return false;
                }

                WriteUInt16(output, scanLength);

                if (!CopyBytes(input, output, scanLength - 2))
                {
                    return false;
                }

                input.CopyTo(output);
                return true;
            }

            if ((marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                output.WriteByte(0xFF);
                output.WriteByte((byte)marker);
                continue;
            }

            if (!TryReadUInt16(input, out var segmentLength) || segmentLength < 2)
            {
                return false;
            }

            var payloadLength = segmentLength - 2;
            var segmentData = new byte[payloadLength];
            if (!TryReadExact(input, segmentData, payloadLength))
            {
                return false;
            }

            var isExifSegment = marker == 0xE1 && IsExifSegment(segmentData);
            if (isExifSegment)
            {
                if (!exifWritten && exifPayloadAvailable)
                {
                    if (!TryWriteExifSegment(output, exifPayload!))
                    {
                        return false;
                    }

                    exifWritten = true;
                }

                continue;
            }

            if (!exifWritten && exifPayloadAvailable && !insertedAfterApp0 && marker != 0xE0)
            {
                if (!TryWriteExifSegment(output, exifPayload!))
                {
                    return false;
                }

                exifWritten = true;
                insertedAfterApp0 = true;
            }

            output.WriteByte(0xFF);
            output.WriteByte((byte)marker);
            WriteUInt16(output, segmentLength);
            output.Write(segmentData, 0, segmentData.Length);
        }
    }

    private static bool TryWriteExifSegment(Stream output, byte[] exifPayload)
    {
        var segmentLength = exifPayload.Length + 2;
        if (segmentLength > ushort.MaxValue)
        {
            return false;
        }

        output.WriteByte(0xFF);
        output.WriteByte(0xE1);
        WriteUInt16(output, (ushort)segmentLength);
        output.Write(exifPayload, 0, exifPayload.Length);
        return true;
    }

    private static bool IsExifSegment(byte[] segmentData)
    {
        if (segmentData.Length < ExifHeader.Length)
        {
            return false;
        }

        for (var i = 0; i < ExifHeader.Length; i++)
        {
            if (segmentData[i] != ExifHeader[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadByte(Stream stream, out int value)
    {
        value = stream.ReadByte();
        return value != -1;
    }

    private static bool TryReadUInt16(Stream stream, out ushort value)
    {
        value = 0;
        if (!TryReadByte(stream, out var highByte) || !TryReadByte(stream, out var lowByte))
        {
            return false;
        }

        value = (ushort)((highByte << 8) | lowByte);
        return true;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static bool CopyBytes(Stream input, Stream output, int byteCount)
    {
        var buffer = new byte[8192];
        var remaining = byteCount;

        while (remaining > 0)
        {
            var readCount = Math.Min(buffer.Length, remaining);
            var bytesRead = input.Read(buffer, 0, readCount);
            if (bytesRead <= 0)
            {
                return false;
            }

            output.Write(buffer, 0, bytesRead);
            remaining -= bytesRead;
        }

        return true;
    }

    private static bool TryReadExact(Stream stream, byte[] buffer, int length)
    {
        var offset = 0;
        while (offset < length)
        {
            var bytesRead = stream.Read(buffer, offset, length - offset);
            if (bytesRead == 0)
            {
                return false;
            }

            offset += bytesRead;
        }

        return true;
    }
}
