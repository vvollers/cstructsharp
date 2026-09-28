namespace CStructSharp.Compilation;

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
        for (int index = 0; index < this.selectedArms.Length; index++)
        {
            this.selectedArms[index] = int.MinValue;
        }
    }

    /// <summary>
    ///     Whether every arm a field sits in is selected, outermost first. A group not yet decided in this instance
    ///     evaluates its selector now; an inactive outer arm stops before inner selectors are evaluated.
    /// </summary>
    /// <param name="field">A field of the composite this selection was created for.</param>
    /// <param name="variables">The layout variables visible to the selectors.</param>
    /// <returns>Whether the field is present.</returns>
    internal bool IsActive(CompiledField field, IReadOnlyDictionary<string, Expr> variables)
    {
        foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
        {
            int selected = this.selectedArms[branch.Slot];
            if (selected == int.MinValue)
            {
                Int128 value = this.evaluator.Evaluate(branch.Group.Selector, variables, "conditional selector", this.domain);
                selected = branch.Group.SelectArm(value);
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
