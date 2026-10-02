namespace CStructSharp.Generated;

using System;
using System.ComponentModel;
using CStructSharp.Diagnostics;
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
///     The cursor borrows its source span; it neither owns nor mutates those bytes. Position and diagnostics use
///     the span's byte-zero origin. Stream adapters translate that origin at the operation boundary. Byte-budget
///     accounting is separate from position, so revisiting pointer targets still charges the bytes read. When the
///     buffered forms hand the cursor only the first part of a longer input (<see cref="Streams.BufferedInput"/>), every
///     access that needs a byte past that part raises the internal signal that makes the form read more of the input
///     and run the generated reader again; the checks sit on the branches that would otherwise fail.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref partial struct ReadCursor
{
    private readonly ReadOnlySpan<byte> source;
    private readonly ReadOperationSettings settings;
    private readonly string? path;
    private int position;
    private long bytesRead;
    private int nestingDepth;
    private int pointerDepth;
    private int unionDepth;
    private System.Collections.Generic.HashSet<(long Address, string Type, int Depth)>? activeTargets;
    private System.Collections.Generic.Stack<(long Address, string Type, int Depth)>? activeTargetStack;

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
                this.RequireBufferedPosition(value);
                throw this.Fail(ReadFailures.OutsideRegion, null, null);
            }

            this.position = value;
        }
    }

    /// <summary>Gets the path the operation reads, as reported in diagnostics.</summary>
    public readonly string? Path => this.path;

    /// <summary>Gets the number of bytes from the position to the end of the source.</summary>
    internal readonly int Remaining => this.source.Length - this.position;

    /// <summary>Gets the whole source.</summary>
    internal readonly ReadOnlySpan<byte> Source => this.source;

    /// <summary>Gets how pointer addresses are interpreted.</summary>
    public readonly PointerAddressingMode AddressingMode => this.settings.AddressingMode;

    /// <summary>Gets the origin relative pointers are measured from.</summary>
    public readonly long Origin => this.settings.Origin;

    /// <summary>Gets whether pointers are followed.</summary>
    public readonly bool DereferencePointers => this.settings.DereferencePointers;

    /// <summary>Gets whether pointers are followed here: the option, unless a union is being read (an untagged union never follows an address that merely overlaps its bytes).</summary>
    public readonly bool FollowsPointers => this.settings.DereferencePointers && this.unionDepth == 0;

    /// <summary>Gets the byte limit for one pointer target, when configured.</summary>
    public readonly long? MaxPointerTargetBytes => this.settings.MaxPointerTargetBytes;

    /// <summary>Gets whether fixed text drops its trailing NUL padding.</summary>
    public readonly bool TrimFixedText => this.settings.TrimFixedText;

    /// <summary>Gets the configured array element limit.</summary>
    public readonly int MaxArrayElements => this.settings.MaxArrayElements;

    /// <summary>Gets the configured encoded-string byte limit.</summary>
    public readonly long MaxStringBytes => this.settings.MaxStringBytes;

    /// <summary>Moves to <paramref name="position"/> (a placement result: an aligned field start, the end of a composite), failing with the runtime's text and context when it lies outside the input.</summary>
    /// <param name="position">The position to move to.</param>
    /// <param name="member">The layout field being placed, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void Seek(long position, string? member, string? memberType)
    {
        if (position < 0 || position > this.source.Length)
        {
            this.RequireBufferedPosition(position);
            throw this.Fail(ReadFailures.OutsideRegion, member, memberType);
        }

        this.position = (int)position;
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

    /// <summary>
    ///     Raises the buffered-input signal when the source is only the first part of the input and bytes the input does
    ///     or may hold in [<paramref name="start"/>, <paramref name="end"/>) lie past it; otherwise returns, and the caller
    ///     reports what the whole input gives. Called only on branches where an access would otherwise come up short.
    /// </summary>
    /// <param name="start">The first byte the access needs, from the source's byte 0.</param>
    /// <param name="end">The end of the bytes the access needs.</param>
    /// <exception cref="Streams.BufferedInputShortfallException">The access needs bytes past the source.</exception>
    private readonly void RequireBuffered(long start, long end)
    {
        if (this.settings.ContinuedInputLength == Streams.BufferedInput.WholeInput)
        {
            return;
        }

        long limit = this.settings.ContinuedInputLength < 0 ? end : Math.Min(end, this.settings.ContinuedInputLength);
        if (limit > this.source.Length && limit > start)
        {
            throw Streams.BufferedInput.Shortfall(limit);
        }
    }

    /// <summary>
    ///     Raises the buffered-input signal for a position past the source that the input does or may reach, since the
    ///     cursor keeps its position within the bytes it holds; otherwise returns and the caller fails.
    /// </summary>
    /// <param name="value">The rejected position.</param>
    /// <exception cref="Streams.BufferedInputShortfallException">The position lies past the source, inside the input.</exception>
    private readonly void RequireBufferedPosition(long value)
    {
        if (value >= 0 && this.settings.ContinuedInputLength != Streams.BufferedInput.WholeInput && (this.settings.ContinuedInputLength < 0 || value <= this.settings.ContinuedInputLength))
        {
            throw Streams.BufferedInput.Shortfall(value);
        }
    }

    /// <summary>Charges positive bytes to the operation budget without changing position or charging a rejected read.</summary>
    /// <param name="count">Bytes consumed by this read, including repeated reads of previously visited addresses.</param>
    /// <param name="member">The field being read.</param>
    /// <param name="memberType">The field's layout type spelling.</param>
    /// <exception cref="CStructReadLimitException">The read would exceed the total-byte budget.</exception>
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
