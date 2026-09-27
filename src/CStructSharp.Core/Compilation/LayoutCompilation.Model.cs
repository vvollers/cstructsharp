namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using System.Linq;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>Builds and queries the immutable compiled layout model from validated parsed declarations.</summary>
internal sealed partial class LayoutCompilation
{
    private readonly CompiledLayoutModel compiledLayout;

    /// <summary>Gets the immutable internal model for invariant tests and later compiled-executor migrations.</summary>
    internal CompiledLayoutModel CompiledModel => this.compiledLayout;

    /// <summary>The codec id a synthetic root of <paramref name="symbol"/> reads with (a primitive's own, an enum's underlying, else none).</summary>
    internal static int CodecIdOf(CompiledTypeSymbol symbol)
    {
        return GetCompiledCodecId(symbol);
    }

    /// <summary>Returns a primitive's codec id directly or the compiled underlying primitive's id for an enum.</summary>
    private static int GetCompiledCodecId(CompiledTypeSymbol symbol)
    {
        return symbol.Kind switch
        {
            CompiledTypeKind.Primitive => symbol.CodecId,
            CompiledTypeKind.Enum when symbol.Definition is CompiledEnumType enm => enm.Underlying.Symbol.CodecId,
            _ => PrimitiveCatalog.NoCodec,
        };
    }

    /// <summary>
    ///     Checks that a type written with a <c>struct</c>, <c>union</c> or <c>enum</c> keyword names that kind of type.
    /// </summary>
    /// <param name="keyword">The keyword written before the type name, or <see langword="null"/> when none was.</param>
    /// <param name="type">The resolved type.</param>
    /// <param name="subject">The declaration, for the diagnostic (for example <c>Field 'x'</c>).</param>
    /// <param name="typeName">The written type name; its source offset locates the diagnostic.</param>
    /// <exception cref="CStructLayoutException">The keyword names a different kind of type.</exception>
    private static void CheckTypeKeyword(string? keyword, CompiledTypeReference type, string subject, Identifier typeName)
    {
        if (keyword is null)
        {
            return;
        }

        string actualKind = type.Symbol.Kind.ToString().ToLowerInvariant();
        if (!string.Equals(keyword, actualKind, StringComparison.Ordinal))
        {
            throw new CStructLayoutException($"{subject} declared as '{keyword}' but '{typeName.Name}' is a {actualKind}.")
            {
                SourceOffset = typeName.SourceOffset,
            };
        }
    }

    /// <summary>Builds the operation-time model after parsed declarations have passed all layout validation.</summary>
    /// <returns>The compiled model.</returns>
    /// <exception cref="CStructLayoutException">The layout is invalid, or its sizes overflow.</exception>
    private CompiledLayoutModel CompileIntermediateRepresentation()
    {
        try
        {
            return this.BuildCompiledLayout();
        }
        catch (CStructLayoutException)
        {
            throw;
        }
        catch (OverflowException exception)
        {
            // Checked size and offset arithmetic overflows on a layout too large to address; any other exception here
            // is a bug and propagates as itself.
            throw new CStructLayoutException(
                "Layout could not be converted to the compiled intermediate representation.",
                exception);
        }
    }

