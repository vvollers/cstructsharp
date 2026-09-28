namespace CStructSharp.Expressions;

/// <summary>Holds the immutable expression resource limits captured by a compiled layout.</summary>
internal readonly record struct ExpressionEvaluationLimits(int MaximumDepth, int MaximumNodes)
{
    /// <summary>Creates a validated immutable snapshot of public compilation settings.</summary>
    /// <param name="options">The compilation options that supply the nesting-depth and token limits.</param>
    /// <returns>
    ///     The limits taken from <see cref="CStructCompilationOptions.MaxExpressionNestingDepth"/> and
    ///     <see cref="CStructCompilationOptions.MaxExpressionTokens"/>.
    /// </returns>
    public static ExpressionEvaluationLimits FromOptions(CStructCompilationOptions options)
    {
        return new ExpressionEvaluationLimits(
            options.MaxExpressionNestingDepth,
            options.MaxExpressionTokens);
    }
}
