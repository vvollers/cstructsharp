#if NETSTANDARD2_0
namespace System.Collections.Generic;

/// <summary>
///     The .NET 5+ read-only set interface, for the netstandard2.0 build of the Core sources (the source generator).
///     The runtime library uses the BCL interface; only the shape the parser needs (membership) is polyfilled.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public interface IReadOnlySet<T> : IReadOnlyCollection<T>
{
    /// <summary>Whether the set contains <paramref name="item"/>.</summary>
    bool Contains(T item);
}

/// <summary>A read-only set over a <see cref="HashSet{T}"/>, for the generator's own option values.</summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ReadOnlySetAdapter<T> : IReadOnlySet<T>
{
    private readonly HashSet<T> items;

    /// <summary>Wraps <paramref name="items"/> without copying.</summary>
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
