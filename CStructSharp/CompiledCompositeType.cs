namespace CStructSharp;

using System;
using System.Collections.Immutable;

/// <summary>Represents a struct or union with an immutable declaration-order field collection.</summary>
internal sealed class CompiledCompositeType : CompiledType
{
    public CompiledCompositeType(CompiledTypeSymbol symbol, ImmutableArray<CompiledField> fields)
        : base(symbol)
    {
        this.Fields = fields;
        this.FieldsByName = fields.ToImmutableDictionary(
            field => field.Declaration.Name.Name,
            StringComparer.Ordinal);
    }

    public ImmutableArray<CompiledField> Fields { get; }

    public ImmutableDictionary<string, CompiledField> FieldsByName { get; }
}
