namespace CStructSharp;

/// <summary>Represents one name and optional array index in a public layout path.</summary>
internal readonly struct PathSegment
{
    /// <summary>Creates a path segment from a field name and optional zero-based array index.</summary>
    public PathSegment(string name, int? index)
    {
        this.Name = name;
        this.Index = index;
    }

    public string Name { get; }

    public int? Index { get; }
}
