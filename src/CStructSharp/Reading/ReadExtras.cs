namespace CStructSharp.Reading;

using System.Collections.Generic;
using CStructSharp.Diagnostics;

/// <summary>
///     The members few reads set, kept behind one reference so that neither <see cref="ReadOptions"/> nor the settings copied
///     into every read carry them: an ordinary read has none (a <see langword="null"/> reference). Immutable, and shared by the
///     options that hold it and the settings snapshotted from them.
/// </summary>
/// <param name="OffsetBase">The position of the source's first byte in the larger input (<see cref="ReadOptions.OffsetBase"/>).</param>
/// <param name="ConstraintViolations">The collection that receives constraint failures, or <see langword="null"/> (<see cref="ReadOptions.ConstraintViolations"/>).</param>
/// <param name="ContinuedInputLength">What the buffered input forms record when the bytes are the first part of a longer input.</param>
internal sealed record ReadExtras(long OffsetBase, ICollection<CStructConstraintException>? ConstraintViolations, long ContinuedInputLength)
{
    /// <summary>The extras of an ordinary read: nothing set.</summary>
    public static readonly ReadExtras None = new(0, null, 0);

    /// <summary>Returns <see langword="null"/> for extras with nothing set, so equal options store the same thing.</summary>
    /// <param name="value">The extras.</param>
    /// <returns>The extras, or <see langword="null"/> when they equal <see cref="None"/>.</returns>
    public static ReadExtras? Normalize(ReadExtras value)
        => value.OffsetBase == 0 && value.ConstraintViolations is null && value.ContinuedInputLength == 0 ? null : value;
}
