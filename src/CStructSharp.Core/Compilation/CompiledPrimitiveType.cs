namespace CStructSharp.Compilation;

/// <summary>Represents one directly executable primitive codec.</summary>
internal sealed class CompiledPrimitiveType : CompiledType
{
    /// <summary>Wraps the symbol of a built-in primitive type.</summary>
    /// <param name="symbol">The primitive type's compiled symbol.</param>
    public CompiledPrimitiveType(CompiledTypeSymbol symbol)
        : base(symbol)
    {
    }
}
