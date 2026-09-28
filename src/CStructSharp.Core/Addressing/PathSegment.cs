namespace CStructSharp.Addressing;

using System.Collections.Generic;

/// <summary>Represents one name and zero or more array indexes in a public layout path.</summary>
internal readonly struct PathSegment
{
    /// <summary>
    ///     Creates a path segment from a field name and zero or more zero-based array indexes - empty
    ///     for "no index," one entry for a one-dimensional array index, N entries for one bracket per
    ///     dimension of a multidimensional array (<c>matrix[2][3]</c>).
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <param name="indexes">The zero-based element indexes, outermost dimension first.</param>
    public PathSegment(string name, IReadOnlyList<int> indexes)
    {
        this.Name = name;
        this.Indexes = indexes;
    }

    /// <summary>Gets the field name.</summary>
    public string Name { get; }

    /// <summary>Gets the zero-based element indexes, outermost dimension first; empty for no index.</summary>
    public IReadOnlyList<int> Indexes { get; }
}
