namespace CStructSharp.Structure;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>One syntactic decision, shared by its arms and frozen before a compiled layout is published.</summary>
internal sealed class ConditionalGroup(Expr selector, IReadOnlyList<Expr>? caseLabels = null)
{
    public Expr Selector { get; } = selector;

    public IReadOnlyList<Expr>? CaseLabels { get; } = caseLabels;

    public FrozenDictionary<int, int>? CaseArms { get; init; }
}
