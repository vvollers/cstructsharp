namespace CStructSharp.Syntax;

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
    ///     Entry of a one-dimensional <see cref="ArrayCount"/> list (an unsized dimension can
    ///     never appear as one of several dimensions of a multidimensional array).
    /// </summary>
    public static readonly Expr UnknownArraysize = new Literal(int.MinValue);

    /// <summary>
    ///     Creates a field. A parsed field carries its width as written; normalization replaces the width with its
    ///     evaluated literal (see <see cref="Width"/>), which <see cref="BitSize"/> then returns. The pointer depth is
    ///     the type's and the name's stars together unless <paramref name="pointerDepth"/> gives it.
    /// </summary>
    /// <param name="type">The type name.</param>
    /// <param name="name">The field name.</param>
    /// <param name="arraycount">Every dimension's count expression, outermost first; <see cref="NoArray"/> for a scalar.</param>
    /// <param name="bitSize">The <c>: width</c> expression, or <see cref="NoneExpr.Instance"/> for a field without one.</param>
    /// <param name="pointerDepth">The pointer depth, or -1 to derive it from the type and name.</param>
    /// <param name="typeKeywordHint">The struct/union/enum keyword written before the type, or <see langword="null"/>.</param>
    /// <param name="alignmentOverrideExpression">The <c>@align(N)</c> expression, or <see langword="null"/>.</param>
    /// <param name="offsetAssertionExpression">The <c>@N</c> expression, or <see langword="null"/>.</param>
    public Field(
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
        this.bitSize = bitSize switch
        {
            NoneExpr => 0,
            Literal literal => literal.Value,
            _ => null,
        };
        int derivedPointerDepth = type.PointerDepth + name.PointerDepth;
        this.PointerDepth = pointerDepth >= 0 ? pointerDepth : derivedPointerDepth;
        this.IsPointer = this.PointerDepth > 0;
        this.TypeKeywordHint = typeKeywordHint;
        this.AlignmentOverrideExpression = alignmentOverrideExpression;
        this.OffsetAssertionExpression = offsetAssertionExpression;
    }

    /// <summary>
    ///     Every array dimension's own count expression, outermost first - empty for a scalar field
    ///     (<see cref="NoArray"/>), one entry for every array this codebase supported before multidimensional arrays, N entries
    ///     for a multidimensional field.
    /// </summary>
    public IReadOnlyList<Expr> ArrayCount { get; }

    /// <summary>Gets the bitfield width in bits; 0 for a field without one.</summary>
    /// <exception cref="InvalidOperationException">The width is negative, or is an expression normalization has not evaluated yet.</exception>
    public int BitSize => this.bitSize switch
    {
        >= 0 and int value => value,
        int => throw new InvalidOperationException("Bitfield width cannot be negative."),
        null => throw new InvalidOperationException("Bitfield width is not evaluated yet: " + this.BitSizeExpression),
    };

    /// <summary>Whether the declarator carried a <c>: width</c>; a zero width with no name is a storage-unit separator.</summary>
    internal bool HasBitfieldDeclarator => !ReferenceEquals(this.BitSizeExpression, NoneExpr.Instance);

    internal Expr BitSizeExpression { get; }

    /// <summary>The arms this field sits in, outermost first; empty for an unconditional field.</summary>
    internal IReadOnlyList<ConditionalBranch> BranchConditions { get; set; } =
        Array.Empty<ConditionalBranch>();

    /// <summary>Whether the field sits in an arm of an <c>if</c> or <c>switch</c>, so the data decides whether it is present.</summary>
    internal bool IsConditional => this.BranchConditions.Count > 0;

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

    /// <summary>
    ///     The optional <c>@count(N)</c> element count written after a pointer declarator, or null when none was
    ///     written. It makes the pointer's final target an array of N elements instead of one value.
    /// </summary>
    internal Expr? PointerCountExpression { get; init; }

    /// <summary>The width expression for an evaluated width: none for 0 without a <c>:</c>, otherwise the literal.</summary>
    /// <param name="bitSize">The width in bits.</param>
    /// <param name="hasBitfieldDeclarator">Whether the declarator carries a <c>: width</c> (a <c>: 0</c> separator does).</param>
    /// <returns>The expression for <see cref="Field"/>'s constructor.</returns>
    public static Expr Width(int bitSize, bool hasBitfieldDeclarator = false)
        => bitSize == 0 && !hasBitfieldDeclarator ? NoneExpr.Instance : new Literal(bitSize);

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(CStructElement? other)
    {
        return other is Field f &&
               this.Type.Equals(f.Type) &&
               this.Name.Equals(f.Name) &&
               this.ArrayCount.SequenceEqual(f.ArrayCount) &&
               this.BitSizeExpression.Equals(f.BitSizeExpression) &&
               this.PointerDepth == f.PointerDepth &&
               Equals(this.PointerCountExpression, f.PointerCountExpression);
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

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"{this.Name} ({this.Type}) [{string.Join("][", this.ArrayCount)}]";
    }
}
