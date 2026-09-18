namespace CStructSharp.Values;

using System.Collections.Generic;
using CStructSharp.Diagnostics;

/// <summary>
///     The outcome of a debug value read: the selected value exactly as <c>ReadValue</c> returns it, plus one
///     <see cref="DebugData"/> record per value the reader produced, in read order.
/// </summary>
/// <param name="Value">The value at the path; the same object <c>ReadValue</c> would return for the same path.</param>
/// <param name="Debug">The byte range, path, type, and decoded value of every item read.</param>
public sealed record ReadResult(object? Value, IReadOnlyList<DebugData> Debug)
{
    /// <summary>Splits the result into its value and its debug records, in that order.</summary>
    /// <param name="value">Receives <see cref="Value"/>.</param>
    /// <param name="debug">Receives <see cref="Debug"/>.</param>
    public void Deconstruct(out object? value, out IReadOnlyList<DebugData> debug)
    {
        value = this.Value;
        debug = this.Debug;
    }
}
