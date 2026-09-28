namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

/// <summary>Represents a named struct or union and the fields it contains.</summary>
internal class Struct : Field
{
    /// <summary>The type name every struct and union declaration carries as a field.</summary>
    public static readonly Identifier STRUCT = new("struct");

    /// <summary>Creates a struct or union definition from its name, fields, and union flag.</summary>
    /// <param name="name">The declared composite name.</param>
    /// <param name="fields">The members in declaration order.</param>
    /// <param name="isUnion"><see langword="true"/> when every member starts at offset 0 (a union).</param>
    /// <param name="compositeAlignmentOverrideExpression">
    ///     The <c>@align(N)</c> expression written before the opening brace, or <see langword="null"/> for none.
    /// </param>
    public Struct(
        Identifier name,
        ImmutableList<Field> fields,
        bool isUnion,
        Expr? compositeAlignmentOverrideExpression = null)
        : base(STRUCT, name, Field.NoArray, NoneExpr.Instance)
    {
        this.Name = name;
        this.Fields = fields;
        this.IsUnion = isUnion;
        this.CompositeAlignmentOverrideExpression = compositeAlignmentOverrideExpression;
    }

    /// <summary>Gets the members in declaration order.</summary>
    public ImmutableList<Field> Fields { get; }

    /// <summary>
    ///     The <c>if</c>/<c>switch</c> groups declared in this body, nested ones included - also those whose arms are all
    ///     empty, so their case labels are still validated. Each conditional member names its groups through
    ///     <see cref="Field.BranchConditions"/>.
    /// </summary>
    public ImmutableArray<ConditionalGroup> Groups { get; init; } = ImmutableArray<ConditionalGroup>.Empty;

    /// <summary>Gets the declared composite name.</summary>
    public override Identifier Name { get; }

    /// <summary>Gets whether every member starts at offset 0 and shares the same storage (a union).</summary>
    public bool IsUnion { get; }

    /// <summary>
    ///     The optional <c>@align(N)</c> expression written before this composite's opening brace, or null when
    ///     none was written. Clamps every one of this composite's own fields' alignment to at most N, unless a field
    ///     carries its own explicit override (which always wins outright).
    /// </summary>
    internal Expr? CompositeAlignmentOverrideExpression { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The element to compare with.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="other"/> is a composite with the same name, union flag, and
    ///     equal members in the same order.
    /// </returns>
    public override bool Equals(CStructElement? other)
    {
        return other is Struct s &&
               this.Name.Equals(s.Name) &&
               this.IsUnion == s.IsUnion &&
               this.Fields.SequenceEqual(s.Fields);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash of the name, union flag, and members.</returns>
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

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The composite name followed by its members.</returns>
    public override string ToString()
    {
        return $"Struct: {this.Name} ({string.Join(", ", this.Fields)})";
    }
}
