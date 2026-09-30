namespace CStructSharp.Expressions;

using System;

/// <summary>
///     The one rule the compiled engine's reader, writer and path resolver publish qualified layout variables by. While nested structs
///     are read that expressions name through a dotted path, the active prefix is their names joined (<c>m.hdr.</c> while
///     <c>hdr</c> inside <c>m</c> is read); a value captured under the bare name <c>n</c> is also published under every
///     qualified spelling the active prefix allows (<see cref="Covers"/>) - <c>m.hdr.n</c> for an expression of the outer struct, and
///     <c>hdr.n</c> for an expression of the struct that holds <c>hdr</c>.
/// </summary>
/// <remarks>
///     Each spelling is a suffix of the prefix that starts at a member name, so an expression reaches a nested member by
///     the path from the struct it is written in, however deep that struct itself sits. Like bare names, a qualified name
///     keeps the last value published under it (layout variables are dynamically scoped).
/// </remarks>
internal static class QualifiedPublication
{
    /// <summary>
    ///     Returns whether a capture under the active prefix is published under <paramref name="spelling"/>: the spelling
    ///     is a suffix of the prefix that starts at a member name.
    /// </summary>
    /// <param name="prefix">The active prefix, one or more member names each followed by a dot.</param>
    /// <param name="spelling">A qualified prefix some expression spells, such as <c>hdr.</c>.</param>
    /// <returns>Whether the capture is published under the spelling.</returns>
    public static bool Covers(string prefix, string spelling)
        => prefix.EndsWith(spelling, StringComparison.Ordinal) &&
           (prefix.Length == spelling.Length || prefix[prefix.Length - spelling.Length - 1] == '.');
}
