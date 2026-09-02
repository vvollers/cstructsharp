namespace CStructSharp.Structure;

using System;

/// <summary>Base class for each named item that can appear in a layout: a field, struct, enum, typedef, or define.</summary>
internal abstract class CStructElement : IEquatable<CStructElement>
{
    public abstract Identifier Name { get; }

    /// <summary>Checks whether another layout item has the same definition.</summary>
    public abstract bool Equals(CStructElement? other);

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as CStructElement);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public abstract override int GetHashCode();
}
