namespace CStructSharp;

using CStructSharp.Writing;

/// <summary>Says what a write does with a supplied member that the layout does not declare.</summary>
public enum UnknownMemberPolicy
{
    /// <summary>Extra members are skipped; only declared fields are encoded.</summary>
    Ignore,

    /// <summary>An extra member fails the write with <see cref="Diagnostics.CStructWriteException"/> before any byte is written.</summary>
    Reject,
}

/// <summary>Controls serialization and stream-writing operations performed by <see cref="CStruct"/>.</summary>
/// <remarks>
///     Every operation snapshots these values before writing (see <see cref="CStructElementWriterState.SnapshotWriteOptions"/>)
///     using this record's own <c>with</c> expression rather than a hand-maintained property-by-property copy, so a
///     newly added property is always included in the snapshot automatically. Budgets are per public operation.
///     Stream operations use the stream's current position as their output origin; caller-owned memory uses
///     coordinate zero. Left unsealed only so <see cref="UpdateOptions"/> can derive from it while keeping the
///     same snapshot-via-<c>with</c> pattern; <see cref="ReadOptions"/> has no such subtype and stays <c>sealed</c>.
/// </remarks>
public record WriteOptions
{
    /// <summary>Creates the default bounded write policy.</summary>
    public WriteOptions()
    {
    }

    /// <summary>Gets whether written pointer values are absolute stream positions or offsets from <see cref="Origin"/>.</summary>
    public PointerAddressingMode AddressingMode { get; init; } = PointerAddressingMode.Absolute;

    /// <summary>
    ///     Gets what happens when the supplied value carries a member the struct or union does not declare - a
    ///     misspelled key, a stale property, or an extra dictionary entry. The default ignores it; <see cref="UnknownMemberPolicy.Reject"/>
    ///     fails the write with the unknown name and the declared members, checked per composite before its
    ///     bytes are written. Parsed <see cref="Values.UnionValue"/> instances are never checked.
    /// </summary>
    public UnknownMemberPolicy UnknownMembers { get; init; } = UnknownMemberPolicy.Ignore;

    /// <summary>Gets the greatest number of elements one array field may write.</summary>
    public int MaxArrayElements { get; init; } = 1_000_000;

    /// <summary>
    ///     Gets the greatest encoded-byte length one string field may write, including fixed-buffer padding or
    ///     a terminated string's complete terminator.
    /// </summary>
    public long MaxStringBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    ///     Gets the greatest total number of bytes one operation may physically submit to its stream.
    ///     Rewrites of shared storage count again, and extending a seekable stream across a gap is charged by extent.
    /// </summary>
    public long MaxTotalBytesWritten { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the greatest active struct or union depth one write operation may enter.</summary>
    public int MaxNestingDepth { get; init; } = 256;

    /// <summary>
    ///     Gets the base position subtracted, with checked arithmetic, from relative pointer values before
    ///     they are written. The resulting non-null offset must be positive and fit the configured pointer width.
    ///     Null address zero is stored directly and does not use this origin.
    /// </summary>
    public long Origin { get; init; }
}
