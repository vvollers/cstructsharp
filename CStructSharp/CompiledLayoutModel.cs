namespace CStructSharp;

using System.Collections.Generic;
using System.Collections.Immutable;
using CStructSharp.Structure;

/// <summary>Stores the immutable type, composite, and field indexes used after construction.</summary>
internal sealed class CompiledLayoutModel
{
    public CompiledLayoutModel(
        ImmutableDictionary<string, CStructElement> declarations,
        ImmutableArray<KeyValuePair<string, CStructElement>> orderedDeclarations,
        ImmutableDictionary<string, CompiledTypeReference> symbols,
        ImmutableDictionary<Struct, CompiledTypeSymbol> composites,
        ImmutableDictionary<Field, CompiledField> fields,
        ImmutableDictionary<CStructElement, CompiledField> rootFields)
    {
        this.Declarations = declarations;
        this.OrderedDeclarations = orderedDeclarations;
        this.Symbols = symbols;
        this.Composites = composites;
        this.Fields = fields;
        this.RootFields = rootFields;
    }

    public ImmutableDictionary<Struct, CompiledTypeSymbol> Composites { get; }

    public ImmutableDictionary<string, CStructElement> Declarations { get; }

    public ImmutableDictionary<Field, CompiledField> Fields { get; }

    public ImmutableArray<KeyValuePair<string, CStructElement>> OrderedDeclarations { get; }

    public ImmutableDictionary<CStructElement, CompiledField> RootFields { get; }

    public ImmutableDictionary<string, CompiledTypeReference> Symbols { get; }
}
