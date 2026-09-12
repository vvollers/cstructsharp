namespace CStructSharp;

using System;
using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>Selects each group once at entry; all its later fields reuse that decision.</summary>
internal sealed class ConditionalFieldSelection
{
    private readonly LayoutExpressionEvaluator evaluator;
    private readonly int[] selectedArms;

    internal ConditionalFieldSelection(LayoutExpressionEvaluator evaluator, int groupCount)
    {
        this.evaluator = evaluator;
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
                int value = this.evaluator.Evaluate(branch.Group.Selector, variables, "conditional selector");
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
