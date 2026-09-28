namespace CStructSharp.Compilation;

/// <summary>Base class for immutable validated type definitions.</summary>
internal abstract class CompiledType
{
    /// <summary>Initializes the definition bound to its type symbol.</summary>
    /// <param name="symbol">The symbol this definition is bound to.</param>
    protected CompiledType(CompiledTypeSymbol symbol)
    {
        this.Symbol = symbol;
    }

    /// <summary>Gets the symbol carrying this type's name, kind, fixed size, and alignment.</summary>
    public CompiledTypeSymbol Symbol { get; }
}
