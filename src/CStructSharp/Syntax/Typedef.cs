namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Represents a typedef alias for a primitive type or an inline struct definition.</summary>
internal class Typedef : CStructElement
{
    /// <summary>Creates an alias for an existing type name.</summary>
    public Typedef(Identifier name, Identifier type)
    {
        this.Name = name;
        this.Type = type;
    }

    /// <summary>Creates an alias for an inline struct definition.</summary>
    public Typedef(Identifier name, Struct strct)
    {
        this.Name = name;
        this.Struct = strct;
        this.Type = new Identifier("struct");
    }

    public override Identifier Name { get; }

    public Struct? Struct { get; }

    public Identifier Type { get; }

    /// <summary>Fixed dimensions of a <c>typedef T name[N];</c> alias (outermost first); <see cref="Field.NoArray"/> otherwise.</summary>
    public IReadOnlyList<Expr> ArrayShape { get; init; } = Field.NoArray;

    /// <summary>The <c>struct</c>/<c>union</c> keyword of a <c>typedef struct tag alias;</c>, checked against the tag's kind.</summary>
    public string? TypeKeywordHint { get; init; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(CStructElement? other)
    {
        return other is Typedef t &&
               this.Name.Equals(t.Name) &&
               this.Type.Equals(t.Type) &&
               this.ArrayShape.SequenceEqual(t.ArrayShape) &&
               (this.Struct is null ? t.Struct is null : this.Struct.Equals(t.Struct));
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Type, this.Struct);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"Typedef: {this.Name} ({this.Type}) : {this.Struct}";
    }
}
