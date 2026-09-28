namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Represents a name in a layout expression or declaration and records any pointer stars written with it.</summary>
internal class Identifier : Expr
{
    /// <summary>The storage type of an enum with a negative member and no declared storage.</summary>
    public static readonly Identifier INT32 = new("int32");

    /// <summary>The storage type of an enum with no negative member and no declared storage.</summary>
    public static readonly Identifier UINT32 = new("uint32");

    /// <summary>Creates an identifier, removing pointer stars from its name while remembering their count.</summary>
    /// <param name="name">The spelling as written, which may include <c>*</c> characters.</param>
    public Identifier(string name)
    {
        // Most names carry no stars; skip the scan-and-copy for them (the parser constructs one per word).
        if (!name.Contains('*'))
        {
            this.Name = name;
            return;
        }

        this.PointerDepth = name.Count(c => c == '*');
        this.Name = name.Replace("*", string.Empty);
    }

    /// <summary>Whether the spelling carried one or more <c>*</c>. Computed so the node stays within 32 bytes with its source offset.</summary>
    public bool IsPointer => this.PointerDepth > 0;

    /// <summary>
    ///     The zero-based offset of this name in the layout source, or -1 when the identifier was synthesized
    ///     rather than parsed. Equality and hashing ignore it: two spellings of the same name are the same name.
    /// </summary>
    public int SourceOffset { get; init; } = -1;

    /// <summary>Gets the name without any <c>*</c> characters.</summary>
    public string Name { get; }

    /// <summary>Gets the number of <c>*</c> characters the spelling carried; 0 for a non-pointer name.</summary>
    public int PointerDepth { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="other"/> is an identifier with the same name and pointer
    ///     depth; the source offset is ignored.
    /// </returns>
    public override bool Equals(Expr? other)
    {
        return other is Identifier i &&
               this.Name == i.Name &&
               this.PointerDepth == i.PointerDepth;
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>An ordinal hash of the name combined with the pointer depth.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(StringComparer.Ordinal.GetHashCode(this.Name), this.PointerDepth);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The name in square brackets.</returns>
    public override string ToString()
    {
        return $"[{this.Name}]";
    }
}
