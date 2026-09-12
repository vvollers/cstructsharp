namespace CStructSharp;

using System.Collections.Generic;
using System.Collections.Immutable;
using CStructSharp.Structure;

/// <summary>Protects a conditional composite's own fields from nested declarations with the same spelling.</summary>
internal sealed class ConditionalVariableScope
{
    private readonly ImmutableArray<string> names;
    private readonly Expr?[] locals;

    internal ConditionalVariableScope(CompiledCompositeType composite, Dictionary<string, Expr> variables)
    {
        this.names = composite.ConditionalLocalNames;
        this.locals = new Expr?[this.names.Length];
        foreach (string name in this.names)
        {
            variables.Remove(name);
        }
    }

    /// <summary>Enumerates result member names, following only anonymous promotion, never named children.</summary>
    internal static IEnumerable<string> GetVisibleNames(CompiledField field)
    {
        var pending = new Stack<CompiledField>();
        pending.Push(field);
        while (pending.Count > 0)
        {
            CompiledField current = pending.Pop();
            string name = current.Declaration.Name.Name;
            if (name.Length > 0)
            {
                yield return name;
            }
            else if (current.Declaration is Struct && current.Type.Symbol.Definition is CompiledCompositeType promoted)
            {
                foreach (CompiledField child in promoted.Fields)
                {
                    pending.Push(child);
                }
            }
        }
    }

    internal void CompleteField(CompiledField field, Dictionary<string, Expr> variables)
    {
        foreach (int slot in field.CapturedLocalSlots)
        {
            this.locals[slot] = variables.GetValueOrDefault(this.names[slot]);
        }

        foreach (int slot in field.RestoredLocalSlots)
        {
            Expr? value = this.locals[slot];
            if (value is null)
            {
                variables.Remove(this.names[slot]);
            }
            else
            {
                variables[this.names[slot]] = value;
            }
        }
    }
}
