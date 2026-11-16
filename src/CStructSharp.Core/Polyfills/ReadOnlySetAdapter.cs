#if NETSTANDARD2_0
namespace System.Collections.Generic;

/// <summary>A read-only set over a <see cref="HashSet{T}"/>, for the generator's own option values.</summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ReadOnlySetAdapter<T> : IReadOnlySet<T>
{
    private readonly HashSet<T> items;

    /// <summary>Wraps <paramref name="items"/> without copying.</summary>
    /// <param name="items">The set to expose; later changes to it are visible through the adapter.</param>
    public ReadOnlySetAdapter(HashSet<T> items)
    {
        this.items = items;
    }

    /// <inheritdoc />
    public int Count => this.items.Count;

    /// <inheritdoc />
    public bool Contains(T item)
    {
        return this.items.Contains(item);
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator()
    {
        return this.items.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.items.GetEnumerator();
    }
}
#endif
