namespace CStructSharp.Reading;

using System;
using System.IO;

/// <summary>Stores the immutable read settings copied at an operation boundary without a heap allocation.</summary>
internal readonly record struct ReadOperationSettings(
    PointerAddressingMode AddressingMode,
    bool DereferencePointers,
    int MaxPointerDepth,
    long? MaxPointerTargetBytes,
    int MaxArrayElements,
    long MaxStringBytes,
    long MaxTotalBytesRead,
    int MaxNestingDepth,
    long Origin,
    bool TrimFixedText = false,
    System.Threading.CancellationToken CancellationToken = default,
    ExecutionPath ExecutionPath = ExecutionPath.Fastest)
{
    /// <summary>
    ///     Gets whether every limit is usable and the operation is not already cancelled. When this is false the compiled
    ///     engine reports the problem (<see cref="ValidateSettings"/>), so a fast path must leave the call to it.
    /// </summary>
    public bool HasValidLimits =>
        !this.CancellationToken.IsCancellationRequested && this.MaxPointerDepth >= 0 && !(this.MaxPointerTargetBytes < 0) &&
        this.MaxArrayElements >= 0 && this.MaxStringBytes >= 0 && this.MaxTotalBytesRead >= 0 && this.MaxNestingDepth > 0;

    /// <summary>
    ///     Returns whether these limits admit a read the compiled engine would complete at structure depth
    ///     <paramref name="structureDepth"/>: <paramref name="bytes"/> within the byte budget, the read's structs within the
    ///     nesting limit, and its longest array within the element limit.
    /// </summary>
    /// <param name="bytes">The bytes the read consumes.</param>
    /// <param name="structureDepth">The structure depth the read starts at (0 for a root).</param>
    /// <param name="nestingDepth">The struct levels the read adds.</param>
    /// <param name="arrayCount">The largest element count among the read's arrays.</param>
    /// <returns>Whether the read stays within every limit.</returns>
    public bool Covers(long bytes, int structureDepth, int nestingDepth, int arrayCount)
        => bytes <= this.MaxTotalBytesRead && structureDepth + nestingDepth <= this.MaxNestingDepth && arrayCount <= this.MaxArrayElements;

    /// <summary>Returns whether these limits admit running <paramref name="plan"/> as a root read.</summary>
    /// <param name="plan">The static read plan.</param>
    /// <returns>Whether the plan's bytes, nesting and arrays stay within the limits.</returns>
    public bool CoversPlan(StaticReadPlan plan) => this.Covers(plan.Size, 0, plan.NestingDepth, plan.MaximumArrayCount);

    /// <summary>Copies every read choice before variable enumeration, stream access, or another caller callback.</summary>
    /// <param name="options">The caller's read options, or null for <see cref="ReadOptions.Default"/>.</param>
    /// <returns>The settings captured from the options at this moment.</returns>
    public static ReadOperationSettings SnapshotReadOptions(ReadOptions? options)
    {
        options ??= ReadOptions.Default;
        return new ReadOperationSettings(
            options.AddressingMode,
            options.DereferencePointers,
            options.MaxPointerDepth,
            options.MaxPointerTargetBytes,
            options.MaxArrayElements,
            options.MaxStringBytes,
            options.MaxTotalBytesRead,
            options.MaxNestingDepth,
            options.Origin,
            options.TrimFixedText,
            options.CancellationToken,
            options.ExecutionPath);
    }

    /// <summary>Maps already-snapshotted update traversal choices into the same read operation settings.</summary>
    /// <param name="options">The update options whose traversal limits bound the reads an update performs.</param>
    /// <returns>Read settings built from the traversal limits; fixed text is never trimmed.</returns>
    public static ReadOperationSettings SnapshotTraversalOptions(UpdateOptions options)
    {
        return new ReadOperationSettings(
            options.AddressingMode,
            options.DereferencePointers,
            options.MaxTraversalPointerDepth,
            options.MaxTraversalPointerTargetBytes,
            options.MaxTraversalArrayElements,
            options.MaxTraversalStringBytes,
            options.MaxTraversalBytesRead,
            options.MaxTraversalNestingDepth,
            options.Origin,
            CancellationToken: options.CancellationToken,
            ExecutionPath: options.ExecutionPath);
    }

    /// <summary>
    ///     Rejects a source or settings a read operation cannot start with, before any byte is read: the stream first, then
    ///     the settings in the order <see cref="ValidateSettings"/> checks them.
    /// </summary>
    /// <param name="stream">The caller's input stream.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or cannot seek.</exception>
    /// <exception cref="OperationCanceledException">The operation's token is already cancelled.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit in <paramref name="options"/> is negative, or the
    ///     nesting depth is not positive.</exception>
    public static void Validate(Stream stream, in ReadOperationSettings options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Parsing requires a readable, seekable stream.", nameof(stream));
        }

        ValidateSettings(options);
    }

    /// <summary>
    ///     The settings half of <see cref="Validate"/>, for a source that is always readable and seekable (a pinned
    ///     memory region): the token first, then every limit, in the order <see cref="Validate"/> checks them.
    /// </summary>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <exception cref="OperationCanceledException">The operation's token is already cancelled.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit in <paramref name="options"/> is negative, or the
    ///     nesting depth is not positive.</exception>
    public static void ValidateSettings(in ReadOperationSettings options)
    {
        // A token cancelled before the call ends the operation before any byte is read.
        options.CancellationToken.ThrowIfCancellationRequested();
        if (options.MaxPointerDepth < 0)
        {
            // A negative limit has no meaningful safety interpretation and would make the comparison misleading.
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum pointer depth cannot be negative.");
        }

        if (options.MaxPointerTargetBytes < 0)
        {
            // Likewise, a byte budget must either be absent or be a non-negative number of bytes.
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum pointer target bytes cannot be negative.");
        }

        if (options.MaxArrayElements < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum array elements cannot be negative.");
        }

        if (options.MaxStringBytes < 0 || options.MaxTotalBytesRead < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Read byte limits cannot be negative.");
        }

        if (options.MaxNestingDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum nesting depth must be greater than zero.");
        }
    }
}
