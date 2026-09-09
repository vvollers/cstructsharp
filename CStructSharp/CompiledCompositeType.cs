namespace CStructSharp;

using System;
using System.Collections.Immutable;
using System.Linq;

/// <summary>Represents a struct or union with an immutable declaration-order field collection.</summary>
internal sealed class CompiledCompositeType : CompiledType
{
    public CompiledCompositeType(CompiledTypeSymbol symbol, ImmutableArray<CompiledField> fields)
        : base(symbol)
    {
        this.Fields = fields;

        // An anonymous nonzero-width bitfield (LANG-17) has no name to key by, and several may coexist in one
        // composite without colliding with each other - exclude them rather than deduplicate on an empty key.
        this.FieldsByName = fields.
            Where(field => field.Declaration.Name.Name.Length > 0).
            ToImmutableDictionary(field => field.Declaration.Name.Name, StringComparer.Ordinal);
    }

    public ImmutableArray<CompiledField> Fields { get; }

    public ImmutableDictionary<string, CompiledField> FieldsByName { get; }
}
