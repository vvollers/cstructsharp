namespace CStructSharp.Generated;

using System;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Writing;

/// <summary>
///     The writing state a generated <c>Serialize</c> method carries through one operation: the destination, the
///     position, the <see cref="WriteOptions"/> snapshot, and the accounting the runtime writer performs - the
///     total byte budget, array and string limits, nesting depth - with the runtime's failure texts and context
///     (the innermost field and its type, the operation path, the offset). Bytes the cursor pads are zero-filled,
///     as the runtime writer does.
/// </summary>
/// <remarks>
///     This is an advanced surface, public so the code the <c>[CStructLayout]</c> generator emits can use it.
/// </remarks>
public ref struct WriteCursor
{
    private readonly Span<byte> destination;
    private readonly WriteOptions options;
    private readonly string? path;
    private int position;
    private int nestingDepth;

    /// <summary>Creates a cursor at the start of <paramref name="destination"/>.</summary>
    /// <param name="destination">The bytes to write into; a value that does not fit fails with the runtime's capacity message.</param>
    /// <param name="options">The write options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="path">The path the operation writes (<c>root</c>), reported by every failure as the runtime does.</param>
    public WriteCursor(Span<byte> destination, WriteOptions? options = null, string? path = null)
    {
        this.destination = destination;
        this.options = CStructElementWriterState.SnapshotWriteOptions(options);
        CStructElementWriterState.ValidateWriteOptions(this.options);
        this.path = path;
    }

    /// <summary>Gets or sets the offset of the next byte to write.</summary>
    public int Position
    {
        readonly get => this.position;
        set
        {
            if (value < 0 || value > this.destination.Length)
            {
                throw this.Fail(WriteFailures.DestinationCapacity, null, null);
            }

            this.position = value;
        }
    }

    /// <summary>Gets the path the operation writes, as reported in diagnostics.</summary>
    public readonly string? Path => this.path;

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
    /// <param name="member">The layout field being written, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The reserved bytes.</returns>
    /// <exception cref="CStructWriteLimitException">The total byte budget is exceeded.</exception>
    /// <exception cref="CStructWriteException">The destination is too small.</exception>
    public Span<byte> Reserve(int count, string member, string? memberType)
    {
        long end = (long)this.position + count;
        if (count < 0 || end > this.options.MaxTotalBytesWritten)
        {
            throw this.FailLimit(WriteFailures.TotalBytesLimit, member, memberType);
        }

        if (end > this.destination.Length)
        {
            throw this.Fail(WriteFailures.DestinationCapacity, member, memberType);
        }

        Span<byte> bytes = this.destination.Slice(this.position, count);
        this.position += count;
        return bytes;
    }

    /// <summary>Writes zero padding.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The layout field the padding belongs to, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void Pad(int count, string member, string? memberType) => this.Reserve(count, member, memberType).Clear();

    /// <summary>Pads to the next multiple of <paramref name="alignment"/> measured from <paramref name="origin"/>.</summary>
    /// <param name="alignment">The alignment in bytes.</param>
    /// <param name="origin">The position alignment is measured from.</param>
    /// <param name="member">The layout field being aligned, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void Align(int alignment, long origin, string member, string? memberType)
    {
        if (alignment <= 1)
        {
            return;
        }

        long relative = this.position - origin;
        long padding = (alignment - (relative % alignment)) % alignment;
        this.Pad((int)padding, member, memberType);
    }

    /// <summary>Enters a nested struct or union, enforcing <c>MaxNestingDepth</c>.</summary>
    /// <param name="member">The composite field being entered, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The nesting limit is exceeded.</exception>
    public void EnterComposite(string member, string? memberType)
    {
        if (this.nestingDepth >= this.options.MaxNestingDepth)
        {
            throw this.FailLimit(WriteFailures.NestingLimit, member, memberType);
        }

        this.nestingDepth++;
    }

    /// <summary>Leaves the current struct or union.</summary>
    public void ExitComposite() => this.nestingDepth--;

    /// <summary>Validates an array's element count against <c>MaxArrayElements</c>.</summary>
    /// <param name="count">The number of elements the caller supplies.</param>
    /// <param name="member">The array field, named in the message and the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The count exceeds the limit.</exception>
    public readonly void RequireArrayLength(long count, string member, string? memberType)
    {
        if (count < 0 || count > this.options.MaxArrayElements)
        {
            throw this.FailLimit(WriteFailures.ArrayLengthLimit(member), member, memberType);
        }
    }

    /// <summary>Validates a string's encoded length (terminator included) against <c>MaxStringBytes</c>.</summary>
    /// <param name="count">The number of encoded bytes.</param>
    /// <param name="member">The string field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The string exceeds the limit.</exception>
    public readonly void RequireStringBytes(long count, string member, string? memberType)
    {
        if (count < 0 || count > this.options.MaxStringBytes)
        {
            throw this.FailLimit(WriteFailures.StringBytesLimit, member, memberType);
        }
    }

    /// <summary>A write failure at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructWriteException Fail(string message, string? member, string? memberType)
    {
        var exception = new CStructWriteException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>A limit failure at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructWriteLimitException FailLimit(string message, string? member, string? memberType)
    {
        var exception = new CStructWriteLimitException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>
    ///     The failure of a value that cannot be encoded as its field: the runtime's text stating what was supplied
    ///     and what the field accepts (<c>Value 300 does not fit: uint8 accepts 0 to 255.</c>).
    /// </summary>
    /// <param name="value">The value the caller supplied.</param>
    /// <param name="typeSpelling">The field's type spelling.</param>
    /// <param name="acceptedRange">The integer range a fixed-width integer accepts (<c>0 to 255</c>), or <see langword="null"/>.</param>
    /// <param name="member">The layout field, for the diagnostics.</param>
    /// <param name="cause">The conversion exception, when one was raised.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructWriteException FailUnwritable(object? value, string typeSpelling, string? acceptedRange, string member, Exception? cause = null)
    {
        ArgumentNullException.ThrowIfNull(typeSpelling);
        string message = WriteFailures.UnwritableValue(value, typeSpelling, acceptedRange);
        var exception = cause is null ? new CStructWriteException(message) : new CStructWriteException(message, cause);
        this.Attach(exception, member, typeSpelling);
        return exception;
    }

    /// <summary>
    ///     The failure of a layout expression evaluated by generated code during a write: the runtime's
    ///     <c>Cannot evaluate {context}: {reason}</c> text around an exception one of the <see cref="Expressions"/>
    ///     operators raised.
    /// </summary>
    /// <param name="exception">The exception the expression raised.</param>
    /// <param name="context">What was being evaluated, as the runtime names it (<c>array length for items</c>).</param>
    /// <param name="member">The layout field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw, or <paramref name="exception"/> itself when it is not an expression failure.</returns>
    public readonly Exception FailExpression(Exception exception, string context, string? member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!LayoutExpressionEvaluator.IsExpressionFailure(exception))
        {
            return exception;
        }

        var failure = new CStructWriteException(LayoutExpressionEvaluator.DescribeFailure(context, exception), exception);
        this.Attach(failure, member, memberType);
        return failure;
    }

    private readonly void Attach(CStructException exception, string? member, string? memberType)
    {
        if (member is not null)
        {
            exception.AttachMember(member, memberType);
        }

        exception.AttachContext(this.path, this.position);
    }
}
