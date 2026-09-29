namespace CStructSharp.Expressions;

using System;
using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>
///     The operation-owned layout-variable dictionary: the resolved definitions and caller variables, to which the
///     operation adds the values of the fields the layout's expressions reference as it reads or writes them.
/// </summary>
internal sealed class LayoutVariables : Dictionary<string, Expr>
{
    /// <summary>Creates an empty dictionary with ordinal (case-sensitive) name comparison.</summary>
    public LayoutVariables()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>Creates an ordinal-keyed copy of <paramref name="source"/>.</summary>
    /// <param name="source">The variables to copy; the copy does not share storage with it.</param>
    public LayoutVariables(IDictionary<string, Expr> source)
        : base(source, StringComparer.Ordinal)
    {
    }
}
