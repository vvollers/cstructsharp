namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>Keeps parser-expression fixtures behind the test assembly's internal-access boundary.</summary>
internal static class CStructExpressionTestExtensions
{
    /// <summary>Lists every placed field of every compiled struct and union, including inline members.</summary>
    /// <param name="model">The compiled model under test.</param>
    /// <returns>The fields, in no particular order.</returns>
    public static IEnumerable<CompiledField> AllFields(this CompiledLayoutModel model)
    {
        return model.Composites.Values.SelectMany(symbol => ((CompiledCompositeType)symbol.Definition!).Fields);
    }

    /// <summary>Evaluates a parsed expression with the default checked signed 128-bit evaluator, as layout operations do.</summary>
    /// <param name="expression">The expression under test.</param>
    /// <param name="variables">The names the expression may read; none when omitted.</param>
    /// <returns>The expression value.</returns>
    public static Int128 Evaluate(this Expr expression, IReadOnlyDictionary<string, Expr>? variables = null)
    {
        return ExpressionEvaluator.Default.Evaluate(expression, variables);
    }
}
