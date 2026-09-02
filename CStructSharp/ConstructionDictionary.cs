namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
///     Builds one lookup table during layout compilation, then irreversibly publishes a frozen snapshot and releases
///     the mutable builder.
/// </summary>
internal sealed class ConstructionDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private Dictionary<TKey, TValue>? builder;
    private FrozenDictionary<TKey, TValue>? frozen;

    /// <summary>Creates an empty construction table with the requested key comparer.</summary>
    public ConstructionDictionary(IEqualityComparer<TKey>? comparer = null)
    {
        this.builder = new Dictionary<TKey, TValue>(comparer);
    }

    public int Count => this.Current.Count;

    /// <summary>Gets whether the mutable builder has been discarded and the snapshot published.</summary>
    public bool IsFrozen => this.frozen is not null;

    public IEnumerable<TKey> Keys => this.Current.Keys;

    /// <summary>Gets the immutable snapshot after <see cref="Freeze" /> has completed.</summary>
    public FrozenDictionary<TKey, TValue> Snapshot =>
        this.frozen ?? throw new InvalidOperationException("The construction dictionary has not been frozen.");

    public IEnumerable<TValue> Values => this.Current.Values;

    private IReadOnlyDictionary<TKey, TValue> Current =>
        (IReadOnlyDictionary<TKey, TValue>?)this.frozen ??
        this.builder ??
        throw new InvalidOperationException("The construction dictionary has no active storage.");

    public TValue this[TKey key]
    {
        get => this.Current[key];
        set => this.GetBuilder()[key] = value;
    }

    /// <summary>Adds one construction-time entry.</summary>
    public void Add(TKey key, TValue value)
    {
        this.GetBuilder().Add(key, value);
    }

    public bool ContainsKey(TKey key)
    {
        return this.Current.ContainsKey(key);
    }

    /// <summary>Irreversibly converts the builder to the read-optimized immutable representation.</summary>
    public void Freeze()
    {
        Dictionary<TKey, TValue> current = this.GetBuilder();
        this.frozen = current.ToFrozenDictionary(current.Comparer);
        this.builder = null;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return this.Current.GetEnumerator();
    }

    /// <summary>Replaces all construction-time entries while retaining the configured key comparer.</summary>
    public void ReplaceWith(IEnumerable<KeyValuePair<TKey, TValue>> values)
    {
        Dictionary<TKey, TValue> current = this.GetBuilder();
        current.Clear();
        foreach (KeyValuePair<TKey, TValue> value in values)
        {
            current.Add(value.Key, value.Value);
        }
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        return this.Current.TryGetValue(key, out value!);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    private Dictionary<TKey, TValue> GetBuilder()
    {
        return this.builder ??
               throw new InvalidOperationException("The construction dictionary is already frozen.");
    }
}
