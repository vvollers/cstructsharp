namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
///     Builds one lookup table during layout compilation, then irreversibly publishes a frozen snapshot and releases
///     the mutable builder.
/// </summary>
internal sealed class ConstructionDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> storage;
    private bool frozen;

    /// <summary>Creates an empty construction table with the requested key comparer.</summary>
    public ConstructionDictionary(IEqualityComparer<TKey>? comparer = null)
    {
        this.storage = new Dictionary<TKey, TValue>(comparer);
    }

    public int Count => this.Current.Count;

    /// <summary>Gets whether the mutable builder has been discarded and the snapshot published.</summary>
    public bool IsFrozen => this.frozen;

    public IEnumerable<TKey> Keys => this.Current.Keys;

    /// <summary>Gets the immutable snapshot after <see cref="Freeze" /> has completed.</summary>
    /// <summary>
    ///     The frozen, read-only view. Freezing no longer copies into a <c>FrozenDictionary</c>: that construction
    ///     was a quarter of a small layout's compile time (E1.3a), while the same dictionary used read-only after
    ///     the builder handle is withdrawn gives identical immutability for the caller.
    /// </summary>
    public IReadOnlyDictionary<TKey, TValue> Snapshot =>
        this.frozen ? this.storage : throw new InvalidOperationException("The construction dictionary has not been frozen.");

    public IEnumerable<TValue> Values => this.Current.Values;

    /// <summary>The collection view is read-only for every consumer; only the construction-phase methods mutate.</summary>
    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;

    private Dictionary<TKey, TValue> Current => this.storage;

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
        _ = this.GetBuilder();
        this.frozen = true;
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

    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
    {
        throw new NotSupportedException("The construction dictionary is read-only through its collection view.");
    }

    void ICollection<KeyValuePair<TKey, TValue>>.Clear()
    {
        throw new NotSupportedException("The construction dictionary is read-only through its collection view.");
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)this.storage).Contains(item);
    }

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<TKey, TValue>>)this.storage).CopyTo(array, arrayIndex);
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
    {
        throw new NotSupportedException("The construction dictionary is read-only through its collection view.");
    }

    private Dictionary<TKey, TValue> GetBuilder()
    {
        return this.frozen
                   ? throw new InvalidOperationException("The construction dictionary is already frozen.")
                   : this.storage;
    }
}
