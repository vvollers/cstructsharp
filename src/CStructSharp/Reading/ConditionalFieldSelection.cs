namespace CStructSharp.Reading;

using System;
using System.Collections.Generic;
using CStructSharp.Compilation;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>Selects each group once at entry; all its later fields reuse that decision.</summary>
internal sealed class ConditionalFieldSelection
{
    private readonly LayoutExpressionEvaluator evaluator;
    private readonly ExpressionFailureDomain domain;
    private readonly int[] selectedArms;

    /// <summary>Creates the per-operation selection state for one composite.</summary>
    /// <param name="evaluator">The layout's expression evaluator.</param>
    /// <param name="groupCount">The number of conditional groups directly inside the composite.</param>
    /// <param name="domain">Which operation evaluates the selectors, so a selector that cannot be evaluated fails as that operation.</param>
    internal ConditionalFieldSelection(LayoutExpressionEvaluator evaluator, int groupCount, ExpressionFailureDomain domain = ExpressionFailureDomain.Read)
    {
        this.evaluator = evaluator;
        this.domain = domain;
        this.selectedArms = new int[groupCount];
        Array.Fill(this.selectedArms, int.MinValue);
    }

    internal bool IsActive(CompiledField field, IReadOnlyDictionary<string, Expr> variables)
    {
        foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
        {
            int selected = this.selectedArms[branch.Slot];
            if (selected == int.MinValue)
            {
                int value = this.evaluator.Evaluate(branch.Group.Selector, variables, "conditional selector", this.domain);
                selected = branch.Group.CaseArms is { } cases ? cases.GetValueOrDefault(value, -1) : value != 0 ? 1 : 0;
                this.selectedArms[branch.Slot] = selected;
            }

            if (selected != branch.Arm)
            {
                return false;
            }
        }

        return true;
    }
}
