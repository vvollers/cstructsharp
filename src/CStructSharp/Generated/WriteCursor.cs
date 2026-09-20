namespace CStructSharp.Generated;

using System;
using CStructSharp.Diagnostics;
using CStructSharp.Writing;

/// <summary>
///     The writing state a generated <c>Serialize</c> method carries through one operation: the destination, the
///     position, the <see cref="WriteOptions"/> snapshot, and the accounting the runtime writer performs - the
///     total byte budget, array and string limits, nesting depth - with the runtime's failure texts. Bytes the
///     cursor skips (padding) are zero-filled, as the runtime writer does.
/// </summary>
/// <remarks>
///     This is an advanced surface, public so the code the <c>[CStructLayout]</c> generator emits can use it.
/// </remarks>
public ref struct WriteCursor
{
    private readonly Span<byte> destination;
    private readonly WriteOptions options;
    private int position;
    private int nestingDepth;

    /// <summary>Creates a cursor at the start of <paramref name="destination"/>.</summary>
    /// <param name="destination">The bytes to write into; a value that does not fit fails with the runtime's capacity message.</param>
    /// <param name="options">The write options; <see langword="null"/> uses the documented defaults.</param>
    public WriteCursor(Span<byte> destination, WriteOptions? options = null)
    {
        this.destination = destination;
        this.options = CStructElementWriterState.SnapshotWriteOptions(options);
        CStructElementWriterState.ValidateWriteOptions(this.options);
    }

    /// <summary>Gets or sets the offset of the next byte to write.</summary>
    public int Position
    {
        readonly get => this.position;
        set
        {
            if (value < 0 || value > this.destination.Length)
            {
                throw this.Fail(WriteFailures.DestinationCapacity, null);
            }

            this.position = value;
        }
    }

    /// <summary>Gets the number of bytes written so far (the position; a serialize never seeks backwards).</summary>
    public readonly int Length => this.position;

    /// <summary>Gets how pointer addresses are interpreted.</summary>
    public readonly PointerAddressingMode AddressingMode => this.options.AddressingMode;

    /// <summary>Gets the origin relative pointers are measured from.</summary>
    public readonly long Origin => this.options.Origin;

    /// <summary>Gets the configured array element limit.</summary>
    public readonly int MaxArrayElements => this.options.MaxArrayElements;

    /// <summary>Gets the configured encoded-string byte limit.</summary>
    public readonly long MaxStringBytes => this.options.MaxStringBytes;

    /// <summary>
    ///     Reserves <paramref name="count"/> bytes for a member: checks the total byte budget and the destination
    ///     capacity, advances, and returns the slice to encode into.
    /// </summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="path">The member path, for the diagnostics.</param>
    /// <returns>The reserved bytes.</returns>
    /// <exception cref="CStructWriteLimitException">The total byte budget is exceeded.</exception>
    /// <exception cref="CStructWriteException">The destination is too small.</exception>
    public Span<byte> Reserve(int count, string path)
    {
        long end = (long)this.position + count;
        if (count < 0 || end > this.options.MaxTotalBytesWritten)
        {
            throw this.FailLimit(WriteFailures.TotalBytesLimit, path);
        }

        if (end > this.destination.Length)
        {
            throw this.Fail(WriteFailures.DestinationCapacity, path);
        }

        Span<byte> bytes = this.destination.Slice(this.position, count);
        this.position += count;
        return bytes;
    }

    /// <summary>Writes zero padding.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="path">The member path, for the diagnostics.</param>
    public void Pad(int count, string path) => this.Reserve(count, path).Clear();

    /// <summary>Pads to the next multiple of <paramref name="alignment"/> measured from <paramref name="origin"/>.</summary>
    /// <param name="alignment">The alignment in bytes.</param>
    /// <param name="origin">The position alignment is measured from.</param>
    /// <param name="path">The member path, for the diagnostics.</param>
    public void Align(int alignment, long origin, string path)
    {
        if (alignment <= 1)
        {
            return;
        }

        long relative = this.position - origin;
        long padding = (alignment - (relative % alignment)) % alignment;
        this.Pad((int)padding, path);
    }

    /// <summary>Enters a nested struct or union, enforcing <c>MaxNestingDepth</c>.</summary>
    /// <param name="path">The member path, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The nesting limit is exceeded.</exception>
    public void EnterComposite(string path)
    {
        if (this.nestingDepth >= this.options.MaxNestingDepth)
        {
            throw this.FailLimit(WriteFailures.NestingLimit, path);
        }

        this.nestingDepth++;
    }

    /// <summary>Leaves the current struct or union.</summary>
    public void ExitComposite() => this.nestingDepth--;

    /// <summary>Validates an array's element count against <c>MaxArrayElements</c>.</summary>
    /// <param name="count">The number of elements the caller supplies.</param>
    /// <param name="fieldName">The layout field name, for the message.</param>
    /// <param name="path">The member path, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The count exceeds the limit.</exception>
    public readonly void RequireArrayLength(long count, string fieldName, string path)
    {
        if (count < 0 || count > this.options.MaxArrayElements)
        {
            throw this.FailLimit(WriteFailures.ArrayLengthLimit(fieldName), path);
        }
    }

    /// <summary>Validates a string's encoded length (terminator included) against <c>MaxStringBytes</c>.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="path">The member path, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The string exceeds the limit.</exception>
    public readonly void RequireStringBytes(long count, string path)
    {
        if (count < 0 || count > this.options.MaxStringBytes)
        {
            throw this.FailLimit(WriteFailures.StringBytesLimit, path);
        }
    }

    /// <summary>A write failure at the current position, with the runtime's path and offset context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="path">The member path, for the diagnostics.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructWriteException Fail(string message, string? path)
    {
        var exception = new CStructWriteException(message);
        exception.AttachContext(path, this.position);
        return exception;
    }

    /// <summary>A limit failure at the current position, with the runtime's path and offset context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="path">The member path, for the diagnostics.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructWriteLimitException FailLimit(string message, string? path)
    {
        var exception = new CStructWriteLimitException(message);
        exception.AttachContext(path, this.position);
        return exception;
    }
}
