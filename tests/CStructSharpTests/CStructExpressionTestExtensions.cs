namespace CStructSharp;

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

    /// <summary>Evaluates a parsed expression with the default checked Int32 evaluator, as layout operations do.</summary>
    /// <param name="expression">The expression under test.</param>
    /// <param name="variables">The names the expression may read; none when omitted.</param>
    /// <returns>The expression value.</returns>
    public static int Evaluate(this Expr expression, IReadOnlyDictionary<string, Expr>? variables = null)
    {
        return ExpressionEvaluator.Default.Evaluate(expression, variables);
    }

    /// <summary>Runs the internal expression-variable length path for compiler-domain tests.</summary>
    /// <param name="cstruct">The compiled layout under test.</param>
    /// <param name="stream">The binary input.</param>
    /// <param name="elementNameOrPath">The selected layout path.</param>
    /// <param name="variables">The internal expression variables.</param>
    /// <param name="options">Optional read policy.</param>
    /// <returns>The resolved array or string length.</returns>
    public static int GetArrayLength(
        this CStruct cstruct,
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, Expr>? variables,
        ReadOptions? options = null)
    {
        return cstruct.GetDynamicArrayLengthCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromExpressions(variables),
            options);
    }

    /// <summary>Runs the internal expression-variable parse path for compiler-domain tests.</summary>
    /// <param name="cstruct">The compiled layout under test.</param>
    /// <param name="stream">The binary input.</param>
    /// <param name="elementNameOrPath">The selected layout path.</param>
    /// <param name="variables">The internal expression variables.</param>
    /// <param name="options">Optional read policy.</param>
    /// <returns>The parsed value.</returns>
    public static dynamic Parse(
        this CStruct cstruct,
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, Expr>? variables,
        ReadOptions? options = null)
    {
        return cstruct.ParseStreamCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromExpressions(variables),
            options);
    }

    /// <summary>Runs the internal expression-variable debug path for compiler-domain tests.</summary>
    /// <param name="cstruct">The compiled layout under test.</param>
    /// <param name="stream">The binary input.</param>
    /// <param name="elementNameOrPath">The selected layout path.</param>
    /// <param name="variables">The internal expression variables.</param>
    /// <param name="options">Optional read policy.</param>
    /// <returns>The captured ranges and parsed value.</returns>
    public static ParseResult ParseWithDebug(
        this CStruct cstruct,
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, Expr>? variables,
        ReadOptions? options = null)
    {
        (List<DebugData> debug, object value) = cstruct.ParseStreamWithDebugCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromExpressions(variables),
            options);
        return new ParseResult((StructValue)value, debug);
    }

    /// <summary>Runs the internal expression-variable address path for compiler-domain tests.</summary>
    /// <param name="cstruct">The compiled layout under test.</param>
    /// <param name="stream">The binary input.</param>
    /// <param name="elementNameOrPath">The selected layout path.</param>
    /// <param name="variables">The internal expression variables.</param>
    /// <param name="options">Optional read policy.</param>
    /// <returns>The resolved stream position.</returns>
    public static long ResolveAddress(
        this CStruct cstruct,
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, Expr>? variables,
        ReadOptions? options = null)
    {
        return cstruct.ResolveAddressCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromExpressions(variables),
            options);
    }

    /// <summary>Runs the internal expression-variable serialization path for compiler-domain tests.</summary>
    /// <param name="cstruct">The compiled layout under test.</param>
    /// <param name="elementNameOrPath">The selected layout path.</param>
    /// <param name="data">The value to encode.</param>
    /// <param name="variables">The internal expression variables.</param>
    /// <param name="options">Optional write policy.</param>
    /// <returns>The encoded bytes.</returns>
    public static byte[] Serialize(
        this CStruct cstruct,
        string elementNameOrPath,
        object data,
        IReadOnlyDictionary<string, Expr>? variables,
        WriteOptions? options = null)
    {
        return cstruct.SerializeCore(
            elementNameOrPath,
            data,
            LayoutVariableInput.FromExpressions(variables),
            options);
    }

    /// <summary>Runs the internal expression-variable update path for compiler-domain tests.</summary>
    /// <param name="cstruct">The compiled layout under test.</param>
    /// <param name="stream">The destination stream.</param>
    /// <param name="elementNameOrPath">The selected layout path.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variables">The internal expression variables.</param>
    /// <param name="options">Optional update policy.</param>
    public static void Update(
        this CStruct cstruct,
        Stream stream,
        string elementNameOrPath,
        object value,
        IReadOnlyDictionary<string, Expr>? variables,
        UpdateOptions? options = null)
    {
        cstruct.UpdateStreamCore(
            stream,
            elementNameOrPath,
            value,
            LayoutVariableInput.FromExpressions(variables),
            options);
    }

    /// <summary>Runs the internal expression-variable write path for compiler-domain tests.</summary>
    /// <param name="cstruct">The compiled layout under test.</param>
    /// <param name="stream">The destination stream.</param>
    /// <param name="elementNameOrPath">The selected layout path.</param>
    /// <param name="data">The value to encode.</param>
    /// <param name="variables">The internal expression variables.</param>
    /// <param name="options">Optional write policy.</param>
    public static void Write(
        this CStruct cstruct,
        Stream stream,
        string elementNameOrPath,
        object data,
        IReadOnlyDictionary<string, Expr>? variables,
        WriteOptions? options = null)
    {
        cstruct.WriteStreamCore(
            stream,
            elementNameOrPath,
            data,
            LayoutVariableInput.FromExpressions(variables),
            options);
    }
}
