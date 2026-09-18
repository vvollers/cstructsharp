namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>Represents a struct or union with an immutable declaration-order field collection.</summary>
internal sealed class CompiledCompositeType : CompiledType
{
    private StructShape? shape;
    private StaticReadPlan? staticPlan;
    private System.Collections.Concurrent.ConcurrentDictionary<Type, TypedReadPlan?>? typedReadPlans;
    private bool staticPlanBuilt;

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

        // An anonymous nonzero-width bitfield has no name to key by, and several may coexist in one
        // composite without colliding with each other - exclude them rather than deduplicate on an empty key.
        this.FieldsByName = fields.
            Where(field => field.Declaration.Name.Name.Length > 0).
            ToImmutableDictionary(field => field.Declaration.Name.Name, StringComparer.Ordinal);

        // An anonymous promoted struct member has no name of its own; its own fields are spliced into
        // this composite's namespace instead. Computed once here so every splicing call site (reader, writer,
        // address resolver, layout) shares one definition instead of re-deriving the predicate independently.
        // One level only - a consumer that needs to see through transitive promotion reads a promoted field's own
        // compiled composite's PromotedFields again, rather than this set being pre-flattened.
        this.PromotedFields = fields.
            Where(field => field.Declaration is Struct { Name.Name.Length: 0, }).
            ToImmutableHashSet<CompiledField>(ReferenceEqualityComparer.Instance);
    }

    public ImmutableArray<CompiledField> Fields { get; }

    /// <summary>The declared name; empty for an anonymous inline composite.</summary>
    public string Name => this.Symbol.Name;

    /// <summary>Whether every member starts at the composite's own address.</summary>
    public bool IsUnion => this.Symbol.Kind == CompiledTypeKind.Union;

    public bool HasDirectConditionalFields { get; }

    public int ConditionalGroupCount { get; }

    public ImmutableArray<string> ConditionalLocalNames { get; private set; } = [];

    public ImmutableDictionary<string, CompiledField> FieldsByName { get; }

    public ImmutableHashSet<CompiledField> PromotedFields { get; }

    /// <summary>
    ///     The <see cref="StructValue"/> member layout every parse of this composite shares: declared names in
    ///     order, with anonymous promoted members spliced in and anonymous bitfields left out. Conditional
    ///     arms all get a slot; an arm that is not selected simply leaves its slot unset.
    /// </summary>
    public StructShape Shape => this.shape ??= this.BuildShape();

    /// <summary>The span read plan when every member is statically placed; null otherwise. Built on first use.</summary>
    public StaticReadPlan? StaticPlan
    {
        get
        {
            if (!this.staticPlanBuilt)
            {
                // Built after the whole model is bound; a benign race builds the same plan twice.
                this.staticPlan = StaticReadPlan.TryBuild(this);
                this.staticPlanBuilt = true;
            }

            return this.staticPlan;
        }
    }

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

    private StructShape BuildShape()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        this.CollectShapeNames(names, seen, new HashSet<CompiledCompositeType>(ReferenceEqualityComparer.Instance));
        return new StructShape(names.ToArray());
    }

    private void CollectShapeNames(List<string> names, HashSet<string> seen, HashSet<CompiledCompositeType> visiting)
    {
        if (!visiting.Add(this))
        {
            return;
        }

        foreach (CompiledField field in this.Fields)
        {
            if (this.PromotedFields.Contains(field))
            {
                if (field.Type.Symbol.Definition is CompiledCompositeType promoted)
                {
                    promoted.CollectShapeNames(names, seen, visiting);
                }

                continue;
            }

            string name = field.Declaration.Name.Name;
            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }

        visiting.Remove(this);
    }

    /// <summary>
    ///     Typed read plans bound to this composite's static plan, one per target type, created on the first
    ///     typed read so a layout never read into a POCO does not pay for the table.
    /// </summary>
    public TypedReadPlan? GetOrAddTypedReadPlan([DynamicallyAccessedMembers(TypedValueConverter.MappedMembers)] Type targetType)
    {
        System.Collections.Concurrent.ConcurrentDictionary<Type, TypedReadPlan?> plans = this.typedReadPlans ??
            System.Threading.Interlocked.CompareExchange(ref this.typedReadPlans, new System.Collections.Concurrent.ConcurrentDictionary<Type, TypedReadPlan?>(), null) ??
            this.typedReadPlans;
        if (plans.TryGetValue(targetType, out TypedReadPlan? plan))
        {
            return plan;
        }

        // Built with the annotated type in hand (a delegate would lose the annotation); a benign race builds twice.
        plan = TypedReadPlan.TryBuild(this.StaticPlan!, targetType);
        return plans.TryAdd(targetType, plan) ? plan : plans[targetType];
    }
}
