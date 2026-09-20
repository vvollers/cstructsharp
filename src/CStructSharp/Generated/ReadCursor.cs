namespace CStructSharp.Generated;

using System;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;

/// <summary>
///     The reading state a generated <c>Parse</c> method carries through one operation: the source bytes, the
///     position, the <see cref="ReadOptions"/> snapshot, and the accounting the runtime reader performs - the total
///     read-byte budget, array and string limits, nesting and pointer depth - with the runtime's failure texts, so a
///     generated reader and <see cref="CStruct.Parse(ReadOnlySpan{byte}, string?, System.Collections.Generic.IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
///     report the same error for the same bytes. Every failure carries the runtime's context: the innermost field
///     and its type, the operation path, and the position the cursor had reached.
/// </summary>
/// <remarks>
///     This is an advanced surface, public so the code the <c>[CStructLayout]</c> generator emits can use it.
/// </remarks>
public ref struct ReadCursor
{
    private readonly ReadOnlySpan<byte> source;
    private readonly ReadOperationSettings settings;
    private readonly string? path;
    private int position;
    private long bytesRead;
    private int nestingDepth;
    private int pointerDepth;

    /// <summary>Creates a cursor at the start of <paramref name="source"/>.</summary>
    /// <param name="source">The bytes to read; offset 0 is coordinate zero for addresses and diagnostics.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="path">The path the operation reads (<c>root</c>, <c>root.items[1]</c>), reported by every failure as the runtime does.</param>
    public ReadCursor(ReadOnlySpan<byte> source, ReadOptions? options = null, string? path = null)
    {
        this.source = source;
        this.settings = ReadOperationSettings.SnapshotReadOptions(options);
        this.path = path;
    }

    /// <summary>Gets or sets the offset of the next byte to read.</summary>
    public int Position
    {
        readonly get => this.position;
        set
        {
            if (value < 0 || value > this.source.Length)
            {
                throw this.Fail(ReadFailures.OutsideRegion, null, null);
            }

            this.position = value;
        }
    }

    /// <summary>Gets the path the operation reads, as reported in diagnostics.</summary>
    public readonly string? Path => this.path;

    /// <summary>Gets the number of bytes from the position to the end of the source.</summary>
    public readonly int Remaining => this.source.Length - this.position;

    /// <summary>Gets the whole source.</summary>
    public readonly ReadOnlySpan<byte> Source => this.source;

    /// <summary>Gets how pointer addresses are interpreted.</summary>
    public readonly PointerAddressingMode AddressingMode => this.settings.AddressingMode;

    /// <summary>Gets the origin relative pointers are measured from.</summary>
    public readonly long Origin => this.settings.Origin;

    /// <summary>Gets whether pointers are followed.</summary>
    public readonly bool DereferencePointers => this.settings.DereferencePointers;

    /// <summary>Gets the byte limit for one pointer target, when configured.</summary>
    public readonly long? MaxPointerTargetBytes => this.settings.MaxPointerTargetBytes;

    /// <summary>Gets whether fixed text drops its trailing NUL padding.</summary>
    public readonly bool TrimFixedText => this.settings.TrimFixedText;

    /// <summary>Gets the configured array element limit.</summary>
    public readonly int MaxArrayElements => this.settings.MaxArrayElements;

    /// <summary>Gets the configured encoded-string byte limit.</summary>
    public readonly long MaxStringBytes => this.settings.MaxStringBytes;

    /// <summary>
    ///     Consumes <paramref name="count"/> bytes: checks the total read budget, checks that the bytes exist, and
    ///     advances. A short read moves the cursor to the end, as the stream reader ends there, and its message
    ///     states how many bytes the item needed and how many were left.
    /// </summary>
    /// <param name="count">The number of bytes the item occupies.</param>
    /// <param name="member">The layout field being read, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The consumed bytes.</returns>
    /// <exception cref="CStructReadLimitException">The total read budget is exceeded.</exception>
    /// <exception cref="CStructReadException">Fewer than <paramref name="count"/> bytes remain.</exception>
    public ReadOnlySpan<byte> Take(int count, string member, string? memberType)
    {
        this.Charge(count, member, memberType);
        if (count < 0 || count > this.Remaining)
        {
            throw this.ShortRead(count, member, memberType);
        }

        ReadOnlySpan<byte> bytes = this.source.Slice(this.position, count);
        this.position += count;
        return bytes;
    }

    /// <summary>The next <paramref name="count"/> bytes without consuming them or charging the budget.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The layout field being read, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="CStructReadException">Fewer than <paramref name="count"/> bytes remain.</exception>
    public ReadOnlySpan<byte> Peek(int count, string member, string? memberType)
    {
        if (count < 0 || count > this.Remaining)
        {
            throw this.ShortRead(count, member, memberType);
        }

        return this.source.Slice(this.position, count);
    }

    /// <summary>Moves past padding or a skipped member without reading it; skipped bytes are not charged.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The layout field being skipped, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadException">The skip would leave the source.</exception>
    public void Skip(int count, string member, string? memberType)
    {
        if (count < 0 || count > this.Remaining)
        {
            throw this.ShortRead(count, member, memberType);
        }

        this.position += count;
    }

    /// <summary>Moves to the next multiple of <paramref name="alignment"/> measured from <paramref name="origin"/>.</summary>
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
        this.Skip((int)padding, member, memberType);
    }

    /// <summary>Enters a nested struct or union, enforcing <c>MaxNestingDepth</c>.</summary>
    /// <param name="member">The composite field being entered, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadLimitException">The nesting limit is exceeded.</exception>
    public void EnterComposite(string member, string? memberType)
    {
        if (this.nestingDepth >= this.settings.MaxNestingDepth)
        {
            throw this.FailLimit(ReadFailures.NestingLimit, member, memberType);
        }

        this.nestingDepth++;
    }

    /// <summary>Leaves the current struct or union.</summary>
    public void ExitComposite() => this.nestingDepth--;

    /// <summary>
    ///     Follows a pointer: enforces <c>MaxPointerDepth</c>, resolves the address (relative to <see cref="Origin"/>
    ///     when configured), and moves there. Restore the position afterwards with <see cref="ExitPointer"/>.
    /// </summary>
    /// <param name="address">The stored address.</param>
    /// <param name="member">The pointer field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The position to return to.</returns>
    /// <exception cref="CStructReadLimitException">The pointer depth limit is exceeded.</exception>
    /// <exception cref="CStructReadException">The target lies outside the source.</exception>
    public int EnterPointer(long address, string member, string? memberType)
    {
        if (this.pointerDepth >= this.settings.MaxPointerDepth)
        {
            throw this.FailLimit(ReadFailures.PointerDepthLimit, member, memberType);
        }

        long target = this.settings.AddressingMode == PointerAddressingMode.Relative ? address + this.settings.Origin : address;
        if (target < 0 || target > this.source.Length)
        {
            throw this.Fail(ReadFailures.OutsideRegion, member, memberType);
        }

        int resume = this.position;
        this.pointerDepth++;
        this.position = (int)target;
        return resume;
    }

    /// <summary>Returns from a pointer target to <paramref name="resume"/>, the value <see cref="EnterPointer"/> returned.</summary>
    /// <param name="resume">The position to return to.</param>
    public void ExitPointer(int resume)
    {
        this.pointerDepth--;
        this.position = resume;
    }

    /// <summary>Validates an array length against <c>MaxArrayElements</c> and returns it as an <see cref="int"/>.</summary>
    /// <param name="count">The element count.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The validated count.</returns>
    /// <exception cref="CStructReadLimitException">The length exceeds the limit.</exception>
    public readonly int RequireArrayLength(long count, string member, string? memberType)
    {
        if (count < 0 || count > this.settings.MaxArrayElements)
        {
            throw this.FailLimit(ReadFailures.ArrayLengthLimit(count, this.settings.MaxArrayElements), member, memberType);
        }

        return (int)count;
    }

    /// <summary>Validates an encoded text buffer's size against <c>MaxStringBytes</c>.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The text field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadLimitException">The buffer exceeds the limit.</exception>
    public readonly void RequireBoundedTextBytes(long count, string member, string? memberType)
    {
        if (count > this.settings.MaxStringBytes)
        {
            throw this.FailLimit(ReadFailures.BoundedTextLimit, member, memberType);
        }
    }

    /// <summary>Validates a terminated string's encoded length (terminator included) against <c>MaxStringBytes</c>.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The string field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadLimitException">The string exceeds the limit.</exception>
    public readonly void RequireTerminatedStringBytes(long count, string member, string? memberType)
    {
        if (count > this.settings.MaxStringBytes)
        {
            throw this.FailLimit(ReadFailures.TerminatedStringLimit, member, memberType);
        }
    }

    /// <summary>
    ///     A read failure at the current position with the runtime's context: the innermost field and its type, the
    ///     operation path, and the offset.
    /// </summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructReadException Fail(string message, string? member, string? memberType)
    {
        var exception = new CStructReadException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>A limit failure at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructReadLimitException FailLimit(string message, string? member, string? memberType)
    {
        var exception = new CStructReadLimitException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>
    ///     The failure of a layout expression (an array length, a condition) evaluated by generated code: the
    ///     runtime's <c>Cannot evaluate {context}: {reason}</c> text around an exception one of the
    ///     <see cref="Expressions"/> operators raised.
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

        var failure = new CStructReadException(LayoutExpressionEvaluator.DescribeFailure(context, exception), exception);
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

    private CStructReadException ShortRead(int count, string member, string? memberType)
    {
        int available = this.Remaining;
        this.position = this.source.Length;
        return this.Fail(ReadFailures.ShortRead(count, available), member, memberType);
    }

    private void Charge(int count, string member, string? memberType)
    {
        if (count <= 0)
        {
            return;
        }

        long total = this.bytesRead + count;
        if (total > this.settings.MaxTotalBytesRead)
        {
            throw this.FailLimit(ReadFailures.TotalBytesLimit, member, memberType);
        }

        this.bytesRead = total;
    }
}
