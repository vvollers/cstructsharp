namespace CStructSharp.Compilation.Programs;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
///     What the path resolver of one compiled layout needs, compiled on first request and kept: the walk of every struct
///     and union it passes (<see cref="TargetProgram"/>), the count of a root field, and the read of each member a path
///     can select - a field, an element or row of an array, or the target a pointer reaches. Held by the layout's
///     <see cref="SlotTable"/>, like the read programs, because the slot programs are that table's.
/// </summary>
/// <remarks>
///     Thread-safe: an entry is compiled outside any lock and published with <c>GetOrAdd</c>, so two threads that race may
///     both compile one (the results are equal) and every caller then uses the first published. A selected member's read
///     is keyed by the member's declared field and the number of indexes applied to it (a selection builds a new element
///     view every time), and a pointer's by the pointer levels it still has.
/// </remarks>
internal sealed class TargetProgramCache
{
    private readonly ConcurrentDictionary<CompiledCompositeType, TargetProgram> composites = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<CompiledField, TargetMember> roots = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<SelectionKey, ReadProgram> selections = new();
    private readonly ConcurrentDictionary<SelectionKey, ReadPointerTarget> pointers = new();
    private readonly ConcurrentDictionary<SelectionKey, CompiledField> views = new();

    /// <summary>Creates the cache of one slot table.</summary>
    /// <param name="table">The layout's slot table.</param>
    public TargetProgramCache(SlotTable table)
    {
        this.Table = table;
    }

    /// <summary>Gets the slot table the programs index.</summary>
    public SlotTable Table { get; }

    /// <summary>Returns the walk of a struct or union, compiling it on first request.</summary>
    /// <param name="compilation">The layout the composite belongs to (the compilation that owns this cache).</param>
    /// <param name="composite">The struct or union.</param>
    /// <returns>The program.</returns>
    public TargetProgram GetComposite(LayoutCompilation compilation, CompiledCompositeType composite)
    {
        if (this.composites.TryGetValue(composite, out TargetProgram? program))
        {
            return program;
        }

        return this.composites.GetOrAdd(composite, this.Compile(compilation, composite));
    }

    /// <summary>Returns a root field (a typedef or enum root) as a member walked on its own, compiling it on first request.</summary>
    /// <param name="compilation">The layout the field belongs to.</param>
    /// <param name="field">The root's compiled field.</param>
    /// <returns>The member.</returns>
    public TargetMember GetRootField(LayoutCompilation compilation, CompiledField field)
    {
        if (this.roots.TryGetValue(field, out TargetMember? member))
        {
            return member;
        }

        return this.roots.GetOrAdd(field, this.CreateMember(null, field, []));
    }

    /// <summary>
    ///     Returns the read of a selected member (<see cref="ReadProgramCompiler.CompileSelection"/>), compiling it from
    ///     <paramref name="view"/> on first request.
    /// </summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="declared">The member's declared field, which identifies the selection with <paramref name="indexes"/>.</param>
    /// <param name="indexes">The number of indexes the path applied to the member.</param>
    /// <param name="view">The selected field: <paramref name="declared"/> peeled once per index.</param>
    /// <returns>The program.</returns>
    /// <exception cref="InvalidOperationException">The member cannot be read, although the struct that holds it could.</exception>
    public ReadProgram GetSelection(LayoutCompilation compilation, CompiledField declared, int indexes, CompiledField view)
    {
        var key = new SelectionKey(declared, indexes, -1);
        if (this.selections.TryGetValue(key, out ReadProgram? program))
        {
            return program;
        }

        return this.selections.GetOrAdd(key, new ReadProgramCompiler(compilation, this.Table.ReadPrograms).CompileSelection(view));
    }

    /// <summary>
    ///     Returns how a pointer a path follows reads its target from the level <paramref name="view"/> describes
    ///     (<see cref="ReadProgramCompiler.DescribeSelectedPointer"/>), compiling it on first request.
    /// </summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="declared">The pointer member's declared field.</param>
    /// <param name="indexes">The number of indexes the path applied to the member.</param>
    /// <param name="view">The pointer at the level being read; its <see cref="CompiledField.PointerDepth"/> identifies the level.</param>
    /// <returns>The target description.</returns>
    /// <exception cref="InvalidOperationException">The target cannot be described, although the struct that holds the pointer could be read.</exception>
    public ReadPointerTarget GetPointerTarget(LayoutCompilation compilation, CompiledField declared, int indexes, CompiledField view)
    {
        var key = new SelectionKey(declared, indexes, view.PointerDepth);
        if (this.pointers.TryGetValue(key, out ReadPointerTarget? target))
        {
            return target;
        }

        ReadPointerTarget described = new ReadProgramCompiler(compilation, this.Table.ReadPrograms).DescribeSelectedPointer(view);
        return this.pointers.GetOrAdd(key, described);
    }

