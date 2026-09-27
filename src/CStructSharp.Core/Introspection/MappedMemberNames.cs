namespace CStructSharp.Introspection;

using System;
using System.Collections.Generic;

/// <summary>
///     The rule a mapped class's property uses to find its layout member, shared by the source generator (which binds
///     at build time) and the runtime (which binds a mapper whose layout was not known at build time): the exact name,
///     else the case-insensitive match, else the match after the layout name's underscores are ignored
///     (<c>bit_depth</c> for <c>BitDepth</c>). A step that finds two members ends the search without a match, so a
///     property never binds to a member it names ambiguously.
/// </summary>
internal static class MappedMemberNames
{
    /// <summary>Finds the layout member a property maps to.</summary>
    /// <param name="names">The layout struct's member names.</param>
    /// <param name="propertyName">The mapped property's name.</param>
    /// <returns>The member name, as the <paramref name="names"/> instance, or <see langword="null"/> when none matches or a step is ambiguous.</returns>
    public static string? Match(IReadOnlyList<string> names, string propertyName)
    {
        for (int index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], propertyName, StringComparison.Ordinal))
            {
                return names[index];
            }
        }

        if (Single(names, propertyName, collapseUnderscores: false, out string? found))
        {
            return found;
        }

        return Single(names, propertyName, collapseUnderscores: true, out found) ? found : null;
    }

    /// <summary>One case-insensitive step: the single matching name, if any.</summary>
    /// <param name="names">The layout struct's member names.</param>
    /// <param name="propertyName">The mapped property's name.</param>
    /// <param name="collapseUnderscores">Whether to compare names with underscores (only names that contain one).</param>
    /// <param name="found">The single match, or <see langword="null"/>.</param>
    /// <returns>Whether the step decides: exactly one match, or two (<paramref name="found"/> is then <see langword="null"/>).</returns>
    private static bool Single(IReadOnlyList<string> names, string propertyName, bool collapseUnderscores, out string? found)
    {
        found = null;
        for (int index = 0; index < names.Count; index++)
        {
            string name = names[index];
            if (collapseUnderscores && name.IndexOf('_') < 0)
            {
                continue;
            }

            string compared = collapseUnderscores ? name.Replace("_", string.Empty) : name;
            if (!string.Equals(compared, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (found is not null)
            {
                // Two members answer to this property: the search ends without one.
                found = null;
                return true;
            }

            found = name;
        }

        return found is not null;
    }
}
