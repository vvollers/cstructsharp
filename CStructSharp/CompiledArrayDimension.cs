namespace CStructSharp;

using CStructSharp.Structure;

/// <summary>One dimension's own count expression and, when statically known, its fixed element count.</summary>
internal readonly record struct CompiledArrayDimension(Expr? CountExpression, int? FixedCount);