    /// <summary>
    ///     Returns the view of one element (or row) of a member after <paramref name="indexes"/> indexes, created from the
    ///     view one index shallower on first request: a compiled field is immutable, so every resolution of the same
    ///     selection shares one view instead of building its own.
    /// </summary>
    /// <param name="declared">The member's declared field.</param>
    /// <param name="indexes">The number of indexes applied, at least 1.</param>
    /// <param name="shape">The view after one index fewer (<paramref name="declared"/> itself for the first index).</param>
    /// <returns>The element view (<see cref="CompiledField.SelectArrayElement"/> of <paramref name="shape"/>).</returns>
    public CompiledField GetElementView(CompiledField declared, int indexes, CompiledField shape)
    {
        var key = new SelectionKey(declared, indexes, -1);
        return this.views.TryGetValue(key, out CompiledField? view) ? view : this.views.GetOrAdd(key, shape.SelectArrayElement());
    }

    /// <summary>
    ///     Returns the view of a pointer member with <paramref name="pointerDepth"/> levels left, created on first request
    ///     (<see cref="CompiledField.SelectPointerTarget"/> without a terminated codec), shared like <see cref="GetElementView"/>.
    /// </summary>
    /// <param name="declared">The pointer member's declared field.</param>
    /// <param name="indexes">The number of indexes applied to it.</param>
    /// <param name="pointer">The pointer (or its element view) whose levels are selected.</param>
    /// <param name="pointerDepth">The levels left, at least 1.</param>
    /// <param name="pointerSize">The layout's pointer width in bytes.</param>
    /// <returns>The view.</returns>
    public CompiledField GetPointerView(CompiledField declared, int indexes, CompiledField pointer, int pointerDepth, int pointerSize)
    {
        var key = new SelectionKey(declared, indexes, pointerDepth);
        return this.views.TryGetValue(key, out CompiledField? view) ? view : this.views.GetOrAdd(key, pointer.SelectPointerTarget(pointerDepth, null, pointerSize));
    }

    /// <summary>Compiles the walk of a composite: every member with its arms, count, capture slot and publication targets.</summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="composite">The struct or union.</param>
    /// <returns>The program.</returns>
    private TargetProgram Compile(LayoutCompilation compilation, CompiledCompositeType composite)
    {
        var tables = new ProgramTables(this.Table, composite.ConditionalGroupCount);
        var members = new TargetMember[composite.Fields.Length];
        var branches = new List<int>();
        for (int index = 0; index < members.Length; index++)
        {
            CompiledField field = composite.Fields[index];

            // A union's members are never selected by their conditions: the resolver reads every member from the union's
            // first byte without evaluating them.
            branches.Clear();
            if (!composite.IsUnion)
            {
                foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
                {
                    branches.Add(tables.AddBranch(branch));
                }
            }

            members[index] = this.CreateMember(composite, field, branches.ToArray());
        }

        // The resolver removes the kept names at entry and saves and restores them after each member, as a read does.
        ConditionalScopeSlots? scope = composite.HasDirectConditionalFields && !composite.IsUnion
                                          ? new ConditionalScopeSlots(composite.ConditionalScope!, this.Table)
                                          : null;
        return new TargetProgram(composite, members, tables, scope);
    }

    /// <summary>Describes one member for the walk.</summary>
    /// <param name="composite">The struct or union that holds the member, or <see langword="null"/> for a root field.</param>
    /// <param name="field">The member's compiled field.</param>
    /// <param name="branches">The indexes of the arms it sits in.</param>
    /// <returns>The member.</returns>
    private TargetMember CreateMember(CompiledCompositeType? composite, CompiledField field, int[] branches)
    {
        // A runtime-sized array's count is evaluated against the slots; every other count is a constant of the shape.
        ProgramExpression? count = field.Array.Kind == CompiledArrayKind.Runtime ? this.Table.Compile(field.Array.CountExpression!) : null;

        // A capture no expression can read is not observable; its value is still read, because the bytes are charged.
        int slot = field.CapturesLayoutVariable && this.Table.TryGetSlot(field.Name, out int found) ? found : -1;
        QualifiedTarget[] qualified = slot >= 0 ? this.Table.ReadPrograms.GetQualifiedTargets(field.Name) : [];
        bool promoted = composite is not null && composite.PromotedFields.Contains(field);
        return new TargetMember(field, branches, count, slot, qualified, promoted);
    }

    /// <summary>The identity of a selected member's read, view, or a pointer level's target description or view.</summary>
    /// <param name="Declared">The member's declared field, compared by reference.</param>
    /// <param name="Indexes">The number of indexes the path applied to it.</param>
    /// <param name="PointerDepth">The pointer level described, or -1 for a member's read or element view.</param>
    private readonly record struct SelectionKey(CompiledField Declared, int Indexes, int PointerDepth);
}
