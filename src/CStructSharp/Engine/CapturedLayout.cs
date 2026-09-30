namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
///     What an update compares before and after its change (<see cref="ReadEngine.CaptureLayout"/>): the byte range of every
///     value its root's debug read produced, then how each conditional member was selected. Two captures are equal when
///     both sequences are equal element by element, so an update detects a change that moves, resizes or reselects a member.
/// </summary>
/// <param name="Values">Each value's path and byte range, in read order.</param>
/// <param name="Conditions">Each conditional member's selection, in the order the members were decided.</param>
internal readonly record struct CapturedLayout(LayoutRange[] Values, List<ConditionalSelection> Conditions)
{
    /// <summary>Compares two captures element by element (the default record equality would compare the collections by reference).</summary>
    /// <param name="other">The other capture.</param>
    /// <returns>Whether both captures hold the same ranges and the same selections in the same order.</returns>
    public bool Equals(CapturedLayout other)
        => this.Values.AsSpan().SequenceEqual(other.Values)
           && CollectionsMarshal.AsSpan(this.Conditions).SequenceEqual(CollectionsMarshal.AsSpan(other.Conditions));

    /// <summary>Returns a hash code consistent with <see cref="Equals(CapturedLayout)"/>: the two sequence lengths.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(this.Values.Length, this.Conditions.Count);
}
