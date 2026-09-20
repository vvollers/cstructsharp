namespace CStructSharp.Syntax;

using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>One syntactic decision, shared by its arms and frozen before a compiled layout is published.</summary>
internal sealed class ConditionalGroup(Expr selector, IReadOnlyList<Expr>? caseLabels = null)
{
    public Expr Selector { get; } = selector;

    public IReadOnlyList<Expr>? CaseLabels { get; } = caseLabels;

    public ImmutableDictionary<int, int>? CaseArms { get; init; }
}
