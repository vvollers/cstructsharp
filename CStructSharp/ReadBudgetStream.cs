namespace CStructSharp;

using System;
using System.IO;

/// <summary>Counts bytes read through a caller-owned stream and enforces one read-like operation's byte budget.</summary>
internal sealed class ReadBudgetStream : Stream
{
    private readonly Stream inner;
    private readonly long maxTotalBytesRead;
    private long bytesRead;

    /// <summary>Wraps a readable stream without taking ownership of it.</summary>
    public ReadBudgetStream(Stream inner, ReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.maxTotalBytesRead = options.MaxTotalBytesRead;
        this.MaxStringBytes = options.MaxStringBytes;
    }

    /// <summary>Wraps a readable stream using operation-owned limit values.</summary>
    public ReadBudgetStream(Stream inner, long maxStringBytes, long maxTotalBytesRead)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.maxTotalBytesRead = maxTotalBytesRead;
        this.MaxStringBytes = maxStringBytes;
    }

    /// <summary>Gets the configured per-string encoded-byte budget.</summary>
    public long MaxStringBytes { get; }

    public override bool CanRead => this.inner.CanRead;

    public override bool CanSeek => this.inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            try
            {
                return this.inner.Length;
            }
            catch (IOException exception)
            {
                throw this.CreateReadFailure("Cannot read the source stream length.", exception);
            }
        }
    }

    public override long Position
    {
        get
        {
            try
            {
                return this.inner.Position;
            }
            catch (IOException exception)
            {
                throw this.CreateReadFailure("Cannot read the source stream position.", exception);
            }
        }

        set
        {
            try
            {
                this.inner.Position = value;
            }
            catch (IOException exception)
            {
                throw this.CreateReadFailure("Cannot change the source stream position.", exception);
            }
        }
    }

    /// <summary>Flushes the wrapped stream without closing or otherwise taking ownership of it.</summary>
    public override void Flush()
    {
        try
        {
            this.inner.Flush();
        }
        catch (IOException exception)
        {
            throw this.CreateReadFailure("Cannot flush the source stream.", exception);
        }
    }

    /// <summary>Reads bytes while charging the operation-wide budget only for bytes actually returned.</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        int read;
        try
        {
            read = this.inner.Read(buffer, offset, count);
        }
        catch (IOException exception)
        {
            throw this.CreateReadFailure("Cannot read from the source stream.", exception);
        }

        this.RecordRead(read);
        return read;
    }

    /// <summary>Reads span data while charging the operation-wide budget only for bytes actually returned.</summary>
    public override int Read(Span<byte> buffer)
    {
        int read;
        try
        {
            read = this.inner.Read(buffer);
        }
        catch (IOException exception)
        {
            throw this.CreateReadFailure("Cannot read from the source stream.", exception);
        }

        this.RecordRead(read);
        return read;
    }

    /// <summary>Reads one byte while applying the same budget as bulk reads.</summary>
    public override int ReadByte()
    {
        int value;
        try
        {
            value = this.inner.ReadByte();
        }
        catch (IOException exception)
        {
            throw this.CreateReadFailure("Cannot read from the source stream.", exception);
        }

        if (value >= 0)
        {
            this.RecordRead(1);
        }

        return value;
    }

    /// <summary>Seeks in the wrapped stream without resetting the total physical-read budget.</summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        try
        {
            return this.inner.Seek(offset, origin);
        }
        catch (IOException exception)
        {
            throw this.CreateReadFailure("Cannot seek in the source stream.", exception);
        }
    }

    /// <summary>Rejects length changes because this wrapper advertises a read-only stream contract.</summary>
    public override void SetLength(long value)
    {
        throw new NotSupportedException("The parse budget stream is read-only.");
    }

    /// <summary>Rejects writes because this wrapper is only used by parse operations.</summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("The parse budget stream is read-only.");
    }

    /// <summary>Leaves the caller-owned stream open when parser state is released.</summary>
    protected override void Dispose(bool disposing)
    {
        // Intentionally do not dispose this.inner; public CStruct methods do not take stream ownership.
    }

    /// <summary>Raises a layout-specific read error when one operation exceeds its configured byte budget.</summary>
    private void RecordRead(int count)
    {
        if (count <= 0)
        {
            return;
        }

        try
        {
            this.bytesRead = checked(this.bytesRead + count);
        }
        catch (OverflowException exception)
        {
            throw new CStructReadLimitException(
                "Read operation exceeded the supported read-byte accounting range.",
                exception);
        }

        if (this.bytesRead > this.maxTotalBytesRead)
        {
            throw new CStructReadLimitException("Read operation exceeded the configured total read-byte limit.");
        }
    }

    /// <summary>Creates a physical source error and records its position when the stream can still report it.</summary>
    private CStructReadException CreateReadFailure(string message, IOException exception)
    {
        var result = new CStructReadException(message, exception);
        result.AttachContext(offset: this.TryGetPosition());
        return result;
    }

    /// <summary>Obtains diagnostic position context without allowing a secondary stream failure to hide the first.</summary>
    private long? TryGetPosition()
    {
        try
        {
            return this.inner.Position;
        }
        catch (Exception)
        {
            // Preserve the physical read failure even if the stream can no longer report diagnostic context.
            return null;
        }
    }
}
