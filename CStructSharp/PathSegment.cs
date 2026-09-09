namespace CStructSharp;

using System.Collections.Generic;

/// <summary>Represents one name and zero or more array indexes in a public layout path.</summary>
internal readonly struct PathSegment
{
    /// <summary>
    ///     Creates a path segment from a field name and zero or more zero-based array indexes (LANG-05) - empty
    ///     for "no index," one entry for a 1-D array index exactly as before, N entries for one bracket per
    ///     dimension of a multidimensional array (<c>matrix[2][3]</c>).
    /// </summary>
    public PathSegment(string name, IReadOnlyList<int> indexes)
    {
        this.Name = name;
        this.Indexes = indexes;
    }

    public string Name { get; }

    public IReadOnlyList<int> Indexes { get; }
}
