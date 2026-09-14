namespace CStructSharp;

/// <summary>Controls the resource limits applied before a C-like layout definition is parsed and compiled.</summary>
/// <remarks>
///     Values are read and validated when <see cref="CStruct"/> is constructed. Later changes to the options object
///     do not alter an already compiled layout.
/// </remarks>
public sealed class CStructCompilationOptions
{
    /// <summary>Creates the default bounded compilation policy.</summary>
    public CStructCompilationOptions()
    {
    }

    /// <summary>Gets the greatest accepted layout-source length in characters.</summary>
    public int MaxDefinitionLength { get; init; } = 128 * 1024;

    /// <summary>Gets the greatest brace-nesting depth accepted in a layout definition.</summary>
    public int MaxLayoutNestingDepth { get; init; } = 256;

    /// <summary>
    ///     Gets the greatest syntax-tree or identifier-dependency depth accepted for one expression.
    /// </summary>
    public int MaxExpressionNestingDepth { get; init; } = 256;

    /// <summary>
    ///     Gets the greatest number of expression nodes one evaluation session may compile or execute.
    /// </summary>
    public int MaxExpressionTokens { get; init; } = 100_000;
}
