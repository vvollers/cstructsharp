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
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
internal sealed class ConstructionDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> storage;
    private readonly IReadOnlyDictionary<TKey, TValue> baseline;
    private bool frozen;

    /// <summary>Creates an empty construction table with the requested key comparer and an optional shared baseline.</summary>
    /// <param name="comparer">The key comparer for the layout's entries, or <see langword="null"/>.</param>
    /// <param name="baseline">
    ///     The shared table beneath the layout's entries, which is never copied or changed, or
    ///     <see langword="null"/> for none.
    /// </param>
    public ConstructionDictionary(IEqualityComparer<TKey>? comparer = null, IReadOnlyDictionary<TKey, TValue>? baseline = null)
    {
        this.storage = new Dictionary<TKey, TValue>(comparer);
        this.baseline = baseline ?? EmptyBaseline.Instance;
    }

    /// <summary>Gets the number of visible entries: the layout's own plus the unshadowed baseline ones.</summary>
    public int Count => this.baseline.Count == 0 ? this.storage.Count : this.storage.Count + this.baseline.Count(pair => !this.storage.ContainsKey(pair.Key));

    /// <summary>Gets whether the mutable builder has been discarded and the snapshot published.</summary>
    public bool IsFrozen => this.frozen;

    /// <summary>Gets the keys of the visible entries, in enumeration order.</summary>
    public IEnumerable<TKey> Keys => this.Select(pair => pair.Key);

    /// <summary>
    ///     The frozen, read-only view: the same dictionary, read-only once the builder handle is withdrawn, which
    ///     Gives the caller immutability without copying into a <c>FrozenDictionary</c> (a copy costs about a
    ///     Quarter of a small layout's compile time).
    /// </summary>
    public IReadOnlyDictionary<TKey, TValue> Snapshot =>
        this.frozen ? this : throw new InvalidOperationException("The construction dictionary has not been frozen.");

    /// <summary>Gets the values of the visible entries, in enumeration order.</summary>
    public IEnumerable<TValue> Values => this.Select(pair => pair.Value);

    /// <summary>The collection view is read-only for every consumer; only the construction-phase methods mutate.</summary>
    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;

    /// <summary>
    ///     Gets or sets the value for a key: a get searches the layout's entries, then the baseline; a set assigns a
    ///     layout entry while the dictionary is still being built.
    /// </summary>
    /// <param name="key">The key to look up or assign.</param>
    /// <exception cref="KeyNotFoundException">On get, neither table contains <paramref name="key"/>.</exception>
    /// <exception cref="InvalidOperationException">On set, the dictionary is frozen.</exception>
    public TValue this[TKey key]
    {
        get => this.TryGetValue(key, out TValue? value) ? value : throw new KeyNotFoundException($"The key '{key}' was not present in the dictionary.");
        set => this.GetBuilder()[key] = value;
    }

    /// <summary>Adds one construction-time entry.</summary>
    /// <param name="key">The entry's key; it may shadow a baseline key.</param>
    /// <param name="value">The entry's value.</param>
    /// <exception cref="InvalidOperationException">The dictionary is frozen.</exception>
    /// <exception cref="ArgumentException">The layout's own entries already contain <paramref name="key"/>.</exception>
    public void Add(TKey key, TValue value)
    {
        this.GetBuilder().Add(key, value);
    }

    /// <summary>Tests whether the key is in the layout's own entries or in the baseline.</summary>
    /// <param name="key">The key to find.</param>
    /// <returns><see langword="true"/> when either table contains <paramref name="key"/>.</returns>
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

    /// <summary>Enumerates the layout's own entries, then the baseline entries whose keys they do not shadow.</summary>
    /// <returns>An enumerator over the visible entries.</returns>
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
    /// <param name="values">The new entries; the baseline is unaffected and still sits beneath them.</param>
    /// <exception cref="InvalidOperationException">The dictionary is frozen.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> contains a duplicate key.</exception>
    public void ReplaceWith(IEnumerable<KeyValuePair<TKey, TValue>> values)
    {
        Dictionary<TKey, TValue> current = this.GetBuilder();
        current.Clear();
        foreach (KeyValuePair<TKey, TValue> value in values)
        {
            current.Add(value.Key, value.Value);
        }
    }

    /// <summary>Looks up a key in the layout's own entries first, then in the baseline.</summary>
    /// <param name="key">The key to find.</param>
    /// <param name="value">Receives the found value, or the default value when the key is absent.</param>
    /// <returns><see langword="true"/> when either table contains <paramref name="key"/>.</returns>
    public bool TryGetValue(TKey key, out TValue value)
    {
        return this.storage.TryGetValue(key, out value!) || this.baseline.TryGetValue(key, out value!);
    }

    /// <summary>Enumerates the same entries as the generic enumerator.</summary>
    /// <returns>A non-generic enumerator over the visible entries.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    /// <summary>Rejects adding through the collection view; see <see cref="Add(TKey, TValue)"/>.</summary>
    /// <param name="item">The ignored entry.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
    {
        throw new NotSupportedException("The construction dictionary is read-only through its collection view.");
    }

    /// <summary>Rejects clearing through the collection view.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    void ICollection<KeyValuePair<TKey, TValue>>.Clear()
    {
        throw new NotSupportedException("The construction dictionary is read-only through its collection view.");
    }

    /// <summary>Tests whether a visible entry has the item's key and an equal value.</summary>
    /// <param name="item">The key and value to find.</param>
    /// <returns><see langword="true"/> when the key is visible and its value equals the item's value.</returns>
    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
    {
        return this.TryGetValue(item.Key, out TValue? value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
    }

    /// <summary>Copies the visible entries, in enumeration order, into an array.</summary>
    /// <param name="array">The destination array, which must have room for <see cref="Count"/> entries.</param>
    /// <param name="arrayIndex">The index in <paramref name="array"/> that receives the first entry.</param>
    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        foreach (KeyValuePair<TKey, TValue> pair in this)
        {
            array[arrayIndex++] = pair;
        }
    }

    /// <summary>Rejects removing through the collection view.</summary>
    /// <param name="item">The ignored entry.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
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
