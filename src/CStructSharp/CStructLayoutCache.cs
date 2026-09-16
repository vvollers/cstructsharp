namespace CStructSharp;

using System;
using System.Collections.Generic;

/// <summary>
///     Process-wide, bounded, most-recently-used cache of compiled layouts keyed by every constructor input. A hit
///     returns the same immutable <see cref="CStruct"/> instance, which is safe because a compiled layout carries no
///     per-operation state. Compilation runs outside the lock, so two threads may compile the same source
///     concurrently; the first result stored wins and the other is discarded.
/// </summary>
internal sealed class CStructLayoutCache
{
    private const int DefaultCapacity = 64;
    private const long DefaultMaximumSourceBytes = 8L * 1024 * 1024;
    private static readonly CStructCompilationOptions DefaultOptions = new();

    private readonly object gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> entries = new();
    private readonly LinkedList<Entry> recency = new();
    private readonly int capacity;
    private readonly long maximumSourceChars;
    private long retainedSourceChars;

    public CStructLayoutCache(int capacity = DefaultCapacity, long maximumSourceChars = DefaultMaximumSourceBytes)
    {
        this.capacity = capacity;
        this.maximumSourceChars = maximumSourceChars;
    }

    public static CStructLayoutCache Shared { get; } = new();

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
            options.BitfieldAllocation);

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
        BitfieldAllocation BitfieldAllocation);

    private sealed record Entry(Key Key, CStruct Layout);
}
