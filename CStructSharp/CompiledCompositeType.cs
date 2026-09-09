namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Structure;

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

        // An anonymous promoted struct member (LANG-14) has no name of its own; its own fields are spliced into
        // this composite's namespace instead. Computed once here so every splicing call site (reader, writer,
        // address resolver, layout) shares one definition instead of re-deriving the predicate independently.
        // One level only - a consumer that needs to see through transitive promotion reads a promoted field's own
        // compiled composite's PromotedFields again, rather than this set being pre-flattened.
        this.PromotedFields = fields.
            Where(field => field.Declaration is Struct { Name.Name.Length: 0, }).
            ToImmutableHashSet<CompiledField>(ReferenceEqualityComparer.Instance);
    }

    public ImmutableArray<CompiledField> Fields { get; }

    public ImmutableDictionary<string, CompiledField> FieldsByName { get; }

    public ImmutableHashSet<CompiledField> PromotedFields { get; }
}
