namespace CStructSharp;

using CStructSharp.Structure;

/// <summary>Keeps parser-expression fixtures behind the test assembly's internal-access boundary.</summary>
internal static class CStructExpressionTestExtensions
{
    /// <summary>Runs the internal expression-variable length path for compiler-domain tests.</summary>
    /// <param name="cstruct">The compiled layout under test.</param>
    /// <param name="stream">The binary input.</param>
    /// <param name="elementNameOrPath">The selected layout path.</param>
    /// <param name="variables">The internal expression variables.</param>
    /// <param name="options">Optional read policy.</param>
    /// <returns>The resolved array or string length.</returns>
    public static int GetDynamicArrayLength(
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
    public static dynamic ParseStream(
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
    public static (List<DebugData> DebugData, dynamic Result) ParseStreamWithDebug(
        this CStruct cstruct,
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, Expr>? variables,
        ReadOptions? options = null)
    {
        return cstruct.ParseStreamWithDebugCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromExpressions(variables),
            options);
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
    public static void UpdateStream(
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
    public static void WriteStream(
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
