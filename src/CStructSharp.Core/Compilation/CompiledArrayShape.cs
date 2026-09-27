namespace CStructSharp.Compilation;

using System.Collections.Immutable;
using CStructSharp.Syntax;

/// <summary>Stores one immutable validated array-count strategy and its direct dependencies.</summary>
internal sealed class CompiledArrayShape
{
    /// <summary>Creates a shape from its kind, the names its count reads, and its dimensions.</summary>
    /// <param name="kind">How the outermost dimension's count is decided.</param>
    /// <param name="dependencies">The layout variables the count expression reads.</param>
    /// <param name="dimensions">Every dimension, outermost first; empty for a scalar.</param>
    public CompiledArrayShape(
        CompiledArrayKind kind,
        ImmutableArray<string> dependencies,
        ImmutableArray<CompiledArrayDimension> dimensions)
    {
        this.Kind = kind;
        this.Dependencies = dependencies;
        this.Dimensions = dimensions;
    }

    /// <summary>Gets the shape of a field that is not an array: no dimensions and one element.</summary>
    public static CompiledArrayShape Scalar { get; } = new(
        CompiledArrayKind.Scalar,
        ImmutableArray<string>.Empty,
        ImmutableArray<CompiledArrayDimension>.Empty);

    /// <summary>Gets the outermost dimension's count expression, or <see langword="null"/> for a scalar.</summary>
    public Expr? CountExpression => this.Dimensions.IsEmpty ? null : this.Dimensions[0].CountExpression;

    /// <summary>Gets the layout variables the count expression reads.</summary>
    public ImmutableArray<string> Dependencies { get; }

    /// <summary>
    ///     Gets every dimension, outermost first: empty for <see cref="Scalar"/>, one entry for a one-dimensional
    ///     array, N entries for an N-dimensional one. Only the first entry may be runtime-sized or flexible; every
    ///     later entry is fixed, because only the outermost dimension of a multidimensional array may depend on data.
    /// </summary>
    public ImmutableArray<CompiledArrayDimension> Dimensions { get; }

    /// <summary>Gets the outermost dimension's static count, 1 for a scalar, or <see langword="null"/> when it depends on data.</summary>
    public int? FixedCount => this.Dimensions.IsEmpty ? 1 : this.Dimensions[0].FixedCount;

    /// <summary>Gets how the outermost dimension's count is decided.</summary>
    public CompiledArrayKind Kind { get; }

    /// <summary>
    ///     The total element count across every dimension - 1 for <see cref="Scalar"/>, the product of every
    ///     dimension's fixed count otherwise, or <see langword="null"/> if any dimension has no statically known
    ///     count (a one-dimensional runtime-sized or flexible array; a multidimensional array never is, because
    ///     compilation requires every one of its dimensions to be fixed).
    /// </summary>
    public int? TotalFixedElementCount
    {
        get
        {
            if (this.Dimensions.IsEmpty)
            {
                return 1;
            }

            int total = 1;
            foreach (CompiledArrayDimension dimension in this.Dimensions)
            {
                if (dimension.FixedCount is not int count)
                {
                    return null;
                }

                total = checked(total * count);
            }

            return total;
        }
    }

    /// <summary>
    ///     Returns the shape one dimension inward - the remaining dimensions' own shape if more than one
    ///     dimension remains, or <see cref="Scalar"/> once the last dimension has been peeled away. Every
    ///     consumer of a multidimensional array (the reader/writer element loops, the address resolver) reaches
    ///     an N-dimensional shape by calling this once per dimension, exactly the same way a 1-D array's single
    ///     dimension has always been consumed in one call.
    /// </summary>
    public CompiledArrayShape PeelOuterDimension()
    {
        if (this.Dimensions.Length <= 1)
        {
            return Scalar;
        }

        ImmutableArray<CompiledArrayDimension> remaining = this.Dimensions.RemoveAt(0);
        return new CompiledArrayShape(CompiledArrayKind.Fixed, ImmutableArray<string>.Empty, remaining);
    }
}
