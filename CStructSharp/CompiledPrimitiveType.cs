namespace CStructSharp;

/// <summary>Represents one directly executable primitive codec.</summary>
internal sealed class CompiledPrimitiveType : CompiledType
{
    public CompiledPrimitiveType(CompiledTypeSymbol symbol)
        : base(symbol)
    {
    }
}
