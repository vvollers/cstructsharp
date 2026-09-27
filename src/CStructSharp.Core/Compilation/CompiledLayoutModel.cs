namespace CStructSharp.Compilation;

using System.Collections.Generic;
using System.Collections.Immutable;
using CStructSharp.Syntax;

/// <summary>Stores the immutable declaration, type, and composite indexes used after construction.</summary>
internal sealed class CompiledLayoutModel
{
    /// <summary>Creates the model from the frozen indexes that compilation built.</summary>
    /// <param name="declarations">Every top-level declaration by name.</param>
    /// <param name="orderedDeclarations">The same declarations in source order.</param>
    /// <param name="symbols">Every named type by name.</param>
    /// <param name="composites">The compiled symbol of every struct and union declaration.</param>
    /// <param name="rootFields">The compiled field of every typedef, enum, or synthetic root declaration.</param>
    public CompiledLayoutModel(
        ImmutableDictionary<string, CStructElement> declarations,
        ImmutableArray<KeyValuePair<string, CStructElement>> orderedDeclarations,
        ImmutableDictionary<string, CompiledTypeReference> symbols,
        ImmutableDictionary<Struct, CompiledTypeSymbol> composites,
        ImmutableDictionary<CStructElement, CompiledField> rootFields)
    {
        this.Declarations = declarations;
        this.OrderedDeclarations = orderedDeclarations;
        this.Symbols = symbols;
        this.Composites = composites;
        this.RootFields = rootFields;
    }

    /// <summary>Gets the compiled symbol of every struct and union declaration, keyed by declaration identity.</summary>
    public ImmutableDictionary<Struct, CompiledTypeSymbol> Composites { get; }

    /// <summary>Gets every top-level declaration by name.</summary>
    public ImmutableDictionary<string, CStructElement> Declarations { get; }

    /// <summary>Gets the top-level declarations in source order.</summary>
    public ImmutableArray<KeyValuePair<string, CStructElement>> OrderedDeclarations { get; }

    /// <summary>Gets the compiled field of every typedef, enum, or synthetic root declaration.</summary>
    public ImmutableDictionary<CStructElement, CompiledField> RootFields { get; }

    /// <summary>Gets every named type by name.</summary>
    public ImmutableDictionary<string, CompiledTypeReference> Symbols { get; }
}
