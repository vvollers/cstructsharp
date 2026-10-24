namespace CStructSharp;

using System.Runtime.CompilerServices;

/// <summary>Lists how pointer addresses in the input stream should be interpreted.</summary>
public enum PointerAddressingMode
{
    /// <summary>Stored pointer values are absolute zero-based stream positions.</summary>
    Absolute,

    /// <summary>Stored pointer values are offsets relative to the configured origin.</summary>
    Relative,
}

/// <summary>
///     Controls the safety budgets and pointer policy shared by parsing, debug parsing, selected reads, address
///     resolution, and dynamic-length lookup.
/// </summary>
/// <remarks>
///     Every operation snapshots these values before reading. Budgets are per public operation, not lifetime
///     counters, and invalid non-positive limits fail before payload traversal. A record: <c>options with
///     { TrimFixedText = true }</c> copies every other member, and two instances with the same members are equal.
/// </remarks>
public sealed record ReadOptions
{
    // MaxPointerTargetBytes is stored as a value and a flag rather than a long? (16 bytes with padding): the flag packs
    // beside the other bool members, which keeps an instance - copied once per stream or async operation that links a
    // token - at 88 bytes with ContinuedInputLength included. The value is 0 when the flag is unset, so the record's
    // member-wise equality matches the nullable property's.
    private readonly long maxPointerTargetBytes;
    private readonly bool hasMaxPointerTargetBytes;

    // InvalidText has two values, so it is stored as a flag beside the other flags instead of as a four-byte enum: an
    // instance is allocated by every read that builds its own options, and a wider field would grow it past 88 bytes.
    private readonly bool lenientText;

    // The members few reads set - the offset base, the violation collection and the buffered-input continuation - share one
    // reference that stays null for an ordinary read, so none of them grows an instance past 88 bytes.
    private readonly Reading.ReadExtras? extras;

    /// <summary>Creates the default bounded read and pointer policy.</summary>
    public ReadOptions()
    {
    }

    /// <summary>Gets the members few reads set (offset base, violation collection, buffered-input continuation), or <see langword="null"/> when none is.</summary>
    internal Reading.ReadExtras? Extras => this.extras;

    /// <summary>Gets the shared default options an operation uses when the caller passes none.</summary>
    internal static ReadOptions Default { get; } = new();

    /// <summary>Gets which fast paths the read may take before the compiled engine; tests restrict it to compare them with the engine.</summary>
    internal ExecutionPath ExecutionPath { get; init; }

    /// <summary>
    ///     Gets what the buffered input forms record when the bytes an operation is handed are only the first part of
    ///     a longer input: <see cref="Streams.BufferedInput.WholeInput"/> (the default) when they are the whole input,
    ///     the whole input's length in bytes when it is known, or <see cref="Streams.BufferedInput.UnknownLength"/>.
    ///     A reader that needs a byte past the handed part then raises the internal signal that makes the buffered form
    ///     read more of the input and run the operation again (<see cref="Streams.BufferedInput"/>).
    /// </summary>
    internal long ContinuedInputLength
    {
        get => this.extras?.ContinuedInputLength ?? 0;
        init => this.extras = Reading.ReadExtras.Normalize((this.extras ?? Reading.ReadExtras.None) with { ContinuedInputLength = value });
    }

    /// <summary>Gets whether pointer addresses are stream positions or offsets from <see cref="Origin"/>.</summary>
    public PointerAddressingMode AddressingMode { get; init; } = PointerAddressingMode.Absolute;

    /// <summary>
    ///     Gets whether non-null pointers are followed while parsing.
    /// </summary>
    public bool DereferencePointers { get; init; } = true;

    /// <summary>
    ///     Gets the greatest number of nested pointer dereferences allowed on one parse branch.
    /// </summary>
    public int MaxPointerDepth { get; init; } = 64;

