namespace CStructSharp.Compilation;

using System.Collections.Generic;
using System.Collections.Immutable;
using CStructSharp.Compilation;
using CStructSharp.Syntax;

/// <summary>Protects a conditional composite's own fields from nested declarations with the same spelling.</summary>
internal sealed class ConditionalVariableScope
{
    private readonly CompiledConditionalScope scope;
    private readonly ImmutableArray<string> names;
    private readonly Expr?[] locals;

    /// <summary>Starts a composite's scope: its kept names are removed from the variables, so an outer value is not read as its own.</summary>
    /// <param name="composite">A composite with conditional members.</param>
    /// <param name="variables">The operation's layout variables; modified.</param>
    internal ConditionalVariableScope(CompiledCompositeType composite, Dictionary<string, Expr> variables)
    {
        this.scope = composite.ConditionalScope!;
        this.names = this.scope.LocalNames;
        this.locals = new Expr?[this.names.Length];
        foreach (string name in this.names)
        {
            variables.Remove(name);
        }
    }

    /// <summary>After a member is read, saves its own values and restores the composite's names a nested declaration replaced.</summary>
    /// <param name="field">The member just read.</param>
    /// <param name="variables">The operation's layout variables; modified.</param>
    internal void CompleteField(CompiledField field, Dictionary<string, Expr> variables)
    {
        foreach (int slot in this.scope.CapturedLocalSlots[field.MemberIndex])
        {
            this.locals[slot] = variables.TryGetValue(this.names[slot], out Expr? value) ? value : null;
        }

        foreach (int slot in this.scope.RestoredLocalSlots[field.MemberIndex])
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
