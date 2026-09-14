namespace CStructSharp;

using System.Collections.Immutable;
using CStructSharp.Structure;

/// <summary>Stores one immutable validated array-count strategy and its direct dependencies.</summary>
internal sealed class CompiledArrayShape
{
    public CompiledArrayShape(
        CompiledArrayKind kind,
        Expr? countExpression,
        int? fixedCount,
        ImmutableArray<string> dependencies,
        ImmutableArray<CompiledArrayDimension> dimensions)
    {
        this.Kind = kind;
        this.CountExpression = countExpression;
        this.FixedCount = fixedCount;
        this.Dependencies = dependencies;
        this.Dimensions = dimensions;
    }

    public static CompiledArrayShape Scalar { get; } = new(
        CompiledArrayKind.Scalar,
        null,
        1,
        ImmutableArray<string>.Empty,
        ImmutableArray<CompiledArrayDimension>.Empty);

    public Expr? CountExpression { get; }

    public ImmutableArray<string> Dependencies { get; }

    /// <summary>
    ///     Every dimension this array actually has, outermost first - empty for <see cref="Scalar"/>, one entry
    ///     for every array shape this codebase supported before LANG-05 (mirroring <see cref="Kind"/>/
    ///     <see cref="CountExpression"/>/<see cref="FixedCount"/> exactly), N entries for a multidimensional
    ///     (LANG-05) field. Only the first entry may describe a Runtime/Flexible dimension; every entry after the
    ///     first is always Fixed, since only the outermost dimension of a multidimensional array may ever be
    ///     non-fixed (ADR-016 decision 2) - true today, and still true once a future runtime-sized-outermost-
    ///     dimension follow-on lands, since <see cref="PeelOuterDimension"/> always removes the *current*
    ///     outermost entry, and only the *original* outermost entry can ever be non-fixed.
    /// </summary>
    public ImmutableArray<CompiledArrayDimension> Dimensions { get; }

    public int? FixedCount { get; }

    public CompiledArrayKind Kind { get; }

    /// <summary>
    ///     The total element count across every dimension - 1 for <see cref="Scalar"/>, the product of every
    ///     dimension's fixed count otherwise, or <see langword="null"/> if any dimension has no statically known
    ///     count (a 1-D runtime/flexible array; a multidimensional array can never reach this case, since every
    ///     dimension but the outermost is always fixed and this slice rejects a runtime-sized outermost dimension
    ///     when N &gt; 1).
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
        CompiledArrayDimension next = remaining[0];
        return new CompiledArrayShape(
            CompiledArrayKind.Fixed,
            next.CountExpression,
            next.FixedCount,
            ImmutableArray<string>.Empty,
            remaining);
    }
}
