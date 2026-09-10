namespace CStructSharp;

/// <summary>Base class for immutable validated type definitions.</summary>
internal abstract class CompiledType
{
    protected CompiledType(CompiledTypeSymbol symbol)
    {
        this.Symbol = symbol;
    }

    public CompiledTypeSymbol Symbol { get; }
}
