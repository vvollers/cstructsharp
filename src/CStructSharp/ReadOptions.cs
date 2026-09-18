namespace CStructSharp;

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
///     counters, and invalid non-positive limits fail before payload traversal.
/// </remarks>
public sealed class ReadOptions
{
    /// <summary>Creates the default bounded read and pointer policy.</summary>
    public ReadOptions()
    {
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
    public long? MaxPointerTargetBytes { get; init; }

    /// <summary>Gets the greatest number of elements a single traversed array field may contain.</summary>
    public int MaxArrayElements { get; init; } = 1_000_000;

    /// <summary>
    ///     Gets the greatest encoded-byte length permitted for one terminated string field, including its
    ///     complete encoded terminator.
    /// </summary>
    public long MaxStringBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    ///     Gets the greatest total bytes one public read-like operation may physically read. Seeking across a gap
    ///     does not consume this budget: a small target several terabytes into a file costs only its decoded bytes.
    ///     Set a larger value, up to <see cref="long.MaxValue"/>, when intentionally reading more payload data.
    /// </summary>
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
}
