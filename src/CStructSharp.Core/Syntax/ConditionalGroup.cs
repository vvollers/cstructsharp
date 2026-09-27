namespace CStructSharp.Syntax;

using System.Collections.Generic;

/// <summary>One <c>if</c> or <c>switch</c> of a struct or union body; every arm's members refer to the same instance.</summary>
/// <param name="selector">The <c>if</c> condition or the <c>switch</c> value.</param>
/// <param name="caseLabels">A switch's <c>case</c> labels in arm order (<c>default</c> excluded), or <see langword="null"/> for an <c>if</c>.</param>
internal sealed class ConditionalGroup(Expr selector, IReadOnlyList<Expr>? caseLabels = null)
{
    /// <summary>Gets the <c>if</c> condition or the <c>switch</c> value.</summary>
    public Expr Selector { get; } = selector;

    /// <summary>Gets a switch's labels in arm order: as written when parsed, as literals once normalized; <see langword="null"/> for an <c>if</c>.</summary>
    public IReadOnlyList<Expr>? CaseLabels { get; } = caseLabels;
}
