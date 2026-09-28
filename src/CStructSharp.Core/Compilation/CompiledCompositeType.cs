namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>Represents a struct or union with an immutable declaration-order field collection.</summary>
internal sealed partial class CompiledCompositeType : CompiledType
{
    private StructShape? shape;

    private CompiledConditionalScope? conditionalScope;

    /// <summary>0 until <see cref="ReachesConditionalMembers"/> is first computed, then 1 (no) or 2 (yes).</summary>
    private int reachesConditionalMembers;

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

    /// <summary>Gets the placed members in declaration order, including anonymous and conditional members.</summary>
    public ImmutableArray<CompiledField> Fields { get; }

    /// <summary>The declared name; empty for an anonymous inline composite.</summary>
    public string Name => this.Symbol.Name;

    /// <summary>Whether every member starts at the composite's own address.</summary>
    public bool IsUnion => this.Symbol.Kind == CompiledTypeKind.Union;

    /// <summary>Gets a value indicating whether a member at this level is an <c>if</c>/<c>switch</c> arm.</summary>
    public bool HasDirectConditionalFields { get; }

    /// <summary>The number of <c>if</c>/<c>switch</c> decisions among the members: the length of an operation's selected-arm array.</summary>
    public int ConditionalGroupCount { get; }

    /// <summary>
    ///     Whether this composite or any type reachable from it - member types and pointer targets - has an <c>if</c> or
    ///     <c>switch</c> member, so an update must read the layout around its target. Computed on first use; a race only
    ///     computes the same answer twice.
    /// </summary>
    public bool ReachesConditionalMembers
    {
        get
        {
            int state = Volatile.Read(ref this.reachesConditionalMembers);
            if (state == 0)
            {
                state = this.FindConditionalMember() ? 2 : 1;
                Volatile.Write(ref this.reachesConditionalMembers, state);
            }

            return state == 2;
        }
    }

    /// <summary>
    ///     The layout variables kept for the members while nested declarations are read; <see langword="null"/> when no
    ///     member is conditional. Built on first use, once every composite of the layout is compiled.
    /// </summary>
    public CompiledConditionalScope? ConditionalScope
        => this.HasDirectConditionalFields ? this.conditionalScope ??= new CompiledConditionalScope(this) : null;

    /// <summary>
    ///     Gets this level's named members by exact (case-sensitive) name; anonymous bit-fields and promoted members'
    ///     own fields are not included.
    /// </summary>
    public ImmutableDictionary<string, CompiledField> FieldsByName { get; }

    /// <summary>
    ///     Gets the anonymous struct or union members whose own fields are addressed as if declared here, compared by
    ///     reference; one level only.
    /// </summary>
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

    /// <summary>Walks the types reachable from this composite, each once, for a conditional member.</summary>
    /// <returns>Whether one is found.</returns>
    private bool FindConditionalMember()
    {
        var pending = new Stack<CompiledTypeSymbol>();
        var visited = new HashSet<CompiledTypeSymbol>();
        pending.Push(this.Symbol);
        while (pending.Count > 0)
        {
            CompiledTypeSymbol symbol = pending.Pop();
            if (!visited.Add(symbol) || symbol.Definition is not CompiledCompositeType composite)
            {
                continue;
            }

            foreach (CompiledField field in composite.Fields)
            {
                if (field.IsConditional)
                {
                    return true;
                }

                pending.Push(field.Type.Symbol);
            }
        }

        return false;
    }

    /// <summary>Builds the shared value shape: declared names in order, promoted members spliced in.</summary>
    /// <returns>The shape.</returns>
    private StructShape BuildShape()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        this.CollectShapeNames(names, seen, new HashSet<CompiledCompositeType>(ReferenceEqualityComparer.Instance));
        return new StructShape(names.ToArray());
    }

    /// <summary>Appends this composite's member names to a shape, splicing in promoted members' names.</summary>
    /// <param name="names">The shape's names in order; appended to.</param>
    /// <param name="seen">The names already added, so a repeated name keeps its first slot.</param>
    /// <param name="visiting">The composites on the current splice path, which stops a cycle.</param>
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
    /// <param name="name">The case-sensitive field name, searched here and in promoted anonymous members.</param>
    /// <returns>The compiled field with that name.</returns>
    /// <exception cref="CStructPathException">No field with that name exists.</exception>
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
    /// <param name="name">The case-sensitive field name.</param>
    /// <param name="field">The found field, or null when the name is unknown.</param>
    /// <returns>True when the field exists at this level or inside a promoted member.</returns>
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
