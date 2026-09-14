namespace CStructSharp;

using System;
using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>
///     The operation-owned layout-variable dictionary. <see cref="CaptureAll"/> is set when a caller-supplied
///     expression survived resolution unevaluated (an exact-enum-domain definition override, internal test inputs);
///     such an expression can name any field, so the reader must then capture every field rather than only the
///     ones the compiled layout's own expressions reference (E2.6).
/// </summary>
internal sealed class LayoutVariables : Dictionary<string, Expr>
{
    public LayoutVariables()
        : base(StringComparer.Ordinal)
    {
    }

    public LayoutVariables(IDictionary<string, Expr> source)
        : base(source, StringComparer.Ordinal)
    {
        this.CaptureAll = source is LayoutVariables { CaptureAll: true };
    }

    public bool CaptureAll { get; set; }
}
