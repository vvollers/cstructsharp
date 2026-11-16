namespace CStructSharp.Compilation;

using CStructSharp.Syntax;

/// <summary>One dimension's own count expression and, when statically known, its fixed element count.</summary>
/// <param name="CountExpression">The dimension's count expression as written, or a marker such as <c>[]</c> or <c>[EOF]</c>.</param>
/// <param name="FixedCount">The element count when it is a compile-time constant; <see langword="null"/> for runtime, terminated and to-end dimensions.</param>
internal readonly record struct CompiledArrayDimension(Expr? CountExpression, int? FixedCount);
