namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>Builds and queries the immutable compiled layout model from validated parsed declarations.</summary>
public partial class CStruct
{
    private readonly CompiledLayoutModel compiledLayout;

    /// <summary>Gets the immutable internal model for invariant tests and later compiled-executor migrations.</summary>
    internal CompiledLayoutModel CompiledModel => this.compiledLayout;

    /// <summary>Builds the operation-time model after parsed declarations have passed all layout validation.</summary>
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
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or
                                          InvalidOperationException or KeyNotFoundException)
        {
            throw new CStructLayoutException(
                "Layout could not be converted to the compiled intermediate representation.",
                exception);
        }
    }

    /// <summary>Creates all type symbols first, then binds immutable enum, field, and composite definitions.</summary>
    private CompiledLayoutModel BuildCompiledLayout()
    {
        var namedTypes = new Dictionary<string, CompiledTypeReference>(StringComparer.Ordinal);
        var compositeSymbols = new Dictionary<Struct, CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);
        var enumSymbols = new Dictionary<CstructEnum, CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);

        foreach (KeyValuePair<string, Func<Stream, object>> reader in this.fieldHandlers)
        {
            if (!this.writeHandlers.TryGetValue(reader.Key, out Action<Stream, object>? writer))
            {
                throw new CStructLayoutException("Primitive codec has no matching writer: " + reader.Key);
            }

            int? fixedSize = PrimitiveCodecs.IsVariableLengthType(reader.Key) ? null : this.fieldAlignments[reader.Key];
            var symbol = new CompiledTypeSymbol(
                reader.Key,
                CompiledTypeKind.Primitive,
                null,
                this.fieldAlignments[reader.Key],
                fixedSize,
                reader.Value,
                writer);
            symbol.Bind(new CompiledPrimitiveType(symbol));
            namedTypes.Add(reader.Key, new CompiledTypeReference(symbol, 0, reader.Key));
        }

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
                        null,
                        null));
                break;
            }
        }

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

        var resolvingAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, CStructElement> declaration in this.CStructElements)
        {
            if (declaration.Value is Typedef)
            {
                _ = this.ResolveCompiledTypeReference(declaration.Key, namedTypes, compositeSymbols, resolvingAliases);
            }
        }

        foreach (KeyValuePair<CstructEnum, CompiledTypeSymbol> enm in enumSymbols)
        {
            CompiledTypeReference underlying = this.ResolveCompiledTypeReference(
                enm.Key.Type.Name,
                namedTypes,
                compositeSymbols,
                resolvingAliases);
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
                    members.ToImmutable()));
        }

        var compiledFields = ImmutableDictionary.CreateBuilder<Field, CompiledField>(
            ReferenceEqualityComparer.Instance);
        var compilingComposites = new HashSet<Struct>(ReferenceEqualityComparer.Instance);
        foreach (Struct declaration in compositeSymbols.Keys)
        {
            this.CompileComposite(
                declaration,
                compositeSymbols,
                namedTypes,
                resolvingAliases,
                compiledFields,
                compilingComposites);
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
            var field = new Field(
                new Identifier(type.TerminalName),
                declaration.Name,
                Field.NoArray,
                0,
                type.PointerDepth);
            Func<Stream, object>? reader = this.GetCompiledReader(type.Symbol);
            Action<Stream, object>? writer = this.GetCompiledWriter(type.Symbol);
            Func<Stream, object>? terminatedReader = null;
            Action<Stream, object>? terminatedWriter = null;
            if (type.PointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(field.Type))
            {
                string handler = CharacterFieldTypes.GetStringPointerHandlerKey(field.Type);
                terminatedReader = this.fieldHandlers[handler];
                terminatedWriter = this.writeHandlers[handler];
            }

            int alignment = type.PointerDepth > 0 ? this.PointerSize : type.Symbol.Alignment;
            int? elementSize = type.PointerDepth > 0 ? this.PointerSize : type.Symbol.FixedSize;
            var compiledRoot = new CompiledField(
                field,
                field,
                type,
                reader,
                writer,
                terminatedReader,
                terminatedWriter,
                alignment,
                elementSize,
                CompiledArrayShape.Scalar,
                elementSize,
                false,
                null,
                null,
                0,
                0);
            rootFields.Add(declaration, compiledRoot);
        }

        var publishedSymbols = new HashSet<CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);
        foreach (CompiledTypeSymbol symbol in namedTypes.Values.
                     Select(reference => reference.Symbol).
                     Concat(compositeSymbols.Values).
                     Concat(enumSymbols.Values))
        {
            if (!publishedSymbols.Add(symbol))
            {
                continue;
            }

            if (!symbol.IsBound)
            {
                throw new CStructLayoutException("Compiled type symbol was not bound: " + symbol.Name);
            }

            symbol.Freeze();
        }

        return new CompiledLayoutModel(
            this.cStructElements.ToImmutableDictionary(StringComparer.Ordinal),
            this.cStructElements.ToImmutableArray(),
            namedTypes.ToImmutableDictionary(StringComparer.Ordinal),
            compositeSymbols.ToImmutableDictionary(ReferenceEqualityComparer.Instance),
            compiledFields.ToImmutable(),
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

    /// <summary>Resolves one exported or primitive name once, accumulating pointer depth across typedef chains.</summary>
    private CompiledTypeReference ResolveCompiledTypeReference(
        string name,
        Dictionary<string, CompiledTypeReference> namedTypes,
        IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
        HashSet<string> resolvingAliases)
    {
        if (namedTypes.TryGetValue(name, out CompiledTypeReference known))
        {
            return known;
        }

        if (!this.CStructElements.TryGetValue(name, out CStructElement? declaration) ||
            declaration is not Typedef alias)
        {
            throw new CStructLayoutException("Unknown field type: " + name);
        }

        if (!resolvingAliases.Add(name))
        {
            throw new CStructLayoutException("Circular typedef dependency detected at: " + name);
        }

        try
        {
            CompiledTypeReference resolved;
            if (alias.Struct is not null)
            {
                resolved = new CompiledTypeReference(
                    compositeSymbols[alias.Struct],
                    0,
                    name);
            }
            else
            {
                CompiledTypeReference target = this.ResolveCompiledTypeReference(
                    alias.Type.Name,
                    namedTypes,
                    compositeSymbols,
                    resolvingAliases);
                resolved = new CompiledTypeReference(
                    target.Symbol,
                    checked(target.PointerDepth + alias.Type.PointerDepth),
                    target.TerminalName);
            }

            namedTypes.Add(name, resolved);
            return resolved;
        }
        finally
        {
            resolvingAliases.Remove(name);
        }
    }

    /// <summary>Compiles one composite's fields, fixed placements, bit slices, and size strategy exactly once.</summary>
    private CompiledCompositeType CompileComposite(
        Struct strct,
        IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
        Dictionary<string, CompiledTypeReference> namedTypes,
        HashSet<string> resolvingAliases,
        ImmutableDictionary<Field, CompiledField>.Builder compiledFields,
        HashSet<Struct> compiling)
    {
        CompiledTypeSymbol symbol = compositeSymbols[strct];
        if (symbol.Definition is CompiledCompositeType known)
        {
            return known;
        }

        if (!compiling.Add(strct))
        {
            throw new CStructLayoutException(
                "By-value recursive struct declarations are not supported: " + strct.Name.Name);
        }

        try
        {
            var fields = ImmutableArray.CreateBuilder<CompiledField>(strct.Fields.Count);
            foreach (Field field in strct.Fields)
            {
                CompiledTypeReference type = field is Struct inlineStruct
                                                 ? new CompiledTypeReference(
                                                     compositeSymbols[inlineStruct],
                                                     0,
                                                     inlineStruct.Name.Name)
                                                 : this.ResolveCompiledTypeReference(
                                                     field.Type.Name,
                                                     namedTypes,
                                                     compositeSymbols,
                                                     resolvingAliases);
                int pointerDepth = checked(field.PointerDepth + type.PointerDepth);
                if (pointerDepth == 0 && type.Symbol.Declaration is Struct nested)
                {
                    if (compiling.Contains(nested))
                    {
                        throw new CStructLayoutException(
                            "By-value recursive struct declarations are not supported: " + nested.Name.Name);
                    }

                    _ = this.CompileComposite(
                        nested,
                        compositeSymbols,
                        namedTypes,
                        resolvingAliases,
                        compiledFields,
                        compiling);
                }

                var effectiveField = new Field(
                    new Identifier(type.TerminalName),
                    field.Name,
                    field.ArrayCount,
                    field.BitSize,
                    pointerDepth);
                bool isUnsizedCharacterArray =
                    ReferenceEquals(effectiveField.ArrayCount, Field.UnknownArraysize) &&
                    CharacterFieldTypes.IsCharArrayField(effectiveField);
                if (ReferenceEquals(effectiveField.ArrayCount, Field.UnknownArraysize) &&
                    !isUnsizedCharacterArray)
                {
                    throw new CStructLayoutException(
                        "Only character fields can use an unsized array declarator: " + field.Name.Name);
                }

                Func<Stream, object>? reader = this.GetCompiledReader(type.Symbol);
                Action<Stream, object>? writer = this.GetCompiledWriter(type.Symbol);
                Func<Stream, object>? terminatedReader = null;
                Action<Stream, object>? terminatedWriter = null;
                if (isUnsizedCharacterArray || (pointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(effectiveField.Type)))
                {
                    string handler = CharacterFieldTypes.GetStringPointerHandlerKey(effectiveField.Type);
                    terminatedReader = this.fieldHandlers[handler];
                    terminatedWriter = this.writeHandlers[handler];
                }

                int alignment = pointerDepth > 0 ? this.PointerSize : type.Symbol.Alignment;
                int? elementSize = pointerDepth > 0 ? this.PointerSize : type.Symbol.FixedSize;
                CompiledArrayShape arrayShape = this.CompileArrayShape(effectiveField);
                int? storageSize = elementSize.HasValue && arrayShape.FixedCount.HasValue
                                       ? checked(elementSize.Value * arrayShape.FixedCount.Value)
                                       : null;
                BitfieldCodecTable.Entry? bitfieldStorage = null;
                if (field.BitSize > 0)
                {
                    try
                    {
                        bitfieldStorage = this.bitfieldCodecs.ValidateBitField(field);
                    }
                    catch (InvalidOperationException exception)
                    {
                        throw new CStructLayoutException(
                            "Invalid bitfield declaration: " + field.Name.Name,
                            exception);
                    }
                }

                var compiledField = new CompiledField(
                    field,
                    effectiveField,
                    type,
                    reader,
                    writer,
                    terminatedReader,
                    terminatedWriter,
                    alignment,
                    elementSize,
                    arrayShape,
                    storageSize,
                    isUnsizedCharacterArray,
                    bitfieldStorage?.ByteSize,
                    bitfieldStorage?.IsLittleEndian,
                    null,
                    0);
                fields.Add(compiledField);
            }

            int compositeAlignment = fields.Count == 0 ? 1 : fields.Max(field => field.Alignment);
            ImmutableArray<CompiledField> placedFields =
                this.PlaceCompiledFields(strct, fields.ToImmutable(), compositeAlignment, out int? fixedSize);
            symbol.CompleteLayout(compositeAlignment, fixedSize);
            var definition = new CompiledCompositeType(symbol, placedFields);
            symbol.Bind(definition);
            foreach (CompiledField field in placedFields)
            {
                compiledFields.Add(field.Declaration, field);
            }

            return definition;
        }
        finally
        {
            compiling.Remove(strct);
        }
    }

    /// <summary>Calculates immutable fixed offsets and bit offsets without making runtime-sized offsets look static.</summary>
    private ImmutableArray<CompiledField> PlaceCompiledFields(
        Struct strct,
        ImmutableArray<CompiledField> fields,
        int compositeAlignment,
        out int? fixedSize)
    {
        var result = ImmutableArray.CreateBuilder<CompiledField>(fields.Length);
        if (strct.IsUnion)
        {
            int? largest = 0;
            foreach (CompiledField field in fields)
            {
                if (!field.FixedStorageSize.HasValue)
                {
                    try
                    {
                        _ = this.GetCompiledFieldStorageSize(field, this.staticLayoutVariables, true);
                    }
                    catch (CStructLayoutException exception)
                    {
                        throw new CStructLayoutException(
                            $"Union member '{field.Declaration.Name.Name}' must have fixed storage.",
                            exception);
                    }
                }

                result.Add(field.WithPlacement(0, 0));
                largest = largest.HasValue && field.FixedStorageSize.HasValue
                              ? Math.Max(largest.Value, field.FixedStorageSize.Value)
                              : null;
            }

            if (largest.HasValue && this.Aligned)
            {
                largest = LayoutMath.AlignUp(largest.Value, compositeAlignment);
            }

            fixedSize = largest;
            return result.ToImmutable();
        }

        int? current = 0;
        int? activeBitUnitStart = null;
        int activeBitUnitSize = 0;
        int activeBitUnitBitsUsed = 0;
        int activeBitUnitAlignment = 0;
        string? activeBitUnitType = null;
        foreach (CompiledField field in fields)
        {
            if (field.BitStorageSize.HasValue)
            {
                int unitSize = field.BitStorageSize.Value;
                bool startsNew = LayoutMath.StartsNewBitfieldUnit(
                    activeBitUnitType,
                    activeBitUnitSize,
                    activeBitUnitAlignment,
                    activeBitUnitBitsUsed,
                    field.EffectiveField,
                    unitSize,
                    field.Alignment);
                if (startsNew)
                {
                    if (current.HasValue && this.Aligned)
                    {
                        current = LayoutMath.AlignUp(current.Value, field.Alignment);
                    }

                    activeBitUnitStart = current;
                    current = current.HasValue ? checked(current.Value + unitSize) : null;
                    activeBitUnitSize = unitSize;
                    activeBitUnitAlignment = field.Alignment;
                    activeBitUnitType = field.EffectiveField.Type.Name;
                    activeBitUnitBitsUsed = 0;
                }

                result.Add(field.WithPlacement(activeBitUnitStart, activeBitUnitBitsUsed));
                activeBitUnitBitsUsed += field.EffectiveField.BitSize;
                continue;
            }

            activeBitUnitStart = null;
            activeBitUnitSize = 0;
            activeBitUnitBitsUsed = 0;
            activeBitUnitAlignment = 0;
            activeBitUnitType = null;
            if (current.HasValue && this.Aligned)
            {
                current = LayoutMath.AlignUp(current.Value, field.Alignment);
            }

            int? offset = current;
            current = current.HasValue && field.FixedStorageSize.HasValue
                          ? checked(current.Value + field.FixedStorageSize.Value)
                          : null;
            result.Add(field.WithPlacement(offset, 0));
        }

        if (current.HasValue && this.Aligned)
        {
            current = LayoutMath.AlignUp(current.Value, compositeAlignment);
        }

        fixedSize = current;
        return result.ToImmutable();
    }

    /// <summary>Returns the immutable composite descriptor for an exact parsed struct declaration.</summary>
    private CompiledCompositeType GetCompiledComposite(Struct strct)
    {
        CompiledTypeSymbol symbol = this.compiledLayout.Composites[strct];
        return symbol.Definition as CompiledCompositeType ??
               throw new InvalidOperationException("Composite type is not bound: " + strct.Name.Name);
    }

    /// <summary>Returns a primitive reader directly or the compiled underlying reader for an enum.</summary>
    private Func<Stream, object>? GetCompiledReader(CompiledTypeSymbol symbol)
    {
        return symbol.Kind switch
        {
            CompiledTypeKind.Primitive => symbol.Reader,
            CompiledTypeKind.Enum when symbol.Definition is CompiledEnumType enm => enm.Underlying.Symbol.Reader,
            _ => null,
        };
    }

    /// <summary>Returns a primitive writer directly or the compiled underlying writer for an enum.</summary>
    private Action<Stream, object>? GetCompiledWriter(CompiledTypeSymbol symbol)
    {
        return symbol.Kind switch
        {
            CompiledTypeKind.Primitive => symbol.Writer,
            CompiledTypeKind.Enum when symbol.Definition is CompiledEnumType enm => enm.Underlying.Symbol.Writer,
            _ => null,
        };
    }

    /// <summary>Compiles one scalar, fixed, runtime-counted, or flexible array strategy.</summary>
    private CompiledArrayShape CompileArrayShape(Field field)
    {
        if (ReferenceEquals(field.ArrayCount, Field.NoArray))
        {
            return CompiledArrayShape.Scalar;
        }

        if (ReferenceEquals(field.ArrayCount, Field.UnknownArraysize))
        {
            return new CompiledArrayShape(
                CompiledArrayKind.Flexible,
                field.ArrayCount,
                null,
                ImmutableArray<string>.Empty);
        }

        ImmutableArray<string> dependencies = this.expressionEvaluator.GetDependencies(field.ArrayCount).
            OrderBy(name => name, StringComparer.Ordinal).
            ToImmutableArray();
        if (dependencies.Length != 0)
        {
            return new CompiledArrayShape(
                CompiledArrayKind.Runtime,
                field.ArrayCount,
                null,
                dependencies);
        }

        int count = this.layoutExpressionEvaluator.Evaluate(
            field.ArrayCount,
            this.staticLayoutVariables,
            "array length for " + field.Name.Name);
        return new CompiledArrayShape(
            CompiledArrayKind.Fixed,
            field.ArrayCount,
            count,
            ImmutableArray<string>.Empty);
    }

    /// <summary>Calculates one composite extent from compiled field/type facts and runtime count expressions only.</summary>
    private int GetCompiledStructSizeInBytes(
        CompiledCompositeType composite,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (composite.Fields.Length == 0)
        {
            return 0;
        }

        if (composite.Symbol.Kind == CompiledTypeKind.Union)
        {
            int largest = 0;
            foreach (CompiledField field in composite.Fields)
            {
                largest = Math.Max(
                    largest,
                    this.GetCompiledFieldStorageSize(field, variables, requireFixedSize));
            }

            return this.Aligned ? LayoutMath.AlignUp(largest, composite.Symbol.Alignment) : largest;
        }

        int current = 0;
        int activeBitUnitSize = 0;
        int activeBitUnitBitsUsed = 0;
        int activeBitUnitAlignment = 0;
        string? activeBitUnitType = null;
        foreach (CompiledField field in composite.Fields)
        {
            if (field.BitStorageSize.HasValue)
            {
                int unitSize = field.BitStorageSize.Value;
                bool startsNew = LayoutMath.StartsNewBitfieldUnit(
                    activeBitUnitType,
                    activeBitUnitSize,
                    activeBitUnitAlignment,
                    activeBitUnitBitsUsed,
                    field.EffectiveField,
                    unitSize,
                    field.Alignment);
                if (startsNew)
                {
                    current = this.Aligned ? LayoutMath.AlignUp(current, field.Alignment) : current;
                    current = checked(current + unitSize);
                    activeBitUnitSize = unitSize;
                    activeBitUnitAlignment = field.Alignment;
                    activeBitUnitType = field.EffectiveField.Type.Name;
                    activeBitUnitBitsUsed = 0;
                }

                activeBitUnitBitsUsed += field.EffectiveField.BitSize;
                continue;
            }

            activeBitUnitSize = 0;
            activeBitUnitBitsUsed = 0;
            activeBitUnitAlignment = 0;
            activeBitUnitType = null;
            current = this.Aligned ? LayoutMath.AlignUp(current, field.Alignment) : current;
            current = checked(
                current + this.GetCompiledFieldStorageSize(field, variables, requireFixedSize));
        }

        return this.Aligned ? LayoutMath.AlignUp(current, composite.Symbol.Alignment) : current;
    }

    /// <summary>Calculates one compiled field's complete storage without resolving its parsed type name.</summary>
    private int GetCompiledFieldStorageSize(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        int elementSize = this.GetCompiledFieldElementSize(field, variables, requireFixedSize);
        int count = this.GetCompiledArrayCount(field, variables, requireFixedSize);
        return checked(elementSize * count);
    }

    /// <summary>Calculates one compiled element footprint from its direct pointer, codec, enum, or composite target.</summary>
    private int GetCompiledFieldElementSize(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (field.FixedElementSize.HasValue)
        {
            return field.FixedElementSize.Value;
        }

        if (field.Type.Symbol.Declaration is Struct nested)
        {
            return this.GetCompiledStructSizeInBytes(
                this.GetCompiledComposite(nested),
                variables,
                requireFixedSize);
        }

        throw new CStructLayoutException(
            "Variable-length type has no fixed storage size: " + field.EffectiveField.Type.Name);
    }

    /// <summary>Evaluates one compiled array strategy while preserving fixed/flexible error semantics.</summary>
    private int GetCompiledArrayCount(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 1;
        }

        if (field.Array.Kind == CompiledArrayKind.Flexible)
        {
            throw new CStructLayoutException(
                "Flexible array has no fixed storage size: " + field.EffectiveField.Name.Name);
        }

        int count;
        try
        {
            Expr expression = field.Array.CountExpression ??
                              throw new InvalidOperationException(
                                  "Compiled array strategy has no count expression: " +
                                  field.EffectiveField.Name.Name);
            count = this.layoutExpressionEvaluator.Evaluate(
                expression,
                variables,
                "array length for " + field.EffectiveField.Name.Name);
        }
        catch (Exception exception) when (requireFixedSize)
        {
            throw new CStructLayoutException(
                "Cannot calculate fixed array size for field: " + field.EffectiveField.Name.Name,
                exception);
        }

        if (count < 0)
        {
            throw new CStructLayoutException(
                "Array length cannot be negative: " + field.EffectiveField.Name.Name);
        }

        return count;
    }
}
