namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Syntax;

/// <summary>
///     The layout variables a composite with <c>if</c>/<c>switch</c> members keeps for its own fields: while a member is
///     read, nested declarations with the same name must not replace the value a later selector reads. Indexed by
///     <see cref="CompiledField.MemberIndex"/>.
/// </summary>
/// <remarks>
///     Built on first use rather than with the composite: a member may point to a composite that is compiled later
///     (a recursive pointer), and the restore sets walk those composites' fields.
/// </remarks>
internal sealed class CompiledConditionalScope
{
    /// <summary>Builds the scope from the composite's members and the composites they contain.</summary>
    /// <param name="composite">A composite whose every reachable composite is compiled.</param>
    public CompiledConditionalScope(CompiledCompositeType composite)
    {
        ImmutableArray<CompiledField> fields = composite.Fields;
        var names = ImmutableArray.CreateBuilder<string>();
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        var visible = ImmutableArray.CreateBuilder<ImmutableArray<string>>(fields.Length);
        foreach (CompiledField field in fields)
        {
            string fieldName = field.Declaration.Name.Name;
            ImmutableArray<string> fieldNames = fieldName.Length > 0
                                                    ? ImmutableArray.Create(fieldName)
                                                    : GetVisibleNames(field).ToImmutableArray();
            visible.Add(fieldNames);
            foreach (string name in fieldNames)
            {
                if (slots.TryAdd(name, names.Count))
                {
                    names.Add(name);
                }
            }
        }

        this.LocalNames = names.ToImmutable();
        this.VisibleNames = visible.MoveToImmutable();

        var captured = ImmutableArray.CreateBuilder<ImmutableArray<int>>(fields.Length);
        var restored = ImmutableArray.CreateBuilder<ImmutableArray<int>>(fields.Length);
        for (int index = 0; index < fields.Length; index++)
        {
            CompiledField field = fields[index];
            ImmutableArray<string> fieldNames = this.VisibleNames[index];
            captured.Add(fieldNames.Length == 1
                             ? ImmutableArray.Create(slots[fieldNames[0]])
                             : fieldNames.Select(name => slots[name]).ToImmutableArray());
            restored.Add(field.Type.Symbol.Definition is CompiledCompositeType
                             ? RestoredSlots(field, fieldNames, slots)
                             : ImmutableArray<int>.Empty);
        }

        this.CapturedLocalSlots = captured.MoveToImmutable();
        this.RestoredLocalSlots = restored.MoveToImmutable();
    }

    /// <summary>Gets the names the scope keeps, each with its slot index.</summary>
    public ImmutableArray<string> LocalNames { get; }

    /// <summary>Gets each member's names in the result: its own, or an anonymous promoted member's promoted names.</summary>
    public ImmutableArray<ImmutableArray<string>> VisibleNames { get; }

    /// <summary>Gets the slots each member's own values are saved to once it is read.</summary>
    public ImmutableArray<ImmutableArray<int>> CapturedLocalSlots { get; }

    /// <summary>Gets the slots each member restores: the composite's names that a nested declaration inside it may have replaced.</summary>
    public ImmutableArray<ImmutableArray<int>> RestoredLocalSlots { get; }

    /// <summary>Enumerates result member names, following only anonymous promotion, never named children.</summary>
    /// <param name="field">The member.</param>
    /// <returns>The names the member contributes to its composite's result.</returns>
    private static IEnumerable<string> GetVisibleNames(CompiledField field)
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

    /// <summary>The slots of the scope's names that a composite member's nested declarations can overwrite, other than its own names.</summary>
    /// <param name="field">A member of composite type.</param>
    /// <param name="fieldNames">The member's own visible names.</param>
    /// <param name="slots">The scope's slot of each name.</param>
    /// <returns>The slots to restore after the member is read.</returns>
    private static ImmutableArray<int> RestoredSlots(CompiledField field, ImmutableArray<string> fieldNames, Dictionary<string, int> slots)
    {
        var overwritten = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<CompiledTypeSymbol>();
        var pending = new Stack<CompiledTypeSymbol>();
        pending.Push(field.Type.Symbol);
        while (pending.Count > 0)
        {
            CompiledTypeSymbol symbol = pending.Pop();
            if (!visited.Add(symbol) || symbol.Definition is not CompiledCompositeType nested)
            {
                continue;
            }

            foreach (CompiledField child in nested.Fields)
            {
                overwritten.Add(child.Declaration.Name.Name);
                pending.Push(child.Type.Symbol);
            }
        }

        overwritten.ExceptWith(fieldNames);
        return overwritten.Where(slots.ContainsKey).Select(name => slots[name]).ToImmutableArray();
    }
}
