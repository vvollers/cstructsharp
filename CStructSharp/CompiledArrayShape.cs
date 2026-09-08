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
        ImmutableArray<string> dependencies)
    {
        this.Kind = kind;
        this.CountExpression = countExpression;
        this.FixedCount = fixedCount;
        this.Dependencies = dependencies;
    }

    public static CompiledArrayShape Scalar { get; } = new(
        CompiledArrayKind.Scalar,
        null,
        1,
        ImmutableArray<string>.Empty);

    public Expr? CountExpression { get; }

    public ImmutableArray<string> Dependencies { get; }

    public int? FixedCount { get; }

    public CompiledArrayKind Kind { get; }
}
