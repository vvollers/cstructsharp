namespace CStructSharp.Structure;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

/// <summary>Represents a named struct or union and the fields it contains.</summary>
internal class Struct : Field
{
    public static readonly Identifier STRUCT = new("struct");

    /// <summary>Creates a struct or union definition from its name, fields, and union flag.</summary>
    public Struct(
        Identifier name,
        ImmutableList<Field> fields,
        bool isUnion,
        Expr? compositeAlignmentOverrideExpression = null)
        : base(STRUCT, name, NoneExpr.Instance, 0)
    {
        this.Name = name;
        this.Fields = fields;
        this.IsUnion = isUnion;
        this.CompositeAlignmentOverrideExpression = compositeAlignmentOverrideExpression;
    }

    public ImmutableList<Field> Fields { get; }

    public override Identifier Name { get; }

    public bool IsUnion { get; }

    /// <summary>
    ///     The optional <c>@align(N)</c> expression written before this composite's opening brace, or null when
    ///     none was written. Clamps every one of this composite's own fields' alignment to at most N, unless a field
    ///     carries its own explicit override (which always wins outright).
    /// </summary>
    internal Expr? CompositeAlignmentOverrideExpression { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(CStructElement? other)
    {
        return other is Struct s &&
               this.Name.Equals(s.Name) &&
               this.IsUnion == s.IsUnion &&
               this.Fields.SequenceEqual(s.Fields);
    }

    /// <summary>Returns the previously calculated alignment for this named struct.</summary>
    public override T GetAlignment<T>(IReadOnlyDictionary<string, T> alignments, T pointerSize)
    {
        return alignments[this.Name.Name];
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(this.Name);
        hash.Add(this.IsUnion);
        foreach (Field field in this.Fields)
        {
            hash.Add(field);
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns whether this struct already has a calculated entry in the supplied lookup.</summary>
    public override bool IsKnown<T>(IReadOnlyDictionary<string, T> dict)
    {
        return dict.ContainsKey(this.Name.Name);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"Struct: {this.Name} ({string.Join(", ", this.Fields)})";
    }
}
