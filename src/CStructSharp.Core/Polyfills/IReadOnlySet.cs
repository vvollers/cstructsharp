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
    /// <param name="item">The element to look up.</param>
    /// <returns><see langword="true"/> when the element is a member of the set.</returns>
    bool Contains(T item);
}
#endif
