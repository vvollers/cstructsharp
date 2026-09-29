namespace CStructSharp.Compilation.Programs;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CStructSharp.Syntax;

/// <summary>
///     The write programs of one compiled layout, compiled on first request and kept: one outcome per composite (shared
///     by every struct and root that holds it) and one per root name. Held by the layout's <see cref="SlotTable"/>, like
///     its <see cref="ReadProgramCache"/>, whose qualified publication targets the write programs share.
/// </summary>
/// <remarks>
///     Thread-safe in the way <see cref="ReadProgramCache"/> is: an outcome is compiled outside any lock and published with
///     <c>GetOrAdd</c>; two threads that race may both compile a composite, and the programs are equal.
/// </remarks>
internal sealed class WriteProgramCache
{
    private readonly ConcurrentDictionary<CompiledCompositeType, WriteProgramOutcome> composites = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, WriteProgramOutcome> roots = new(StringComparer.Ordinal);

    // The most recent root lookup, as in ReadProgramCache: a repeated write of the same root is one string comparison.
    private RootEntry? lastRoot;

    /// <summary>Creates the cache of one slot table.</summary>
    /// <param name="table">The layout's slot table.</param>
    public WriteProgramCache(SlotTable table)
    {
        this.Table = table;
    }

    /// <summary>Gets the slot table the programs index.</summary>
    public SlotTable Table { get; }

    /// <summary>Returns the program of a composite, compiling it on first request.</summary>
    /// <param name="compilation">The layout the composite belongs to (the compilation that owns this cache).</param>
    /// <param name="composite">The composite.</param>
    /// <returns>The program, or the reason the engine cannot write the composite yet.</returns>
    public WriteProgramOutcome GetComposite(LayoutCompilation compilation, CompiledCompositeType composite)
    {
        if (this.composites.TryGetValue(composite, out WriteProgramOutcome? outcome))
        {
            return outcome;
        }

        return this.composites.GetOrAdd(composite, new WriteProgramCompiler(compilation, this).CompileComposite(composite));
    }

    /// <summary>
    ///     Returns the program of a root, compiling it on first request. A name the layout does not declare is not cached,
    ///     so arbitrary names cannot grow the cache.
    /// </summary>
    /// <param name="compilation">The layout the root belongs to (the compilation that owns this cache).</param>
    /// <param name="rootName">The root's declared name, or a registered type spelling.</param>
    /// <returns>The program, or the reason the engine cannot write the root yet.</returns>
    public WriteProgramOutcome GetRoot(LayoutCompilation compilation, string rootName)
    {
        if (Volatile.Read(ref this.lastRoot) is { } last && string.Equals(last.Name, rootName, StringComparison.Ordinal))
        {
            return last.Outcome;
        }

        if (!this.roots.TryGetValue(rootName, out WriteProgramOutcome? outcome))
        {
            if (!compilation.ModelQueries.TryGetCompiledDeclaration(rootName, out CStructElement? declaration))
            {
                return WriteProgramOutcome.NotSupported(rootName + ": the layout declares no such root");
            }

            outcome = this.roots.GetOrAdd(rootName, new WriteProgramCompiler(compilation, this).CompileRoot(rootName, declaration));
        }

        Volatile.Write(ref this.lastRoot, new RootEntry(rootName, outcome));
        return outcome;
    }

    /// <summary>Returns the slots a capture of <paramref name="name"/> is published to under each qualified prefix.</summary>
    /// <param name="name">The bare field name.</param>
    /// <returns>The targets; empty when no expression spells the name with a prefix.</returns>
    public ReadProgram.QualifiedTarget[] GetQualifiedTargets(string name) => this.Table.ReadPrograms.GetQualifiedTargets(name);

    /// <summary>One cached root lookup: a declared root's name and its outcome.</summary>
    private sealed class RootEntry
    {
        /// <summary>Stores the lookup.</summary>
        /// <param name="name">The root's name.</param>
        /// <param name="outcome">The root's program or reason.</param>
        public RootEntry(string name, WriteProgramOutcome outcome)
        {
            this.Name = name;
            this.Outcome = outcome;
        }

        /// <summary>Gets the root's name.</summary>
        public string Name { get; }

        /// <summary>Gets the root's program or reason.</summary>
        public WriteProgramOutcome Outcome { get; }
    }
}
