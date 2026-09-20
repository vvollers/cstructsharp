#if NETSTANDARD2_0
namespace System.Collections.Generic;

using System.Runtime.CompilerServices;

/// <summary>The .NET 5+ reference-identity comparer, for the netstandard2.0 build of the Core sources.</summary>
internal sealed class ReferenceEqualityComparer : IEqualityComparer<object?>, IEqualityComparer
{
    private ReferenceEqualityComparer()
    {
    }

    /// <summary>The single instance.</summary>
    public static ReferenceEqualityComparer Instance { get; } = new();

    /// <inheritdoc />
    public new bool Equals(object? x, object? y)
    {
        return ReferenceEquals(x, y);
    }

    /// <inheritdoc />
    public int GetHashCode(object? obj)
    {
        return RuntimeHelpers.GetHashCode(obj!);
    }
}
#endif
