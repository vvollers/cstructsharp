namespace CStructSharp;

using System.Collections.Generic;
using System.Linq;
using CStructSharp.Structure;

/// <summary>Protects a conditional composite's own fields from nested declarations with the same spelling.</summary>
internal sealed class ConditionalVariableScope
{
    private readonly Dictionary<string, Expr?> locals = new();

    internal ConditionalVariableScope(CompiledCompositeType composite, Dictionary<string, Expr> variables)
    {
        if (!composite.Fields.Any(field => field.Declaration.Condition is not null))
        {
            return;
        }

        foreach (CompiledField field in composite.Fields)
        {
            foreach (string name in GetVisibleNames(field))
            {
                this.locals[name] = null;
                variables.Remove(name);
            }
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
        if (this.locals.Count == 0)
        {
            return;
        }

        foreach (string name in GetVisibleNames(field))
        {
            if (this.locals.ContainsKey(name))
            {
                this.locals[name] = variables.GetValueOrDefault(name);
            }
        }

        foreach ((string local, Expr? value) in this.locals)
        {
            if (value is null)
            {
                variables.Remove(local);
            }
            else
            {
                variables[local] = value;
            }
        }
    }
}
