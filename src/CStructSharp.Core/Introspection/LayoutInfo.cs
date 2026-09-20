namespace CStructSharp.Introspection;

using System.Collections.Generic;

/// <summary>
///     A read-only description of a compiled layout: its declarations with their sizes, alignments, fields, offsets,
///     and members. Built on first use from the compiled model, so a layout that is never inspected pays nothing.
/// </summary>
public sealed class LayoutInfo
{
    internal LayoutInfo(IReadOnlyList<LayoutDeclarationInfo> declarations, IReadOnlyDictionary<string, LayoutConstant> constants, IReadOnlyList<string> includes)
    {
        this.Declarations = declarations;
        this.Constants = constants;
        this.Includes = includes;
    }

    /// <summary>Gets every exported declaration in source order (a typedef's tag appears as its own struct or union).</summary>
    public IReadOnlyList<LayoutDeclarationInfo> Declarations { get; }

    /// <summary>Gets the layout's <c>#define</c>s; the same view as <see cref="CStruct.Constants"/>.</summary>
    public IReadOnlyDictionary<string, LayoutConstant> Constants { get; }

    /// <summary>Gets the recorded <c>#include</c> paths; the same view as <see cref="CStruct.Includes"/>.</summary>
    public IReadOnlyList<string> Includes { get; }
}
