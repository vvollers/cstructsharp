namespace CStructSharp.Compilation.Programs;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CStructSharp.Syntax;

/// <summary>
///     The read programs of one compiled layout, compiled on first request and kept: one outcome per composite (shared by
///     every struct and root that holds it) and one per root name. Held by the layout's <see cref="SlotTable"/>, because a
///     program's slots are that table's; neither exists until an operation asks for them, so constructing a layout
///     compiles no program.
/// </summary>
/// <remarks>
///     Thread-safe: an outcome is compiled outside any lock and published with <c>GetOrAdd</c>, so two threads that race
///     may both compile a composite (the programs are equal) and every caller then uses the first one published. A
///     nested struct's program may be the losing copy; it reads the same.
/// </remarks>
internal sealed class ReadProgramCache
{
    private static readonly ReadProgram.QualifiedTarget[] NoTargets = [];

    private readonly ConcurrentDictionary<CompiledCompositeType, ReadProgramOutcome> composites = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, ReadProgramOutcome> roots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReadProgram.QualifiedTarget[]> qualifiedTargets;

    // The most recent root lookup. Callers read the same root call after call, so a repeated lookup is one string
    // comparison instead of a dictionary lookup; the entry is immutable and replaced whole, so no reader sees it torn.
    private RootEntry? lastRoot;

    /// <summary>Creates the cache of one slot table and indexes its qualified names.</summary>
    /// <param name="table">The layout's slot table.</param>
    public ReadProgramCache(SlotTable table)
    {
        this.Table = table;

        // Every slot spelled `prefix.name` is a place a capture of `name` is published to while `prefix.` is active
        // (field names contain no dots, so the bare name is the last segment). A name with no such slot is published
        // nowhere observable.
        var targets = new Dictionary<string, List<ReadProgram.QualifiedTarget>>(StringComparer.Ordinal);
        for (int slot = 0; slot < table.Count; slot++)
        {
            string name = table.GetName(slot);
            int dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1)
            {
                continue;
            }

            string bare = name.Substring(dot + 1);
            if (!targets.TryGetValue(bare, out List<ReadProgram.QualifiedTarget>? list))
            {
                targets.Add(bare, list = []);
            }

            list.Add(new ReadProgram.QualifiedTarget(name.Substring(0, dot + 1), slot));
        }

        this.qualifiedTargets = new Dictionary<string, ReadProgram.QualifiedTarget[]>(targets.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<ReadProgram.QualifiedTarget>> entry in targets)
        {
            this.qualifiedTargets.Add(entry.Key, entry.Value.ToArray());
        }
    }

    /// <summary>Gets the slot table the programs index.</summary>
    public SlotTable Table { get; }

    /// <summary>Returns the program of a composite, compiling it on first request.</summary>
    /// <param name="compilation">The layout the composite belongs to (the compilation that owns this cache).</param>
    /// <param name="composite">The composite.</param>
    /// <returns>The program, or the reason the engine cannot read the composite yet.</returns>
    public ReadProgramOutcome GetComposite(LayoutCompilation compilation, CompiledCompositeType composite)
    {
        if (this.composites.TryGetValue(composite, out ReadProgramOutcome? outcome))
        {
            return outcome;
        }

        return this.composites.GetOrAdd(composite, new ReadProgramCompiler(compilation, this).CompileComposite(composite));
    }

    /// <summary>
    ///     Returns the program of a root, compiling it on first request. A name the layout does not declare (nor has
    ///     registered as a type-spelling root) is not cached, so arbitrary names cannot grow the cache.
    /// </summary>
    /// <param name="compilation">The layout the root belongs to (the compilation that owns this cache).</param>
    /// <param name="rootName">The root's declared name, or a registered type spelling.</param>
    /// <returns>The program, or the reason the engine cannot read the root yet.</returns>
    public ReadProgramOutcome GetRoot(LayoutCompilation compilation, string rootName)
    {
        if (Volatile.Read(ref this.lastRoot) is { } last && string.Equals(last.Name, rootName, StringComparison.Ordinal))
        {
            return last.Outcome;
        }

        if (!this.roots.TryGetValue(rootName, out ReadProgramOutcome? outcome))
        {
            if (!compilation.ModelQueries.TryGetCompiledDeclaration(rootName, out CStructElement? declaration))
            {
                return ReadProgramOutcome.NotSupported(rootName + ": the layout declares no such root");
            }

            outcome = this.roots.GetOrAdd(rootName, new ReadProgramCompiler(compilation, this).CompileRoot(rootName, declaration));
        }

        Volatile.Write(ref this.lastRoot, new RootEntry(rootName, outcome));
        return outcome;
    }

    /// <summary>Returns the slots a capture of <paramref name="name"/> is published to under each qualified prefix.</summary>
    /// <param name="name">The bare field name.</param>
    /// <returns>The targets; empty when no expression spells the name with a prefix.</returns>
    public ReadProgram.QualifiedTarget[] GetQualifiedTargets(string name)
        => this.qualifiedTargets.TryGetValue(name, out ReadProgram.QualifiedTarget[]? targets) ? targets : NoTargets;

    /// <summary>One cached root lookup: a declared root's name and its outcome.</summary>
    private sealed class RootEntry
    {
        /// <summary>Stores the lookup.</summary>
        /// <param name="name">The root's name.</param>
        /// <param name="outcome">The root's program or reason.</param>
        public RootEntry(string name, ReadProgramOutcome outcome)
        {
            this.Name = name;
            this.Outcome = outcome;
        }

        /// <summary>Gets the root's name.</summary>
        public string Name { get; }

        /// <summary>Gets the root's program or reason.</summary>
        public ReadProgramOutcome Outcome { get; }
    }
}
