namespace CStructSharp.Structure;

using System;

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

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(CStructElement? other)
    {
        return other is Typedef t &&
               this.Name.Equals(t.Name) &&
               this.Type.Equals(t.Type) &&
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
