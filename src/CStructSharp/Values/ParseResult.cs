namespace CStructSharp.Values;

using System.Collections.Generic;
using CStructSharp.Diagnostics;

/// <summary>
///     The outcome of a debug parse: the selected struct exactly as <c>Parse</c> returns it, plus one
///     <see cref="DebugData"/> record per value the reader produced, in read order.
/// </summary>
/// <param name="Value">The parsed struct; the same object <c>Parse</c> would return for the same path.</param>
/// <param name="Debug">The byte range, path, type, and decoded value of every item read, including the root's members.</param>
public sealed record ParseResult(StructValue Value, IReadOnlyList<DebugData> Debug)
{
    /// <summary>Splits the result into its value and its debug records, in that order.</summary>
    public void Deconstruct(out StructValue value, out IReadOnlyList<DebugData> debug)
    {
        value = this.Value;
        debug = this.Debug;
    }
}
