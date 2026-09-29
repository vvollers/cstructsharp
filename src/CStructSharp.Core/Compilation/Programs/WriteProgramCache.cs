namespace CStructSharp.Compilation.Programs;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CStructSharp.Codecs;
using CStructSharp.Syntax;

/// <summary>
///     The write programs of one compiled layout, compiled on first request and kept: one outcome per composite (shared
///     by every struct and root that holds it), one per root name, one per member a nested path selects, and one per
///     pointed-to value an update's <c>.value</c> path selects. Held by the layout's <see cref="SlotTable"/>, like
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
    private readonly ConcurrentDictionary<MemberKey, WriteProgramOutcome> members = new();

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

    /// <summary>
    ///     Returns the program that writes one member on its own - the member a nested path selects, with the dimensions its
    ///     indexes peel - compiling it on first request. The key is the declared member and the peeled dimension count, both
    ///     bounded by the layout, so paths cannot grow the cache without bound.
    /// </summary>
    /// <param name="compilation">The layout the member belongs to (the compilation that owns this cache).</param>
    /// <param name="declared">The declared member.</param>
    /// <param name="peeled">The number of array dimensions the path's indexes select into.</param>
    /// <returns>The program, or the reason the engine cannot write the member yet.</returns>
    public WriteProgramOutcome GetMember(LayoutCompilation compilation, CompiledField declared, int peeled)
    {
        var key = new MemberKey(declared, peeled, -1);
        if (this.members.TryGetValue(key, out WriteProgramOutcome? outcome))
        {
            return outcome;
        }

        return this.members.GetOrAdd(key, new WriteProgramCompiler(compilation, this).CompileMember(Peel(declared, peeled)));
    }

    /// <summary>
    ///     Returns the program that writes the storage a pointer member's <c>.value</c> accessors reach - the pointed-to
    ///     value, or with levels left the pointer stored there - on its own, as an update writes it, compiling it on first
    ///     request. The storage is described as the interpreter's resolver describes it: the pointer's view with
    ///     <paramref name="remaining"/> levels, a terminated string's codec for a pointed-to text value.
    /// </summary>
    /// <param name="compilation">The layout the member belongs to (the compilation that owns this cache).</param>
    /// <param name="declared">The declared pointer member.</param>
    /// <param name="peeled">The number of array dimensions the path's indexes select into.</param>
    /// <param name="remaining">The pointer levels left after the followed accessors.</param>
    /// <returns>The program, or the reason the engine cannot write the storage yet.</returns>
    public WriteProgramOutcome GetPointee(LayoutCompilation compilation, CompiledField declared, int peeled, int remaining)
    {
        var key = new MemberKey(declared, peeled, remaining);
        if (this.members.TryGetValue(key, out WriteProgramOutcome? outcome))
        {
            return outcome;
        }

        CompiledField pointer = Peel(declared, peeled);
        string? terminated = remaining == 0 && pointer.HasTerminatedCodec ? CharacterFieldTypes.GetStringPointerHandlerKey(pointer.TypeSpelling) : null;
        CompiledField storage = pointer.SelectPointerTarget(remaining, terminated, compilation.PointerSize);
        return this.members.GetOrAdd(key, new WriteProgramCompiler(compilation, this).CompileMember(storage));
    }

    /// <summary>Returns the slots a capture of <paramref name="name"/> is published to under each qualified prefix.</summary>
    /// <param name="name">The bare field name.</param>
    /// <returns>The targets; empty when no expression spells the name with a prefix.</returns>
    public ReadProgram.QualifiedTarget[] GetQualifiedTargets(string name) => this.Table.ReadPrograms.GetQualifiedTargets(name);

    /// <summary>Selects an element view of <paramref name="declared"/> per peeled dimension.</summary>
    /// <param name="declared">The declared member.</param>
    /// <param name="peeled">The number of dimensions to select into.</param>
    /// <returns>The member, or its element or sub-array view.</returns>
    private static CompiledField Peel(CompiledField declared, int peeled)
    {
        CompiledField selected = declared;
        for (int dimension = 0; dimension < peeled; dimension++)
        {
            selected = selected.SelectArrayElement();
        }

        return selected;
    }

    /// <summary>
    ///     What a path selects to write: the declared member, by reference, the dimensions its indexes peel, and the pointer
    ///     levels left after <c>.value</c> accessors (-1 for the member itself).
    /// </summary>
    private readonly struct MemberKey : IEquatable<MemberKey>
    {
        private readonly CompiledField field;
        private readonly int peeled;
        private readonly int level;

        /// <summary>Creates a key.</summary>
        /// <param name="field">The declared member.</param>
        /// <param name="peeled">The peeled dimension count.</param>
        /// <param name="level">The pointer levels left, or -1 for the member itself.</param>
        public MemberKey(CompiledField field, int peeled, int level)
        {
            this.field = field;
            this.peeled = peeled;
            this.level = level;
        }

        /// <summary>Whether two keys name the same member instance with the same peeled dimensions and pointer level.</summary>
        /// <param name="other">The other key.</param>
        /// <returns>Whether they are equal.</returns>
        public bool Equals(MemberKey other) => ReferenceEquals(this.field, other.field) && this.peeled == other.peeled && this.level == other.level;

        /// <summary>Whether <paramref name="obj"/> is an equal key.</summary>
        /// <param name="obj">The object.</param>
        /// <returns>Whether it is an equal key.</returns>
        public override bool Equals(object? obj) => obj is MemberKey other && this.Equals(other);

        /// <summary>A hash of the member's identity, the peeled dimensions and the pointer level.</summary>
        /// <returns>The hash.</returns>
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(this.field), this.peeled, this.level);
    }

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
