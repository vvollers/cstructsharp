namespace CStructSharp.Expressions;

using System;
using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>
///     The operation-owned layout-variable dictionary. <see cref="CaptureAll"/> is set when a caller-supplied
///     expression survived resolution unevaluated (an exact-enum-domain definition override, internal test inputs);
///     such an expression can name any field, so the reader must then capture every field rather than only the
///     ones the compiled layout's own expressions reference.
/// </summary>
internal sealed class LayoutVariables : Dictionary<string, Expr>
{
    /// <summary>Creates an empty dictionary with ordinal (case-sensitive) name comparison.</summary>
    public LayoutVariables()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>Creates an ordinal-keyed copy of <paramref name="source"/>, keeping its capture-all flag.</summary>
    /// <param name="source">The variables to copy; the copy does not share storage with it.</param>
    public LayoutVariables(IDictionary<string, Expr> source)
        : base(source, StringComparer.Ordinal)
    {
        this.CaptureAll = source is LayoutVariables { CaptureAll: true };
    }

    /// <summary>Gets or sets whether the reader must publish every field, not only referenced ones.</summary>
    public bool CaptureAll { get; set; }
}
