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

    /// <summary>Creates the default bounded read and pointer policy.</summary>
    public ReadOptions()
    {
    }

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
    internal long ContinuedInputLength { get; init; }

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
    public int MaxArrayElements { get; init; } = 1_000_000;

    /// <summary>
    ///     Gets the greatest encoded-byte length permitted for one terminated string field, including its
    ///     complete encoded terminator.
    /// </summary>
    public long MaxStringBytes { get; init; } = 16 * 1024 * 1024;

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
    public long MaxTotalBytesRead { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the greatest active struct depth permitted during one read-like operation.</summary>
    public int MaxNestingDepth { get; init; } = 256;

    /// <summary>
    ///     Gets whether fixed-capacity text - <c>char[N]</c>, <c>wchar[N]</c>, and bounded encoded buffers such as
    ///     <c>utf8 name[N]</c> - drops its trailing NUL padding when read. The default keeps every character, so a
    ///     <c>char[4]</c> holding <c>61 62 00 00</c> reads as <c>"ab\0\0"</c>; with this option it reads as
    ///     <c>"ab"</c>. Only trailing NULs are removed; embedded NULs stay. Writing is unaffected: shorter text is
    ///     always zero-padded to the declared capacity.
    /// </summary>
    public bool TrimFixedText { get; init; }

    /// <summary>
    ///     Gets the signed base position added with checked arithmetic to non-null relative pointer offsets
    ///     before their target stream range is validated.
    /// </summary>
    public long Origin { get; init; }

    /// <summary>
    ///     Gets the token a long read observes: it is checked when a composite or a pointer target is entered, per
    ///     block of a primitive array, per element of a composite array, and per chunk of a terminated string, and
    ///     a cancelled token ends the operation with <see cref="OperationCanceledException"/> (not a read failure:
    ///     <c>TryReadValue</c> lets it through). Never checked per primitive, so a small read costs nothing for it.
    /// </summary>
    public System.Threading.CancellationToken CancellationToken { get; init; }
}