    /// <summary>Creates all type symbols first, then binds immutable enum, field, and composite definitions.</summary>
    private CompiledLayoutModel BuildCompiledLayout()
    {
        // The catalog's symbols include its custom codecs.
        var namedTypes = this.catalog.Symbols.ToBuilder();

        // A layout declaration shadows a built-in alias spelling of the same name (SymbolValidation lets those
        // through); drop the built-in entry so the declaration is what the name resolves to.
        foreach (string declaredName in this.CStructElements.Keys)
        {
            if (PrimitiveSpellings.IsAlias(declaredName))
            {
                namedTypes.Remove(declaredName);
            }
        }

        var compositeSymbols = new Dictionary<Struct, CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);
        var enumSymbols = new Dictionary<CstructEnum, CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);

        foreach (CStructElement declaration in this.CStructElements.Values)
        {
            switch (declaration)
            {
            case Struct strct:
                this.DiscoverCompositeSymbols(strct, compositeSymbols);
                break;
            case Typedef { Struct: not null, } typedef:
                this.DiscoverCompositeSymbols(typedef.Struct, compositeSymbols);
                break;
            case CstructEnum enm:
                enumSymbols.Add(
                    enm,
                    new CompiledTypeSymbol(
                        enm.Name.Name,
                        CompiledTypeKind.Enum,
                        enm,
                        this.enumIntegerCodecs.Get(enm.Name.Name).SizeInBytes,
                        this.enumIntegerCodecs.Get(enm.Name.Name).SizeInBytes,
                        PrimitiveCatalog.NoCodec));
                break;
            }
        }

        // Every composite symbol is discovered by this point and compositeSymbols never gains further entries, so a
        // size-query view built now stays valid for the rest of construction - including the union-storage check
        // below, before the final immutable CompiledLayoutModel exists.
        var sizeQueries = new CompiledSizeQueries(compositeSymbols, this.Aligned, this.BitfieldPacking, this.highBitFirst, this.layoutExpressionEvaluator);
        var context = new CompositeCompilationContext(namedTypes, compositeSymbols, sizeQueries);

        foreach (KeyValuePair<string, CStructElement> declaration in this.CStructElements)
        {
            if (declaration.Value is Struct strct)
            {
                namedTypes.Add(
                    declaration.Key,
                    new CompiledTypeReference(
                        compositeSymbols[strct],
                        0,
                        declaration.Key));
            }
        }

        foreach (KeyValuePair<CstructEnum, CompiledTypeSymbol> enm in enumSymbols)
        {
            namedTypes.Add(enm.Key.Name.Name, new CompiledTypeReference(enm.Value, 0, enm.Key.Name.Name));
        }

        foreach (KeyValuePair<string, CStructElement> declaration in this.CStructElements)
        {
            if (declaration.Value is Typedef)
            {
                _ = this.ResolveCompiledTypeReference(declaration.Key, context);
            }
        }

        foreach (KeyValuePair<CstructEnum, CompiledTypeSymbol> enm in enumSymbols)
        {
            CompiledTypeReference underlying = this.ResolveCompiledTypeReference(enm.Key.Type.Name, context);
            if (underlying.PointerDepth != 0 || underlying.Symbol.Kind != CompiledTypeKind.Primitive)
            {
                throw new CStructLayoutException(
                    "Enum storage type must resolve to a scalar primitive codec: " + enm.Key.Type.Name);
            }

            var members = ImmutableArray.CreateBuilder<CompiledEnumMember>(enm.Key.Values.Length);
            foreach (EnumValue value in enm.Key.Values)
            {
                if (value.Value is not Literal literal)
                {
                    throw new CStructLayoutException(
                        "Compiled enum member is not an evaluated literal: " + value.Name.Name);
                }

                members.Add(
                    new CompiledEnumMember(
                        value.Name.Name,
                        this.enumIntegerCodecs.Get(enm.Key.Name.Name).ToRawBits(literal.ExactValue)));
            }

            enm.Value.Bind(
                new CompiledEnumType(
                    enm.Value,
                    underlying,
                    this.enumIntegerCodecs.Get(enm.Key.Name.Name),
                    members.ToImmutable(),
                    enm.Key.IsFlag));
        }

        foreach (Struct declaration in compositeSymbols.Keys)
        {
            this.CompileComposite(declaration, context);
        }

        var rootFields = ImmutableDictionary.CreateBuilder<CStructElement, CompiledField>(
            ReferenceEqualityComparer.Instance);
        foreach (CStructElement declaration in this.CStructElements.Values)
        {
            if (declaration is not (Typedef or CstructEnum))
            {
                continue;
            }

            CompiledTypeReference type = namedTypes[declaration.Name.Name];

            // A `typedef T name[N];` root reads its whole fixed shape.
            IReadOnlyList<Expr> rootShape = declaration is Typedef { ArrayShape.Count: > 0, } arrayAlias
                                                ? arrayAlias.ArrayShape
                                                : Field.NoArray;
            var field = new Field(
                new Identifier(type.TerminalName),
                declaration.Name,
                rootShape,
                NoneExpr.Instance,
                type.PointerDepth);
            int codecId = GetCompiledCodecId(type.Symbol);
            int terminatedCodecId = PrimitiveCatalog.NoCodec;
            if (type.PointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(field.Type))
            {
                terminatedCodecId = this.catalog.CodecIdOf(CharacterFieldTypes.GetStringPointerHandlerKey(field.Type));
            }

            int alignment = type.PointerDepth > 0 ? this.PointerSize : type.Symbol.Alignment;
            int? elementSize = type.PointerDepth > 0 ? this.PointerSize : type.Symbol.FixedSize;
            CompiledArrayShape rootArrayShape = this.CompileArrayShape(field);
            int? rootStorageSize = elementSize.HasValue && rootArrayShape.TotalFixedElementCount.HasValue
                                       ? checked(elementSize.Value * rootArrayShape.TotalFixedElementCount.Value)
                                       : elementSize;
            var compiledRoot = new CompiledField(
                field,
                field,
                type,
                codecId,
                terminatedCodecId,
                alignment,
                elementSize,
                rootArrayShape,
                rootStorageSize,
                false,
                null,
                null,
                0,
                0,
                this.IsLittleEndian);
            rootFields.Add(declaration, compiledRoot);
        }

        var publishedSymbols = new HashSet<CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);
        foreach (CompiledTypeSymbol symbol in compositeSymbols.Values.Concat(enumSymbols.Values))
        {
            if (!publishedSymbols.Add(symbol))
            {
                continue;
            }

            if (!symbol.IsBound)
            {
                throw new CStructLayoutException("Compiled type symbol was not bound: " + symbol.Name);
            }

            if (symbol.Definition is CompiledCompositeType composite)
            {
                composite.CompleteConditionalScope();
            }

            symbol.Freeze();
        }

        this.MarkReferencedLayoutVariables(publishedSymbols, rootFields);

        return new CompiledLayoutModel(
            this.cStructElements.ToImmutableDictionary(StringComparer.Ordinal),
            this.cStructElements.ToImmutableArray(),
            namedTypes.ToImmutable(),
            compositeSymbols.ToImmutableDictionary(ReferenceEqualityComparer.Instance),
            rootFields.ToImmutable());
    }

    /// <summary>Predeclares exact composite identities so recursive pointers can refer to a symbol before it is bound.</summary>
    private void DiscoverCompositeSymbols(
        Struct strct,
        Dictionary<Struct, CompiledTypeSymbol> compositeSymbols)
    {
        if (compositeSymbols.ContainsKey(strct))
        {
            return;
        }

        compositeSymbols.Add(
            strct,
            CompiledTypeSymbol.PredeclareComposite(strct));
        foreach (Field field in strct.Fields)
        {
            if (field is Struct nested)
            {
                this.DiscoverCompositeSymbols(nested, compositeSymbols);
            }
        }
    }

    /// <summary>
    ///     Resolves a field's declared type and, when the spelling is unknown, reports it with the field, the
    ///     containing declaration, and the most likely cause: a multi-word spelling whose first word is itself a
    ///     type is almost always a missing <c>;</c> between two declarators.
    /// </summary>
    private CompiledTypeReference ResolveFieldTypeReference(
        Field field,
        Struct owner,
        CompositeCompilationContext context)
    {
        try
        {
            return this.ResolveCompiledTypeReference(field.Type.Name, context);
        }
        catch (CStructLayoutException exception) when (exception.SourceOffset < 0 && exception.Message.StartsWith("Unknown field type: ", StringComparison.Ordinal))
        {
            string spelling = field.Type.Name;
            string where = owner.Name.Name.Length == 0 ? "an anonymous " + (owner.IsUnion ? "union" : "struct") : (owner.IsUnion ? "union '" : "struct '") + owner.Name.Name + "'";
            string hint = string.Empty;
            int space = spelling.IndexOf(' ');
            if (space > 0)
            {
                string firstWord = spelling[..space];
                if (this.catalog.IsKnownName(firstWord) || context.NamedTypes.ContainsKey(firstWord) || this.CStructElements.ContainsKey(firstWord))
                {
                    int secondSpace = spelling.IndexOf(' ', space + 1);
                    string secondWord = secondSpace > 0 ? spelling[(space + 1)..secondSpace] : spelling[(space + 1)..];
                    hint = $"; a ';' may be missing after '{secondWord}'";
                }
            }

            throw new CStructLayoutException(
                $"Unknown type '{spelling}' for field '{field.Name.Name}' in {where}{hint}.",
                exception)
            {
                SourceOffset = field.Type.SourceOffset,
            };
        }
    }

    /// <summary>Resolves one exported or primitive name once, accumulating pointer depth across typedef chains.</summary>
    private CompiledTypeReference ResolveCompiledTypeReference(
        string name,
        CompositeCompilationContext context)
    {
        if (context.NamedTypes.TryGetValue(name, out CompiledTypeReference known))
        {
            return known;
        }

        if (!this.CStructElements.TryGetValue(name, out CStructElement? declaration) ||
            declaration is not Typedef alias)
        {
            // `size_t` and its relatives are as wide as this layout's pointers: resolved here, on use, rather than
            // seeded into every layout's type table (a dozen immutable-dictionary mutations per compile). A layout
            // declaration of the same name was found above and wins.
            if (declaration is null && PrimitiveSpellings.PointerSizedIsUnsigned.TryGetValue(name, out bool pointerUnsigned))
            {
                return context.NamedTypes[PrimitiveSpellings.PointerSizedCanonical(pointerUnsigned, this.PointerSize)];
            }

            throw new CStructLayoutException("Unknown field type: " + name);
        }

        if (!context.ResolvingAliases.Add(name))
        {
            throw new CStructLayoutException("Circular typedef dependency detected at: " + name);
        }

        try
        {
            CompiledTypeReference resolved;
            if (alias.Struct is not null)
            {
                resolved = new CompiledTypeReference(
                    context.CompositeSymbols[alias.Struct],
                    0,
                    name);
            }
            else
            {
                CompiledTypeReference target = this.ResolveCompiledTypeReference(
                    alias.Type.Name, context);
                CheckTypeKeyword(alias.TypeKeywordHint, target, $"Typedef '{name}'", alias.Type);

                resolved = new CompiledTypeReference(
                    target.Symbol,
                    checked(target.PointerDepth + alias.Type.PointerDepth),
                    target.TerminalName);
            }

            context.NamedTypes.Add(name, resolved);
            return resolved;
        }
        finally
        {
            context.ResolvingAliases.Remove(name);
        }
    }

    /// <summary>The compiled enum of a declaration, for the browser bridge's static plan description.</summary>
    internal CompiledEnumType GetCompiledEnumForInterop(Syntax.Enum declaration)
    {
        return this.compiledModelQueries.GetCompiledEnum(declaration);
    }

    /// <summary>
    ///     The state that compiling one layout's composites shares: the types by name (growing as typedefs resolve),
    ///     every composite's symbol, the size queries over them, and the two in-progress sets that turn a typedef cycle
    ///     or a by-value recursive struct into an error instead of endless recursion.
    /// </summary>
    private sealed class CompositeCompilationContext
    {
        /// <summary>Starts compiling with every composite symbol discovered.</summary>
        /// <param name="namedTypes">The types by name, which alias resolution extends.</param>
        /// <param name="compositeSymbols">Every composite's symbol, by declaration.</param>
        /// <param name="sizeQueries">The size queries over those composites.</param>
        public CompositeCompilationContext(
            ImmutableDictionary<string, CompiledTypeReference>.Builder namedTypes,
            IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
            CompiledSizeQueries sizeQueries)
        {
            this.NamedTypes = namedTypes;
            this.CompositeSymbols = compositeSymbols;
            this.SizeQueries = sizeQueries;
        }

        /// <summary>Gets the types by name, which alias resolution extends.</summary>
        public ImmutableDictionary<string, CompiledTypeReference>.Builder NamedTypes { get; }

        /// <summary>Gets every composite's symbol, by declaration.</summary>
        public IReadOnlyDictionary<Struct, CompiledTypeSymbol> CompositeSymbols { get; }

        /// <summary>Gets the size queries over the composites.</summary>
        public CompiledSizeQueries SizeQueries { get; }

        /// <summary>Gets the typedef names being resolved, to reject an alias cycle.</summary>
        public HashSet<string> ResolvingAliases { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the composites being compiled, to reject a by-value recursive struct.</summary>
        public HashSet<Struct> Compiling { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
