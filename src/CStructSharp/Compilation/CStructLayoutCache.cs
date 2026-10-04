namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;

/// <summary>
///     Bounded, most-recently-used cache of compiled layouts keyed by every constructor input. A hit
///     returns the same immutable <see cref="CStruct"/> instance, which is safe because a compiled layout carries no
///     per-operation state. Compilation runs outside the lock, so two threads may compile the same source
///     concurrently; the first result stored wins and the other is discarded.
/// </summary>
internal sealed class CStructLayoutCache
{
    private const int DefaultCapacity = 64;
    private const long DefaultMaximumSourceChars = 8L * 1024 * 1024;
    private static readonly CStructCompilationOptions DefaultOptions = new();

    private readonly object gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> entries = new();
    private readonly LinkedList<Entry> recency = new();
    private readonly int capacity;
    private readonly long maximumSourceChars;
    private long retainedSourceChars;

    /// <summary>Creates an empty cache bounded by entry count and by the total length of the retained layout sources.</summary>
    /// <param name="capacity">The most compiled layouts kept.</param>
    /// <param name="maximumSourceChars">The most source characters (UTF-16 code units) kept across all entries; a longer layout is never cached.</param>
    public CStructLayoutCache(int capacity = DefaultCapacity, long maximumSourceChars = DefaultMaximumSourceChars)
    {
        this.capacity = capacity;
        this.maximumSourceChars = maximumSourceChars;
    }

    /// <summary>
    ///     Gets the process-wide cache behind <see cref="CStruct.GetOrCompile"/>, with the default bounds.
    /// </summary>
    public static CStructLayoutCache Shared { get; } = new();

    /// <summary>Gets the number of compiled layouts currently retained.</summary>
    public int Count
    {
        get
        {
            lock (this.gate)
            {
                return this.entries.Count;
            }
        }
    }

    /// <summary>
    ///     Returns the cached layout for these exact inputs, or compiles one and retains it, evicting the least
    ///     recently used entries until both bounds hold again. A failed compilation is not cached.
    /// </summary>
    /// <param name="layout">The layout source text; every character is part of the cache key.</param>
    /// <param name="pointerSize">The binary format's pointer width in bytes.</param>
    /// <param name="aligned">Whether the portable composite-alignment rules apply.</param>
    /// <param name="isLittleEndian">Whether neutral values are little-endian.</param>
    /// <param name="compilationOptions">
    ///     Optional compilation limits and settings, or <see langword="null"/> for the defaults; each setting is part
    ///     of the cache key, and custom codecs match by list reference.
    /// </param>
    /// <returns>
    ///     The shared compiled layout, identical to one the constructor would build from the same inputs.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    public CStruct GetOrCompile(
        string layout,
        byte pointerSize,
        bool aligned,
        bool isLittleEndian,
        CStructCompilationOptions? compilationOptions)
    {
        ArgumentNullException.ThrowIfNull(layout);

        // The defaults are init-only and never mutated, so a hit with null options allocates nothing.
        CStructCompilationOptions options = compilationOptions ?? DefaultOptions;
        var key = new Key(
            layout,
            pointerSize,
            aligned,
            isLittleEndian,
            options.MaxDefinitionLength,
            options.MaxLayoutNestingDepth,
            options.MaxExpressionNestingDepth,
            options.MaxExpressionTokens,
            options.CLongWidth,
            options.DefaultEnumStorage,
            options.Defined is null or { Count: 0 } ? string.Empty : string.Join('\0', options.Defined.Order(StringComparer.Ordinal)),
            options.Codecs is null or { Count: 0 } ? null : options.Codecs,
            options.Prelude,
            options.BitfieldAllocation,
            options.BitfieldPacking);

        lock (this.gate)
        {
            if (this.entries.TryGetValue(key, out LinkedListNode<Entry>? hit))
            {
                this.recency.Remove(hit);
                this.recency.AddFirst(hit);
                return hit.Value.Layout;
            }
        }

        // Compile outside the lock: a slow or failing compilation must not block unrelated lookups, and a failing
        // one is never cached (the exception propagates exactly as from the constructor).
        var compiled = new CStruct(layout, pointerSize, aligned, isLittleEndian, options);

        lock (this.gate)
        {
            if (this.entries.TryGetValue(key, out LinkedListNode<Entry>? raced))
            {
                return raced.Value.Layout;
            }

            if (layout.Length > this.maximumSourceChars)
            {
                // Oversized sources are compiled but never retained; they would evict the whole working set.
                return compiled;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, compiled));
            this.recency.AddFirst(node);
            this.entries.Add(key, node);
            this.retainedSourceChars += layout.Length;
            while (this.entries.Count > this.capacity || this.retainedSourceChars > this.maximumSourceChars)
            {
                LinkedListNode<Entry> oldest = this.recency.Last!;
                this.recency.RemoveLast();
                this.entries.Remove(oldest.Value.Key);
                this.retainedSourceChars -= oldest.Value.Key.Layout.Length;
            }

            return compiled;
        }
    }

    /// <summary>Removes every retained layout; instances already returned stay valid.</summary>
    public void Clear()
    {
        lock (this.gate)
        {
            this.entries.Clear();
            this.recency.Clear();
            this.retainedSourceChars = 0;
        }
    }

    private readonly record struct Key(
        string Layout,
        byte PointerSize,
        bool Aligned,
        bool IsLittleEndian,
        int MaxDefinitionLength,
        int MaxLayoutNestingDepth,
        int MaxExpressionNestingDepth,
        int MaxExpressionTokens,
        int CLongWidth,
        string? DefaultEnumStorage,
        string Defined,
        object? Codecs,
        string? Prelude,
        BitfieldAllocation BitfieldAllocation,
        BitfieldPacking BitfieldPacking);

    private sealed record Entry(Key Key, CStruct Layout);
}
