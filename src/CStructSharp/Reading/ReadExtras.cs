namespace CStructSharp.Reading;

using System;
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
/// <param name="SkipConstraints">Whether the read evaluates no constraint (<see cref="ReadOptions.Constraints"/> is <see cref="ConstraintPolicy.Skip"/>).</param>
internal sealed record ReadExtras(long OffsetBase, ICollection<CStructConstraintException>? ConstraintViolations, long ContinuedInputLength, bool SkipConstraints = false)
{
    /// <summary>The extras of an ordinary read: nothing set.</summary>
    public static readonly ReadExtras None = new(0, null, 0);

    /// <summary>The extras of a read that only skips its constraints, shared so that a <see cref="ReadLimits"/> read with that policy allocates nothing.</summary>
    public static readonly ReadExtras SkipOnly = new(0, null, 0, true);

    /// <summary>Returns <see langword="null"/> for extras with nothing set, so equal options store the same thing.</summary>
    /// <param name="value">The extras.</param>
    /// <returns>The extras, or <see langword="null"/> when they equal <see cref="None"/>.</returns>
    public static ReadExtras? Normalize(ReadExtras value)
        => value.OffsetBase == 0 && value.ConstraintViolations is null && value.ContinuedInputLength == 0 && !value.SkipConstraints ? null : value;

    /// <summary>Rejects the contradictory request to skip the constraints and collect their violations in the same read.</summary>
    /// <exception cref="ArgumentException">The extras set both <see cref="SkipConstraints"/> and <see cref="ConstraintViolations"/>.</exception>
    public void ThrowIfContradictory()
    {
        if (this.SkipConstraints && this.ConstraintViolations is not null)
        {
            throw new ArgumentException("ReadOptions.Constraints = ConstraintPolicy.Skip evaluates no constraint, so it cannot be combined with ReadOptions.ConstraintViolations, which asks for the violations to be collected. Set only one of them.");
        }
    }
}
