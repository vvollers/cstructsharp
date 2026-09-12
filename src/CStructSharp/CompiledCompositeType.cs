namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Structure;

/// <summary>Represents a struct or union with an immutable declaration-order field collection.</summary>
internal sealed class CompiledCompositeType : CompiledType
{
    public CompiledCompositeType(CompiledTypeSymbol symbol, ImmutableArray<CompiledField> fields)
        : base(symbol)
    {
        this.Fields = fields;
        this.HasDirectConditionalFields = fields.Any(field => field.Declaration.Condition is not null);
        if (this.HasDirectConditionalFields)
        {
            var groups = new Dictionary<ConditionalGroup, int>();
            foreach (CompiledField field in fields)
            {
                if (field.Declaration.BranchConditions.Count == 0)
                {
                    continue;
                }

                var branches = ImmutableArray.CreateBuilder<CompiledConditionalBranch>(field.Declaration.BranchConditions.Count);
                foreach (ConditionalBranch branch in field.Declaration.BranchConditions)
                {
                    if (!groups.TryGetValue(branch.Group, out int slot))
                    {
                        slot = groups.Count;
                        groups.Add(branch.Group, slot);
                    }

                    branches.Add(new CompiledConditionalBranch(branch.Group, slot, branch.Arm));
                }

                field.ConditionalBranches = branches.MoveToImmutable();
            }

            this.ConditionalGroupCount = groups.Count;
        }

        // An anonymous nonzero-width bitfield (LANG-17) has no name to key by, and several may coexist in one
        // composite without colliding with each other - exclude them rather than deduplicate on an empty key.
        this.FieldsByName = fields.
            Where(field => field.Declaration.Name.Name.Length > 0).
            ToImmutableDictionary(field => field.Declaration.Name.Name, StringComparer.Ordinal);

        // An anonymous promoted struct member (LANG-14) has no name of its own; its own fields are spliced into
        // this composite's namespace instead. Computed once here so every splicing call site (reader, writer,
        // address resolver, layout) shares one definition instead of re-deriving the predicate independently.
        // One level only - a consumer that needs to see through transitive promotion reads a promoted field's own
        // compiled composite's PromotedFields again, rather than this set being pre-flattened.
        this.PromotedFields = fields.
            Where(field => field.Declaration is Struct { Name.Name.Length: 0, }).
            ToImmutableHashSet<CompiledField>(ReferenceEqualityComparer.Instance);
    }

    public ImmutableArray<CompiledField> Fields { get; }

    public bool HasDirectConditionalFields { get; }

    public int ConditionalGroupCount { get; }

    public ImmutableArray<string> ConditionalLocalNames { get; private set; } = [];

    public ImmutableDictionary<string, CompiledField> FieldsByName { get; }

    public ImmutableHashSet<CompiledField> PromotedFields { get; }

    /// <summary>Finishes scope metadata after recursive pointer symbols have all been bound.</summary>
    internal void CompleteConditionalScope()
    {
        if (!this.HasDirectConditionalFields)
        {
            return;
        }

        var names = ImmutableArray.CreateBuilder<string>();
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (CompiledField field in this.Fields)
        {
            string fieldName = field.Declaration.Name.Name;
            field.VisibleNames = fieldName.Length > 0
                ? [fieldName]
                : ConditionalVariableScope.GetVisibleNames(field).ToImmutableArray();
            foreach (string name in field.VisibleNames)
            {
                if (slots.TryAdd(name, names.Count))
                {
                    names.Add(name);
                }
            }
        }

        this.ConditionalLocalNames = names.ToImmutable();
        foreach (CompiledField field in this.Fields)
        {
            field.CapturedLocalSlots = field.VisibleNames.Length == 1
                ? [slots[field.VisibleNames[0]]]
                : field.VisibleNames.Select(name => slots[name]).ToImmutableArray();
            if (field.Type.Symbol.Definition is not CompiledCompositeType)
            {
                // Primitive/enum storage cannot introduce a nested declaration that shadows a sibling.
                continue;
            }

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

            overwritten.ExceptWith(field.VisibleNames);
            field.RestoredLocalSlots = overwritten.Where(slots.ContainsKey).Select(name => slots[name]).ToImmutableArray();
        }
    }
}
