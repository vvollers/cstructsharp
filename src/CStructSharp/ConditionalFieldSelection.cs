namespace CStructSharp;

using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>Freezes each branch environment when its group is reached in one composite instance.</summary>
internal sealed class ConditionalFieldSelection
{
    private readonly LayoutExpressionEvaluator evaluator;
    private readonly Dictionary<object, IReadOnlyDictionary<string, Expr>> environments = new();

    internal ConditionalFieldSelection(LayoutExpressionEvaluator evaluator)
    {
        this.evaluator = evaluator;
    }

    internal bool IsActive(CompiledField field, IReadOnlyDictionary<string, Expr> variables)
    {
        foreach ((object group, Expr predicate) in field.Declaration.BranchConditions)
        {
            if (!this.environments.TryGetValue(group, out IReadOnlyDictionary<string, Expr>? snapshot))
            {
                snapshot = new Dictionary<string, Expr>(variables);
                this.environments.Add(group, snapshot);
            }

            if (this.evaluator.Evaluate(predicate, snapshot, "condition for " + field.Declaration.Name.Name) == 0)
            {
                return false;
            }
        }

        return true;
    }
}
