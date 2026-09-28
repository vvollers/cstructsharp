namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Syntax;

/// <summary>
///     One <c>if</c>/<c>switch</c> decision of a compiled composite: the selector evaluated once per instance and, for a
///     switch, the arm each case value selects. All arms of the decision share one instance.
/// </summary>
internal sealed class CompiledConditionalGroup
{
    /// <summary>Compiles a normalized group, whose case labels are already evaluated to distinct literals.</summary>
    /// <param name="group">The normalized group.</param>
    public CompiledConditionalGroup(ConditionalGroup group)
    {
        this.Selector = group.Selector;
        this.CaseArms = group.CaseLabels?
                        .Select((label, arm) => new KeyValuePair<Int128, int>(((Literal)label).Value, arm))
                        .ToImmutableDictionary();
    }

    /// <summary>Gets the selector expression, compiled during normalization.</summary>
    public Expr Selector { get; }

    /// <summary>
    ///     Gets each case value's arm index for a switch, or <see langword="null"/> for an <c>if</c>. Case values use
    ///     the whole 128-bit expression domain.
    /// </summary>
    public ImmutableDictionary<Int128, int>? CaseArms { get; }

    /// <summary>The arm a selector value chooses.</summary>
    /// <param name="value">The selector's value.</param>
    /// <returns>For an <c>if</c>, 1 when the value is nonzero and 0 otherwise; for a switch, the case's arm, or -1 (<c>default</c>).</returns>
    public int SelectArm(Int128 value)
        => this.CaseArms is { } cases
               ? cases.TryGetValue(value, out int arm) ? arm : -1
               : value != Int128.Zero ? 1 : 0;
}
