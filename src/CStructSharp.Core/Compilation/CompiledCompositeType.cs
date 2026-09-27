namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>Represents a struct or union with an immutable declaration-order field collection.</summary>
internal sealed partial class CompiledCompositeType : CompiledType
{
    private StructShape? shape;

    private CompiledConditionalScope? conditionalScope;

    /// <summary>Creates the composite from its placed members.</summary>
    /// <param name="symbol">The composite's type symbol.</param>
    /// <param name="fields">The members in declaration order, built by <see cref="CompiledField.WithPlacement"/>.</param>
    public CompiledCompositeType(CompiledTypeSymbol symbol, ImmutableArray<CompiledField> fields)
        : base(symbol)
    {
        this.Fields = fields;
        this.HasDirectConditionalFields = fields.Any(field => field.IsConditional);
        foreach (CompiledField field in fields)
        {
            foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
            {
                this.ConditionalGroupCount = Math.Max(this.ConditionalGroupCount, branch.Slot + 1);
            }
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

    /// <summary>The number of <c>if</c>/<c>switch</c> decisions among the members: the length of an operation's selected-arm array.</summary>
    public int ConditionalGroupCount { get; }

    /// <summary>
    ///     The layout variables kept for the members while nested declarations are read; <see langword="null"/> when no
    ///     member is conditional. Built on first use, once every composite of the layout is compiled.
    /// </summary>
    public CompiledConditionalScope? ConditionalScope
        => this.HasDirectConditionalFields ? this.conditionalScope ??= new CompiledConditionalScope(this) : null;

    public ImmutableDictionary<string, CompiledField> FieldsByName { get; }

    public ImmutableHashSet<CompiledField> PromotedFields { get; }

    /// <summary>
    ///     The <see cref="CStructSharp.Values.StructValue"/> member layout every parse of this composite shares: declared names in
    ///     order, with anonymous promoted members spliced in and anonymous bitfields left out. Conditional
    ///     arms all get a slot; an arm that is not selected simply leaves its slot unset.
    /// </summary>
    public StructShape Shape => this.shape ??= this.BuildShape();

    /// <summary>
    ///     Numbers the <c>if</c>/<c>switch</c> decisions of a composite's members in declaration order and compiles each
    ///     conditional member's arms; every arm of one decision shares its <see cref="CompiledConditionalGroup"/>.
    /// </summary>
    /// <param name="fields">The members in declaration order.</param>
    /// <returns>Each member's arms by position, or <see langword="null"/> when no member is conditional.</returns>
    internal static ImmutableArray<CompiledConditionalBranch>[]? CompileConditionalBranches(IReadOnlyList<CompiledField> fields)
    {
        ImmutableArray<CompiledConditionalBranch>[]? result = null;
        Dictionary<ConditionalGroup, CompiledConditionalBranch>? groups = null;
        for (int index = 0; index < fields.Count; index++)
        {
            IReadOnlyList<ConditionalBranch> declared = fields[index].Declaration.BranchConditions;
            if (declared.Count == 0)
            {
                continue;
            }

            result ??= new ImmutableArray<CompiledConditionalBranch>[fields.Count];
            groups ??= new Dictionary<ConditionalGroup, CompiledConditionalBranch>();
            var branches = ImmutableArray.CreateBuilder<CompiledConditionalBranch>(declared.Count);
            foreach (ConditionalBranch branch in declared)
            {
                if (!groups.TryGetValue(branch.Group, out CompiledConditionalBranch decision))
                {
                    decision = new CompiledConditionalBranch(new CompiledConditionalGroup(branch.Group), groups.Count, 0);
                    groups.Add(branch.Group, decision);
                }

                branches.Add(decision with { Arm = branch.Arm });
            }

            result[index] = branches.MoveToImmutable();
        }

        return result;
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

    /// <summary>Finds one exact compiled field name in this composite, or throws a <see cref="CStructPathException"/> naming both.</summary>
    public CompiledField FindField(string name)
    {
        return this.TryFindField(name, out CompiledField? field)
            ? field!
            : throw new CStructPathException($"Unknown field '{name}' in '{this.Name}'.");
    }

    /// <summary>
    ///     Finds one exact compiled field name, recursing into every anonymous promoted member's own fields when the
    ///     name isn't one of this level's own - never throws. Purely an in-memory, side-effect-free tree walk, so it
    ///     is safe to call speculatively before attempting a real, I/O-touching resolution.
    /// </summary>
    public bool TryFindField(string name, out CompiledField? field)
    {
        if (this.FieldsByName.TryGetValue(name, out field))
        {
            return true;
        }

        foreach (CompiledField promoted in this.PromotedFields)
        {
            if (promoted.Composite is { } promotedStruct &&
                promotedStruct.TryFindField(name, out field))
            {
                return true;
            }
        }

        field = null;
        return false;
    }
}
