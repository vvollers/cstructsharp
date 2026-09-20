namespace CStructSharp.Compilation;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
///     Builds one lookup table during layout compilation, then irreversibly publishes a frozen snapshot and releases
///     the mutable builder. An optional shared baseline (a process-wide table) sits under the layout's own entries,
///     so a layout never copies the baseline: its entries shadow the baseline's on lookup and enumeration.
/// </summary>
internal sealed class ConstructionDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> storage;
    private readonly IReadOnlyDictionary<TKey, TValue> baseline;
    private bool frozen;

    /// <summary>Creates an empty construction table with the requested key comparer and an optional shared baseline.</summary>
    public ConstructionDictionary(IEqualityComparer<TKey>? comparer = null, IReadOnlyDictionary<TKey, TValue>? baseline = null)
    {
        this.storage = new Dictionary<TKey, TValue>(comparer);
        this.baseline = baseline ?? EmptyBaseline.Instance;
    }

    public int Count => this.baseline.Count == 0 ? this.storage.Count : this.storage.Count + this.baseline.Count(pair => !this.storage.ContainsKey(pair.Key));

    /// <summary>Gets whether the mutable builder has been discarded and the snapshot published.</summary>
    public bool IsFrozen => this.frozen;

    public IEnumerable<TKey> Keys => this.Select(pair => pair.Key);

    /// <summary>
    ///     The frozen, read-only view: the same dictionary, read-only once the builder handle is withdrawn, which
    ///     Gives the caller immutability without copying into a <c>FrozenDictionary</c> (a copy costs about a
    ///     Quarter of a small layout's compile time).
    /// </summary>
    public IReadOnlyDictionary<TKey, TValue> Snapshot =>
        this.frozen ? this : throw new InvalidOperationException("The construction dictionary has not been frozen.");

    public IEnumerable<TValue> Values => this.Select(pair => pair.Value);

    /// <summary>The collection view is read-only for every consumer; only the construction-phase methods mutate.</summary>
    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;

    public TValue this[TKey key]
    {
        get => this.TryGetValue(key, out TValue? value) ? value : throw new KeyNotFoundException($"The key '{key}' was not present in the dictionary.");
        set => this.GetBuilder()[key] = value;
    }

    /// <summary>Adds one construction-time entry.</summary>
    public void Add(TKey key, TValue value)
    {
        this.GetBuilder().Add(key, value);
    }

    public bool ContainsKey(TKey key)
    {
        return this.storage.ContainsKey(key) || this.baseline.ContainsKey(key);
    }

    /// <summary>Irreversibly converts the builder to the read-optimized immutable representation.</summary>
    public void Freeze()
    {
        _ = this.GetBuilder();
        this.frozen = true;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (KeyValuePair<TKey, TValue> pair in this.storage)
        {
            yield return pair;
        }

        foreach (KeyValuePair<TKey, TValue> pair in this.baseline)
        {
            if (!this.storage.ContainsKey(pair.Key))
            {
                yield return pair;
            }
        }
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
        return this.storage.TryGetValue(key, out value!) || this.baseline.TryGetValue(key, out value!);
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
        return this.TryGetValue(item.Key, out TValue? value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
    }

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        foreach (KeyValuePair<TKey, TValue> pair in this)
        {
            array[arrayIndex++] = pair;
        }
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

    private static class EmptyBaseline
    {
        public static readonly IReadOnlyDictionary<TKey, TValue> Instance = new Dictionary<TKey, TValue>();
    }
}
