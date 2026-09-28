namespace CStructSharp.Syntax;

using System;

/// <summary>Base class for each named item that can appear in a layout: a field, struct, enum, typedef, or define.</summary>
internal abstract class CStructElement : IEquatable<CStructElement>
{
    /// <summary>Gets the name the item is declared under and looked up by.</summary>
    public abstract Identifier Name { get; }

    /// <summary>Checks whether another layout item has the same definition.</summary>
    /// <param name="other">The item to compare with, or <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="other"/> is the same kind of item with an equal definition.
    /// </returns>
    public abstract bool Equals(CStructElement? other);

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="obj">The object to compare with; anything other than a layout item is unequal.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="obj"/> is a layout item with an equal definition.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as CStructElement);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash code that is equal for items with equal definitions.</returns>
    public abstract override int GetHashCode();
}
