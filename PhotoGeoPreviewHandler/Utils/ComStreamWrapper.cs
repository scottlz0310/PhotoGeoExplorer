using System.IO;
using System.Runtime.InteropServices.ComTypes;

namespace PhotoGeoPreviewHandler.Services;

/// <summary>
/// Wraps a COM IStream as a .NET Stream for easier handling.
/// </summary>
internal class ComStreamWrapper : Stream
{
    private readonly IStream _comStream;
    private long _position;

    public ComStreamWrapper(IStream comStream)
    {
        _comStream = comStream ?? throw new ArgumentNullException(nameof(comStream));
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            STATSTG stat;
            _comStream.Stat(out stat, 1); // STATFLAG_NONAME
            return stat.cbSize;
        }
    }

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
        _comStream.Commit(0);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (offset != 0)
        {
            byte[] tempBuffer = new byte[count];
            int bytesRead = ReadInternal(tempBuffer, count);
            Array.Copy(tempBuffer, 0, buffer, offset, bytesRead);
            return bytesRead;
        }
        else
        {
            return ReadInternal(buffer, count);
        }
    }

    private int ReadInternal(byte[] buffer, int count)
    {
        IntPtr bytesReadPtr = IntPtr.Zero;
        try
        {
            _comStream.Read(buffer, count, bytesReadPtr);
            int bytesRead = bytesReadPtr.ToInt32();
            _position += bytesRead;
            return bytesRead;
        }
        catch
        {
            // If native read fails, try managed approach
            unsafe
            {
                int bytesRead;
                _comStream.Read(buffer, count, (IntPtr)(&bytesRead));
                _position += bytesRead;
                return bytesRead;
            }
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        int dwOrigin = origin switch
        {
            SeekOrigin.Begin => 0,    // STREAM_SEEK_SET
            SeekOrigin.Current => 1,  // STREAM_SEEK_CUR
            SeekOrigin.End => 2,      // STREAM_SEEK_END
            _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
        };

        IntPtr newPositionPtr = IntPtr.Zero;
        _comStream.Seek(offset, dwOrigin, newPositionPtr);

        unsafe
        {
            long newPosition;
            _comStream.Seek(offset, dwOrigin, (IntPtr)(&newPosition));
            _position = newPosition;
            return newPosition;
        }
    }

    public override void SetLength(long value)
    {
        _comStream.SetSize(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Writing is not supported for preview handlers.");
    }

    protected override void Dispose(bool disposing)
    {
        // Note: Do not release the COM object here, as it's owned by the caller
        base.Dispose(disposing);
    }
}
