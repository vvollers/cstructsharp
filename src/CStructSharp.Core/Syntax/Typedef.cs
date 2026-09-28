namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Represents a typedef alias for a primitive type or an inline struct definition.</summary>
internal class Typedef : CStructElement
{
    /// <summary>Creates an alias for an existing type name.</summary>
    /// <param name="name">The alias being declared.</param>
    /// <param name="type">The aliased type spelling, such as <c>uint32</c> or a struct name.</param>
    public Typedef(Identifier name, Identifier type)
    {
        this.Name = name;
        this.Type = type;
    }

    /// <summary>Creates an alias for an inline struct definition.</summary>
    /// <param name="name">The alias being declared.</param>
    /// <param name="strct">The inline struct or union the alias names.</param>
    public Typedef(Identifier name, Struct strct)
    {
        this.Name = name;
        this.Struct = strct;
        this.Type = new Identifier("struct");
    }

    /// <summary>Gets the alias name this typedef declares.</summary>
    public override Identifier Name { get; }

    /// <summary>Gets the inline struct or union definition, or null when the alias names an existing type.</summary>
    public Struct? Struct { get; }

    /// <summary>Gets the aliased type spelling; <c>struct</c> for an inline definition.</summary>
    public Identifier Type { get; }

    /// <summary>Fixed dimensions of a <c>typedef T name[N];</c> alias (outermost first); <see cref="Field.NoArray"/> otherwise.</summary>
    public IReadOnlyList<Expr> ArrayShape { get; init; } = Field.NoArray;

    /// <summary>The <c>struct</c>/<c>union</c> keyword of a <c>typedef struct tag alias;</c>, checked against the tag's kind.</summary>
    public string? TypeKeywordHint { get; init; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The element to compare with this typedef.</param>
    /// <returns>True when both are typedefs with equal names, types, array shapes, and inline definitions.</returns>
    public override bool Equals(CStructElement? other)
    {
        return other is Typedef t &&
               this.Name.Equals(t.Name) &&
               this.Type.Equals(t.Type) &&
               this.ArrayShape.SequenceEqual(t.ArrayShape) &&
               (this.Struct is null ? t.Struct is null : this.Struct.Equals(t.Struct));
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash combining the name, aliased type, and inline definition.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Type, this.Struct);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The alias name, aliased type, and inline definition as text.</returns>
    public override string ToString()
    {
        return $"Typedef: {this.Name} ({this.Type}) : {this.Struct}";
    }
}