    /// <summary>
    ///     Gets the greatest fixed-size target, in bytes, that can be read through one pointer.
    ///     A null value leaves the target size unrestricted. Variable-length string targets are rejected when a limit is set.
    ///     This is a target's decoded size, never a maximum pointer address or seek distance.
    /// </summary>
    public long? MaxPointerTargetBytes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.hasMaxPointerTargetBytes ? this.maxPointerTargetBytes : null;
        init
        {
            this.hasMaxPointerTargetBytes = value.HasValue;
            this.maxPointerTargetBytes = value.GetValueOrDefault();
        }
    }

    /// <summary>Gets the greatest number of elements a single traversed array field may contain.</summary>
    public int MaxArrayElements { get; init; } = ReadLimits.DefaultMaxArrayElements;

    /// <summary>
    ///     Gets the greatest encoded-byte length permitted for one terminated string field, including its
    ///     complete encoded terminator.
    /// </summary>
    public long MaxStringBytes { get; init; } = ReadLimits.DefaultMaxStringBytes;

    /// <summary>
    ///     Gets the greatest total bytes one public read-like operation may consume. Each byte a read consumes counts
    ///     once; a byte read again (a pointer target visited twice, a union's overlapping members) counts again. Bytes
    ///     inspected before they are consumed do not count: the scan of a terminated array for its terminator, and the
    ///     read-ahead of a terminated string past its terminator, although a stream source may physically read them.
    ///     Seeking across a gap or padding does not consume this budget: a small target several terabytes into a file
    ///     costs only its decoded bytes. Set a larger value, up to <see cref="long.MaxValue"/>, when intentionally
    ///     reading more payload data.
    /// </summary>
    /// <remarks>
    ///     A parse of <c>struct one { uint8 a[]; }</c> over <c>01 02 00</c> consumes the two elements and the terminator,
    ///     so it succeeds with a budget of 3 and fails with <see cref="Diagnostics.CStructReadLimitException"/> at 2. The
    ///     runtime and the generated readers charge the same bytes on every input form.
    /// </remarks>
    public long MaxTotalBytesRead { get; init; } = ReadLimits.DefaultMaxTotalBytesRead;

    /// <summary>Gets the greatest active struct depth permitted during one read-like operation.</summary>
    public int MaxNestingDepth { get; init; } = ReadLimits.DefaultMaxNestingDepth;

    /// <summary>
    ///     Gets whether fixed-capacity text - <c>char[N]</c>, <c>wchar[N]</c>, and bounded encoded buffers such as
    ///     <c>utf8 name[N]</c> - drops its trailing NUL padding when read. The default keeps every character, so a
    ///     <c>char[4]</c> holding <c>61 62 00 00</c> reads as <c>"ab\0\0"</c>; with this option it reads as
    ///     <c>"ab"</c>. Only trailing NULs are removed; embedded NULs stay. Writing is unaffected: shorter text is
    ///     always zero-padded to the declared capacity.
    /// </summary>
    public bool TrimFixedText { get; init; }

    /// <summary>
    ///     Gets what a read does with text that is not valid in its encoding: <see cref="InvalidTextHandling.Fail"/>
    ///     (the default) reports the field, <see cref="InvalidTextHandling.Replace"/> reads each invalid sequence as
    ///     U+FFFD and continues. It applies to <c>utf8</c> and UTF-16 buffers, <c>wchar[N]</c>, and terminated strings;
    ///     see <see cref="InvalidTextHandling"/>. Writing is unaffected: text that cannot be encoded still fails.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of <see cref="InvalidTextHandling"/>.</exception>
    public InvalidTextHandling InvalidText
    {
        get => this.lenientText ? InvalidTextHandling.Replace : InvalidTextHandling.Fail;
        init => this.lenientText = value switch
        {
            InvalidTextHandling.Fail => false,
            InvalidTextHandling.Replace => true,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Not a member of InvalidTextHandling."),
        };
    }

    /// <summary>Gets whether invalid text is replaced instead of failing, as the readers test it.</summary>
    internal bool LenientText => this.lenientText;

    /// <summary>
    ///     Gets the signed base position added with checked arithmetic to non-null relative pointer offsets
    ///     before their target stream range is validated.
    /// </summary>
    public long Origin { get; init; }

    /// <summary>
    ///     Gets the position of the source's first byte in the larger input it was cut from (an image, a file; for a stream
    ///     read by a generated reader, the stream's position at the call), added to the
    ///     <see cref="CStructSharp.Diagnostics.CStructException.Offset"/> every failure of a span, array, memory or generated stream read
    ///     reports and to the offset in its message, so a failure names where the bytes are in the evidence rather than in the
    ///     buffer. It changes nothing else: positions, addresses and pointer targets still count from the buffer's first byte, and
    ///     the runtime layout's stream readers and the record sequences over a stream report absolute stream positions already and ignore it. The default is 0.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">The value is negative or greater than 2^48 - 1 (<see cref="ReadLimits.MaxOffsetBase"/>).</exception>
    public long OffsetBase
    {
        get => this.extras?.OffsetBase ?? 0;
        init
        {
            ReadLimits.ValidateOffsetBase(value);
            this.extras = Reading.ReadExtras.Normalize((this.extras ?? Reading.ReadExtras.None) with { OffsetBase = value });
        }
    }

    /// <summary>
    ///     Gets the collection that receives every <c>@expect</c> and <c>@supports</c> failure instead of the first one
    ///     ending the read: when it is set, a field whose value breaks a constraint adds a
    ///     <see cref="CStructSharp.Diagnostics.CStructConstraintException"/> (not thrown, with the member, path and offset
    ///     of a thrown one) and the read continues with the value as read, so one pass reports every inconsistent field
    ///     of a header. Any other failure (a short read, a limit, an expression that cannot be evaluated) still ends the read
    ///     with its exception, and a read that completes with an empty collection satisfied every constraint. Constraints are
    ///     checked in field order, so the list is in field order. <see langword="null"/>, the default, throws the first.
    /// </summary>
    /// <remarks>
    ///     A value that breaks a constraint may break what depends on it (a count that is too large fails the array limit
    ///     next); the collection then holds the failures found up to that read failure, which is thrown as usual.
    ///     The collection is not thread-safe and is added to in place: use one per read.
    /// </remarks>
    public System.Collections.Generic.ICollection<CStructSharp.Diagnostics.CStructConstraintException>? ConstraintViolations
    {
        get => this.extras?.ConstraintViolations;
        init => this.extras = Reading.ReadExtras.Normalize((this.extras ?? Reading.ReadExtras.None) with { ConstraintViolations = value });
    }

    /// <summary>
    ///     Gets the token a long read observes: it is checked when a composite or a pointer target is entered, per
    ///     block of a primitive array, per element of a composite array, and per chunk of a terminated string, and
    ///     a cancelled token ends the operation with <see cref="OperationCanceledException"/> (not a read failure:
    ///     <c>TryReadValue</c> lets it through). Never checked per primitive, so a small read costs nothing for it.
    /// </summary>
    public System.Threading.CancellationToken CancellationToken { get; init; }
}
