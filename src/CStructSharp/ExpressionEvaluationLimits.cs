namespace CStructSharp;

/// <summary>Holds the immutable expression resource limits captured by a compiled layout.</summary>
internal readonly record struct ExpressionEvaluationLimits(int MaximumDepth, int MaximumNodes)
{
    /// <summary>Creates a validated immutable snapshot of public compilation settings.</summary>
    public static ExpressionEvaluationLimits FromOptions(CStructCompilationOptions options)
    {
        return new ExpressionEvaluationLimits(
            options.MaxExpressionNestingDepth,
            options.MaxExpressionTokens);
    }
}
