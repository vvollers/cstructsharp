namespace CStructSharp.Writing;

using System;

/// <summary>
///     Copies and checks the options of a write or update once, at the public operation boundary, so a caller that changes
///     its options object during the operation cannot change what the operation does. The compiled engine's writes and
///     updates and the generated writers' cursor share these rules.
/// </summary>
internal static class WriteOptionSnapshots
{
    /// <summary>Gets the options a write without caller options uses; shared because <see cref="WriteOptions"/> is immutable.</summary>
    public static WriteOptions DefaultWriteOptions { get; } = new();

    /// <summary>Copies every update choice before variable enumeration, payload access, or stream traversal.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A private copy the caller cannot change during the operation.</returns>
    public static UpdateOptions SnapshotUpdateOptions(UpdateOptions? options)
    {
        // The record's own `with` expression clones every current and future property in one step, instead of a
        // hand-maintained property-by-property copy that silently reverts a newly added property to its default
        // on every call until someone remembers to list it here too.
        return (options ?? new UpdateOptions()) with { };
    }

    /// <summary>Copies normal write choices while retaining update semantics when that derived value was supplied.</summary>
    /// <param name="options">The caller's write or update options, or <see langword="null"/>.</param>
    /// <returns>
    ///     <see cref="DefaultWriteOptions"/> for <see langword="null"/>; otherwise a copy of the same record type.
    /// </returns>
    public static WriteOptions SnapshotWriteOptions(WriteOptions? options)
    {
        if (options is UpdateOptions updateOptions)
        {
            return SnapshotUpdateOptions(updateOptions);
        }

        // WriteOptions is an immutable record, so the shared default needs no copy; a caller's instance is copied because
        // a derived record could add mutable state.
        return options is null ? DefaultWriteOptions : options with { };
    }

    /// <summary>Validates finite write budgets once at the public operation boundary.</summary>
    /// <param name="options">The options to check.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     A byte or element limit is negative, or the maximum nesting depth is not positive.
    /// </exception>
    public static void ValidateWriteOptions(WriteOptions options)
    {
        if (options.MaxArrayElements < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum array elements cannot be negative.");
        }

        if (options.MaxStringBytes < 0 || options.MaxTotalBytesWritten < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Write byte limits cannot be negative.");
        }

        if (options.MaxNestingDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum nesting depth must be greater than zero.");
        }
    }
}
