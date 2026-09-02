namespace CStructSharp.Structure;

using System;
using System.Collections.Generic;

/// <summary>Describes one field in a struct or union, including its type, array length, bit width, and pointer depth.</summary>
internal class Field : CStructElement
{
    private readonly int? bitSize;

    public static readonly Expr NoArray = NoneExpr.Instance;
    public static readonly Expr UnknownArraysize = new Literal(int.MinValue);

    /// <summary>Creates a field definition and derives pointer depth from the type and field name when it is not supplied.</summary>
    public Field(Identifier type, Identifier name, Expr arraycount, int bitSize, int pointerDepth = -1)
    {
        this.Type = type;
        this.Name = name;
        this.ArrayCount = arraycount;
        this.bitSize = bitSize;
        this.BitSizeExpression = bitSize == 0 ? NoneExpr.Instance : new Literal(bitSize);
        int derivedPointerDepth = type.PointerDepth + name.PointerDepth;
        this.PointerDepth = pointerDepth >= 0 ? pointerDepth : derivedPointerDepth;
        this.IsPointer = this.PointerDepth > 0;
    }

    /// <summary>Creates a parsed field whose bit width will be evaluated with the compiled layout's expression policy.</summary>
    internal Field(Identifier type, Identifier name, Expr arraycount, Expr bitSize, int pointerDepth = -1)
    {
        this.Type = type;
        this.Name = name;
        this.ArrayCount = arraycount;
        this.BitSizeExpression = bitSize;
        int derivedPointerDepth = type.PointerDepth + name.PointerDepth;
        this.PointerDepth = pointerDepth >= 0 ? pointerDepth : derivedPointerDepth;
        this.IsPointer = this.PointerDepth > 0;
    }

    public Expr ArrayCount { get; }

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

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(CStructElement? other)
    {
        return other is Field f &&
               this.Type.Equals(f.Type) &&
               this.Name.Equals(f.Name) &&
               this.ArrayCount.Equals(f.ArrayCount) &&
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
        return HashCode.Combine(this.Type, this.Name, this.ArrayCount, this.BitSizeExpression, this.PointerDepth);
    }

    /// <summary>Returns whether the field's type is known to the supplied lookup.</summary>
    public virtual bool IsKnown<T>(IReadOnlyDictionary<string, T> dict)
    {
        return this.IsPointer || dict.ContainsKey(this.Type.Name);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"{this.Name} ({this.Type}) [{this.ArrayCount}]";
    }
}
