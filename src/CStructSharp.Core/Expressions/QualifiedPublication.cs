namespace CStructSharp.Expressions;

using System;
using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>
///     The one rule every reader, writer and resolver path publishes qualified layout variables by. While nested structs
///     are read that expressions name through a dotted path, the active prefix is their names joined (<c>m.hdr.</c> while
///     <c>hdr</c> inside <c>m</c> is read); a value captured under the bare name <c>n</c> is also published under every
///     qualified spelling the active prefix allows - <c>m.hdr.n</c> for an expression of the outer struct, and
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
    ///     Copies the bare variable <paramref name="name"/> under every qualified spelling of <paramref name="prefix"/>,
    ///     or removes those spellings when the bare variable is absent.
    /// </summary>
    /// <param name="variables">The operation's variables.</param>
    /// <param name="prefix">The active prefix, one or more member names each followed by a dot.</param>
    /// <param name="name">The bare field name just captured.</param>
    public static void Publish(Dictionary<string, Expr> variables, string prefix, string name)
    {
        bool present = variables.TryGetValue(name, out Expr? value);
        int start = 0;
        while (true)
        {
            string qualified = prefix.Substring(start) + name;
            if (present)
            {
                variables[qualified] = value!;
            }
            else
            {
                variables.Remove(qualified);
            }

            int dot = prefix.IndexOf('.', start);
            if (dot < 0 || dot + 1 >= prefix.Length)
            {
                return;
            }

            start = dot + 1;
        }
    }

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
