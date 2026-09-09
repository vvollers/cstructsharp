namespace CStructSharp.Structure;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Describes one field in a struct or union, including its type, array length, bit width, and pointer depth.</summary>
internal class Field : CStructElement
{
    private readonly int? bitSize;

    /// <summary>The sentinel "not an array" value - an empty dimension list.</summary>
    public static readonly IReadOnlyList<Expr> NoArray = Array.Empty<Expr>();

    /// <summary>
    ///     The sentinel unsized-character-array count expression (<c>char name[];</c>) - valid only as the sole
    ///     entry of a one-dimensional <see cref="ArrayCount"/> list (LANG-05 decision 6: an unsized dimension can
    ///     never appear as one of several dimensions of a multidimensional array).
    /// </summary>
    public static readonly Expr UnknownArraysize = new Literal(int.MinValue);

    /// <summary>Creates a field definition and derives pointer depth from the type and field name when it is not supplied.</summary>
    public Field(
        Identifier type,
        Identifier name,
        IReadOnlyList<Expr> arraycount,
        int bitSize,
        int pointerDepth = -1,
        string? typeKeywordHint = null,
        Expr? alignmentOverrideExpression = null,
        Expr? offsetAssertionExpression = null)
    {
        this.Type = type;
        this.Name = name;
        this.ArrayCount = arraycount;
        this.bitSize = bitSize;
        this.BitSizeExpression = bitSize == 0 ? NoneExpr.Instance : new Literal(bitSize);
        int derivedPointerDepth = type.PointerDepth + name.PointerDepth;
        this.PointerDepth = pointerDepth >= 0 ? pointerDepth : derivedPointerDepth;
        this.IsPointer = this.PointerDepth > 0;
        this.TypeKeywordHint = typeKeywordHint;
        this.AlignmentOverrideExpression = alignmentOverrideExpression;
        this.OffsetAssertionExpression = offsetAssertionExpression;
    }

    /// <summary>Creates a parsed field whose bit width will be evaluated with the compiled layout's expression policy.</summary>
    internal Field(
        Identifier type,
        Identifier name,
        IReadOnlyList<Expr> arraycount,
        Expr bitSize,
        int pointerDepth = -1,
        string? typeKeywordHint = null,
        Expr? alignmentOverrideExpression = null,
        Expr? offsetAssertionExpression = null)
    {
        this.Type = type;
        this.Name = name;
        this.ArrayCount = arraycount;
        this.BitSizeExpression = bitSize;
        int derivedPointerDepth = type.PointerDepth + name.PointerDepth;
        this.PointerDepth = pointerDepth >= 0 ? pointerDepth : derivedPointerDepth;
        this.IsPointer = this.PointerDepth > 0;
        this.TypeKeywordHint = typeKeywordHint;
        this.AlignmentOverrideExpression = alignmentOverrideExpression;
        this.OffsetAssertionExpression = offsetAssertionExpression;
    }

    /// <summary>
    ///     Every array dimension's own count expression, outermost first - empty for a scalar field
    ///     (<see cref="NoArray"/>), one entry for every array this codebase supported before LANG-05, N entries
    ///     for a multidimensional (LANG-05) field.
    /// </summary>
    public IReadOnlyList<Expr> ArrayCount { get; }

    public int BitSize
    {
        get
        {
            if (this.bitSize.HasValue)
            {
                return this.bitSize.Value;
            }

            if (ReferenceEquals(this.BitSizeExpression, NoneExpr.Instance))
            {
                return 0;
            }

            int value = global::CStructSharp.ExpressionEvaluator.Default.Evaluate(this.BitSizeExpression);
            if (value <= 0)
            {
                throw new InvalidOperationException("Bitfield width must be greater than zero.");
            }

            return value;
        }
    }

    internal Expr BitSizeExpression { get; }

    public bool IsPointer { get; }

    public int PointerDepth { get; }

    public override Identifier Name { get; }

    public Identifier Type { get; }

    /// <summary>The optional struct/union/enum keyword written before the type reference, or null when none was written.</summary>
    public string? TypeKeywordHint { get; }

    /// <summary>The optional <c>@align(N)</c> expression written after this declarator, or null when none was written.</summary>
    internal Expr? AlignmentOverrideExpression { get; }

    /// <summary>The optional <c>@N</c> offset assertion written after this declarator, or null when none was written.</summary>
    internal Expr? OffsetAssertionExpression { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(CStructElement? other)
    {
        return other is Field f &&
               this.Type.Equals(f.Type) &&
               this.Name.Equals(f.Name) &&
               this.ArrayCount.SequenceEqual(f.ArrayCount) &&
               this.BitSizeExpression.Equals(f.BitSizeExpression) &&
               this.PointerDepth == f.PointerDepth;
    }

    /// <summary>Returns the primitive alignment for this field, using pointer size for pointer fields.</summary>
    public virtual T GetAlignment<T>(IReadOnlyDictionary<string, T> alignments, T pointerSize)
    {
        if (this.IsPointer)
        {
            return pointerSize;
        }

        return alignments[this.Type.Name];
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        var arrayCountHash = default(HashCode);
        foreach (Expr dimension in this.ArrayCount)
        {
            arrayCountHash.Add(dimension);
        }

        return HashCode.Combine(this.Type, this.Name, arrayCountHash.ToHashCode(), this.BitSizeExpression, this.PointerDepth);
    }

    /// <summary>Returns whether the field's type is known to the supplied lookup.</summary>
    public virtual bool IsKnown<T>(IReadOnlyDictionary<string, T> dict)
    {
        return this.IsPointer || dict.ContainsKey(this.Type.Name);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"{this.Name} ({this.Type}) [{string.Join("][", this.ArrayCount)}]";
    }
}
