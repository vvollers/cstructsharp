namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using CStructSharp;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Introspection;
using CStructSharp.Parsing;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>The composite stage: each struct and union's fields, their array shapes and <c>sizeof</c>/<c>offsetof</c> folding, and their placement.</summary>
internal sealed partial class LayoutCompilation
{
    /// <summary>Compiles the array strategy of a synthetic root field spelled in a path (<c>uint16[EOF]</c>).</summary>
    /// <param name="field">The parsed root field.</param>
    /// <returns>The array shape.</returns>
    /// <exception cref="CStructLayoutException">The count expression is invalid.</exception>
    internal CompiledArrayShape CompileRootArrayShape(Field field)
    {
        return this.CompileArrayShape(field);
    }

    /// <summary>Converts a placement position to a build-time offset, which must fit an Int32; an unknown position stays unknown.</summary>
    /// <param name="position">The position, or <see langword="null"/>.</param>
    /// <returns>The offset, or <see langword="null"/>.</returns>
    /// <exception cref="OverflowException">The position exceeds the Int32 range.</exception>
    private static int? ToOffset(long? position) => position is long value ? checked((int)value) : null;

    /// <summary>
    ///     Records on every bitfield the bit length of its run of adjacent positive-width bitfields. A zero-width
    ///     separator ends the run; placement aligns its successor before starting the next independent run.
    ///     The value depends only on the run's declarations, so the runtime cursor can clamp packed units without
    ///     looking ahead.
    /// </summary>
    /// <param name="fields">The composite's compiled fields in declaration order.</param>
    private static void MeasureBitfieldRuns(ImmutableArray<CompiledField> fields)
    {
        int index = 0;
        while (index < fields.Length)
        {
            if (!fields[index].BitStorageSize.HasValue || fields[index].IsZeroWidthBitfield || fields[index].IsConditional)
            {
                index++;
                continue;
            }

            int start = index;
            long bits = 0;
            while (index < fields.Length && fields[index].BitStorageSize.HasValue && !fields[index].IsZeroWidthBitfield && !fields[index].IsConditional)
            {
                // Separator padding belongs to placement, not to either neighboring run's storage window.
                CompiledField field = fields[index];
                bits += field.BitSize;
                index++;
            }

            for (int member = start; member < index; member++)
            {
                fields[member].BitRunBits = checked((int)bits);
            }
        }
    }

    /// <summary>
    ///     Replaces every <c>sizeof(T)</c> and <c>offsetof(T, f)</c> in an expression with its literal value. T may be
    ///     a primitive, typedef, enum, or composite (compiled on demand, so it must be complete and fixed-size); a
    ///     pointer type has the pointer width.
    /// </summary>
    private Expr FoldCalls(
        Expr expression,
        string fieldName,
        CompositeCompilationContext context)
    {
        switch (expression)
        {
        case Call call:
            {
                if (call.Expr is not Identifier { Name: "sizeof" or "offsetof", } function)
                {
                    throw new CStructLayoutException(
                        $"Only sizeof(type) and offsetof(type, field) are supported as expression calls: {fieldName}");
                }

                bool isSizeof = function.Name == "sizeof";
                if (call.Arguments.Length != (isSizeof ? 1 : 2) || call.Arguments.Any(argument => argument is not Identifier))
                {
                    throw new CStructLayoutException(
                        $"{function.Name} expects {(isSizeof ? "one type name" : "a type name and a field name")}: {fieldName}");
                }

                var typeName = (Identifier)call.Arguments[0];
                CompiledTypeReference target = this.ResolveCompiledTypeReference(typeName.Name, context);
                int totalPointerDepth = target.PointerDepth + typeName.PointerDepth;
                if (isSizeof)
                {
                    if (totalPointerDepth > 0)
                    {
                        return new Literal(this.PointerSize);
                    }

                    if (target.Symbol.Declaration is Struct sized)
                    {
                        _ = this.CompileComposite(sized, context);
                        return new Literal(
                            context.SizeQueries.GetCompiledStructSizeInBytes(context.SizeQueries.GetCompiledComposite(sized), this.staticLayoutVariables, true));
                    }

                    return new Literal(
                        target.Symbol.FixedSize ??
                        throw new CStructLayoutException($"sizeof({typeName.Name}) has no fixed size: {fieldName}"));
                }

                if (totalPointerDepth > 0 || target.Symbol.Declaration is not Struct composite)
                {
                    throw new CStructLayoutException($"offsetof needs a struct or union type: {fieldName}");
                }

                CompiledCompositeType compiled = this.CompileComposite(composite, context);
                string memberName = ((Identifier)call.Arguments[1]).Name;
                CompiledField? member = compiled.Fields.FirstOrDefault(item => item.Declaration.Name.Name == memberName);
                if (member is null)
                {
                    throw new CStructLayoutException($"offsetof: '{composite.Name.Name}' has no field named '{memberName}': {fieldName}");
                }

                return new Literal(
                    member.FixedOffset ??
                    throw new CStructLayoutException($"offsetof({composite.Name.Name}, {memberName}) is not statically placed: {fieldName}"));
            }

        case UnaryOp unary:
            return new UnaryOp(unary.Type, this.FoldCalls(unary.Expr, fieldName, context));
        case BinaryOp binary:
            return new BinaryOp(
                binary.Type,
                this.FoldCalls(binary.Left, fieldName, context),
                this.FoldCalls(binary.Right, fieldName, context));
        case ConditionalExpr conditional:
            return new ConditionalExpr(
                this.FoldCalls(conditional.Condition, fieldName, context),
                this.FoldCalls(conditional.WhenTrue, fieldName, context),
                this.FoldCalls(conditional.WhenFalse, fieldName, context));
        default:
            return expression;
        }
    }

    /// <summary>Compiles one composite's fields, fixed placements, bit slices, and size strategy exactly once.</summary>
    private CompiledCompositeType CompileComposite(
        Struct strct,
        CompositeCompilationContext context)
    {
        CompiledTypeSymbol symbol = context.CompositeSymbols[strct];
        if (symbol.Definition is CompiledCompositeType known)
        {
            return known;
        }

        if (!context.Compiling.Add(strct))
        {
            throw new CStructLayoutException(
                "By-value recursive struct declarations are not supported: " + strct.Name.Name)
            {
                SourceOffset = strct.Name.SourceOffset,
            };
        }

        try
        {
            int? compositeAlignmentOverride = null;
            if (strct.CompositeAlignmentOverrideExpression is not null)
            {
                int explicitCompositeAlignment = this.layoutExpressionEvaluator.Evaluate(
                    strct.CompositeAlignmentOverrideExpression,
                    this.staticLayoutVariables,
                    "alignment override for " + strct.Name.Name);
                compositeAlignmentOverride = LayoutMath.ValidateExplicitAlignment(explicitCompositeAlignment, strct.Name.Name);
            }

            var fields = ImmutableArray.CreateBuilder<CompiledField>(strct.Fields.Count);
            foreach (Field field in strct.Fields)
            {
                CompiledTypeReference type = field is Struct inlineStruct
                                                 ? new CompiledTypeReference(
                                                     context.CompositeSymbols[inlineStruct],
                                                     0,
                                                     inlineStruct.Name.Name)
                                                 : this.ResolveFieldTypeReference(field, strct, context);
                CheckTypeKeyword(field.TypeKeywordHint, type, $"Field '{field.Name.Name}'", field.Type);

                int pointerDepth = checked(field.PointerDepth + type.PointerDepth);
                if (pointerDepth == 0 && type.TerminalName == "void")
                {
                    throw new CStructLayoutException(
                        "void has no storage of its own; declare a pointer to it: " + field.Name.Name);
                }

                if (pointerDepth == 0 && type.Symbol.Declaration is Struct nested)
                {
                    if (context.Compiling.Contains(nested))
                    {
                        throw new CStructLayoutException(
                            "By-value recursive struct declarations are not supported: " + nested.Name.Name)
                        {
                            SourceOffset = field.Name.SourceOffset,
                        };
                    }

                    _ = this.CompileComposite(
                        nested, context);
                }

                IReadOnlyList<Expr> arrayCount = field.ArrayCount;
                if (arrayCount.Count > 0 && arrayCount.Any(ExpressionEvaluator.ContainsCall))
                {
                    // sizeof(T) / offsetof(T, f) fold to literals now that T can be compiled on demand.
                    var folded = new Expr[arrayCount.Count];
                    for (int index = 0; index < folded.Length; index++)
                    {
                        folded[index] = ReferenceEquals(arrayCount[index], Field.UnknownArraysize)
                                            ? arrayCount[index]
                                            : this.FoldCalls(arrayCount[index], field.Name.Name, context);
                        if (!ReferenceEquals(folded[index], Field.UnknownArraysize))
                        {
                            this.expressionEvaluator.Compile(folded[index]);
                        }
                    }

                    arrayCount = folded;
                }

                var effectiveField = new Field(
                    new Identifier(type.TerminalName),
                    field.Name,
                    arrayCount,
                    Field.Width(field.BitSize, field.HasBitfieldDeclarator),
                    pointerDepth);

                // An unsized dimension can only ever be the sole entry of a one-dimensional
                // ArrayCount list - the grammar already rejects it as an inner dimension of a multidimensional
                // field, so a still-empty check here is simply never true for N >= 2.
                bool isUnsizedArray = effectiveField.ArrayCount.Count == 1 &&
                                       ReferenceEquals(effectiveField.ArrayCount[0], Field.UnknownArraysize);
                if (BoundedTextCodec.IsType(effectiveField.Type.Name) && effectiveField.ArrayCount.Count > 1)
                {
                    throw new CStructLayoutException(
                        "Encoded text buffers support one byte-length dimension; use an array of structs for multiple strings: " + field.Name.Name);
                }

                bool isUnsizedCharacterArray = isUnsizedArray && CharacterFieldTypes.IsCharArrayField(effectiveField);

                int codecId = GetCompiledCodecId(type.Symbol);
                int terminatedCodecId = PrimitiveCatalog.NoCodec;
                if (isUnsizedCharacterArray || (pointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(effectiveField.Type)))
                {
                    terminatedCodecId = this.catalog.CodecIdOf(CharacterFieldTypes.GetStringPointerHandlerKey(effectiveField.Type));
                }

                int alignment = pointerDepth > 0 ? this.PointerSize : type.Symbol.Alignment;
                if (field.AlignmentOverrideExpression is not null)
                {
                    int explicitAlignment = this.layoutExpressionEvaluator.Evaluate(
                        field.AlignmentOverrideExpression,
                        this.staticLayoutVariables,
                        "alignment override for " + field.Name.Name);
                    alignment = LayoutMath.ValidateExplicitAlignment(explicitAlignment, field.Name.Name);
                }
                else if (compositeAlignmentOverride.HasValue)
                {
                    // The composite's own @align(N) clamps a field relying on its natural alignment, matching
                    // #pragma pack(N) semantics; a field's own explicit override always wins outright instead.
                    alignment = Math.Min(alignment, compositeAlignmentOverride.Value);
                }

                int? elementSize = pointerDepth > 0 ? this.PointerSize : type.Symbol.FixedSize;
                CompiledArrayShape arrayShape = this.CompileArrayShape(effectiveField);
                if (arrayShape.Kind is CompiledArrayKind.Terminated or CompiledArrayKind.ToEnd &&
                    (!elementSize.HasValue || (pointerDepth == 0 && BoundedTextCodec.IsType(type.Symbol.Name))))
                {
                    // The count comes from the data, so every element must have one fixed size to step by; a bounded
                    // text codec sizes its buffer by characters, not elements, so it keeps needing an explicit capacity.
                    throw new CStructLayoutException(
                        "A data-sized array needs a fixed-size, non-text element type: " + field.Name.Name);
                }

                CompiledArrayShape? pointerElements = this.CompilePointerCount(field, effectiveField, type, pointerDepth, arrayShape);

                int? storageSize = elementSize.HasValue && arrayShape.TotalFixedElementCount.HasValue
                                       ? checked(elementSize.Value * arrayShape.TotalFixedElementCount.Value)
                                       : null;
                if (field.Name.Name.Length == 0 && field.BitSize == 0 && field is not Struct &&
                    (!storageSize.HasValue || type.Symbol.Kind is CompiledTypeKind.Struct or CompiledTypeKind.Union))
                {
                    // A `_` padding field is written as zeroes without a caller value, so it needs a fixed
                    // primitive shape to know how many.
                    throw new CStructLayoutException(
                        "A padding field (`_`) needs a fixed-size primitive type in: " + strct.Name.Name);
                }

                BitfieldCodecTable.Entry? bitfieldStorage = null;
                bool zeroWidthBitfield = effectiveField.HasBitfieldDeclarator && field.BitSize == 0;
                if (field.BitSize > 0 || zeroWidthBitfield)
                {
                    try
                    {
                        // Validated against the resolved type so an alias or typedef of an integer codec is
                        // acceptable storage, exactly as in C; an enum or flag stores its bits in its backing type.
                        // A zero-width separator is validated as a one-bit field of its type: only the unit size matters.
                        Field storageField = type.Symbol.Definition is CompiledEnumType enumStorage && pointerDepth == 0
                                                 ? new Field(new Identifier(enumStorage.Underlying.TerminalName), field.Name, field.ArrayCount, Field.Width(Math.Max(field.BitSize, 1)), 0)
                                                 : zeroWidthBitfield
                                                     ? new Field(effectiveField.Type, field.Name, field.ArrayCount, Field.Width(1), 0)
                                                     : effectiveField;
                        bitfieldStorage = this.bitfieldCodecs.ValidateBitField(storageField);
                    }
                    catch (InvalidOperationException exception)
                    {
                        throw new CStructLayoutException(
                            "Invalid bitfield declaration: " + field.Name.Name,
                            exception);
                    }

                    if (field.OffsetAssertionExpression is not null)
                    {
                        throw new CStructLayoutException(
                            "An explicit offset assertion is not supported on a bitfield declarator: " + field.Name.Name);
                    }
                }

                // Like @align(N), @N is a constant: it is evaluated once, with the #define constants only.
                int? assertedOffset = field.OffsetAssertionExpression is null
                                          ? null
                                          : OffsetAssertion.Validate(
                                              this.layoutExpressionEvaluator.Evaluate(
                                                  field.OffsetAssertionExpression,
                                                  this.staticLayoutVariables,
                                                  "offset assertion for " + field.Name.Name),
                                              field.Name.Name);

                var compiledField = new CompiledField(
                    field,
                    effectiveField,
                    type,
                    codecId,
                    terminatedCodecId,
                    alignment,
                    elementSize,
                    arrayShape,
                    storageSize,
                    isUnsizedCharacterArray,
                    bitfieldStorage?.ByteSize,
                    bitfieldStorage?.IsLittleEndian,
                    null,
                    0,
                    this.IsLittleEndian)
                {
                    PointerElements = pointerElements,
                    AssertedOffset = assertedOffset,
                };
                fields.Add(compiledField);
            }

            // A SysV zero-width separator moves bits without joining the alignment computation (x86-64 psABI);
            // MSVC lets its type's unit boundary count.
            int compositeAlignment = fields.Count == 0
                                         ? 1
                                         : fields.Max(field => field.IsZeroWidthBitfield && this.BitfieldPacking == BitfieldPacking.SysV ? 1 : field.Alignment);
            ImmutableArray<CompiledField> placedFields =
                this.PlaceCompiledFields(strct, fields.ToImmutable(), compositeAlignment, context.SizeQueries, out int? fixedSize);
            symbol.CompleteLayout(compositeAlignment, fixedSize);
            var definition = new CompiledCompositeType(symbol, placedFields);
            symbol.Bind(definition);
            return definition;
        }
        finally
        {
            context.Compiling.Remove(strct);
        }
    }

    /// <summary>
    ///     Calculates immutable fixed offsets and bit offsets without making runtime-sized offsets look static.
    /// </summary>
    /// <remarks>
    ///     Uses the same <see cref="PlacementCursor"/> as the runtime, from offset 0; its position becomes unknown after
    ///     the first conditional or runtime-sized field. A field with a known offset has its <c>@N</c> assertion checked
    ///     here, and a known <see cref="CompiledField.FixedOffset"/> tells the runtime cursor and generated code that
    ///     the check is done; any other asserted field is checked where an operation places it.
    /// </remarks>
    private ImmutableArray<CompiledField> PlaceCompiledFields(
        Struct strct,
        ImmutableArray<CompiledField> fields,
        int compositeAlignment,
        CompiledSizeQueries sizeQueries,
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
                        _ = sizeQueries.GetCompiledFieldStorageSize(field, this.staticLayoutVariables, true);
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

            fixedSize = largest.HasValue ? ToOffset(PlacementCursor.UnionEnd(largest.Value, compositeAlignment, this.Aligned)) : null;
            return result.ToImmutable();
        }

        MeasureBitfieldRuns(fields);
        var cursor = new PlacementCursor(0, this.Aligned, this.BitfieldPacking, this.highBitFirst);
        foreach (CompiledField field in fields)
        {
            if (field.IsConditional)
            {
                cursor.CompleteField(null);
                if (field.BitStorageSize.HasValue)
                {
                    throw new CStructLayoutException("Place conditional bitfields inside a named struct group.");
                }
            }

            if (field.IsZeroWidthBitfield)
            {
                // A separator has no storage; it only moves the bit position for the next bitfield.
                result.Add(field.WithPlacement(ToOffset(cursor.AdvanceToSeparator(field.BitStorageSize ?? 1, field.Alignment, field.BitRunBits)), 0));
                continue;
            }

            if (field.BitStorageSize.HasValue)
            {
                // Bit runs never span a variable-length field, so an unknown position means the run is unreachable
                // statically; the runtime cursor places it.
                (long UnitStart, int UnitSize, int BitOffset)? unit = cursor.AdvanceToBitfield(field.BitStorageSize.Value, field.Alignment, field.BitSize, field.BitRunBits, field.BitStorageIsLittleEndian ?? true, field.Declaration.Name.Name);
                result.Add(unit is { } placed ? field.WithPlacement(ToOffset(placed.UnitStart), placed.BitOffset, placed.UnitSize) : field.WithPlacement(null, 0));
                continue;
            }

            int? offset = ToOffset(cursor.AdvanceToField(field.Alignment));
            if (offset is int known && field.AssertedOffset is int asserted &&
                cursor.CheckAssertedOffset(known, asserted, field.Declaration.Name.Name) is { } failure)
            {
                throw new CStructLayoutException(failure);
            }

            cursor.CompleteField(offset.HasValue && field.FixedStorageSize.HasValue
                                     ? checked(offset.Value + field.FixedStorageSize.Value)
                                     : null);
            result.Add(field.WithPlacement(offset, 0));
        }

        fixedSize = ToOffset(cursor.Finish(compositeAlignment));
        return result.ToImmutable();
    }

    /// <summary>
    ///     Compiles a pointer declarator's <c>@count(N)</c> into the one-dimensional array shape of its final target:
    ///     <c>uint8 *iv @count(iv_len)</c> points at <c>iv_len</c> consecutive <c>uint8</c> values.
    /// </summary>
    /// <param name="field">The declared field, carrying the count expression.</param>
    /// <param name="effectiveField">The field after typedef resolution, which knows the real pointer depth.</param>
    /// <param name="type">The resolved target type.</param>
    /// <param name="pointerDepth">The resolved pointer depth; a typedef such as <c>CK_BYTE_PTR</c> contributes to it.</param>
    /// <param name="arrayShape">The declarator's own array shape, which must be scalar.</param>
    /// <returns>The target array shape, or <see langword="null"/> when the declarator has no <c>@count</c>.</returns>
    /// <exception cref="CStructLayoutException">The count is on a non-pointer, an array of pointers, or a <c>void</c> target, or it is not a plain count.</exception>
    private CompiledArrayShape? CompilePointerCount(Field field, Field effectiveField, CompiledTypeReference type, int pointerDepth, CompiledArrayShape arrayShape)
    {
        if (field.PointerCountExpression is not { } count)
        {
            return null;
        }

        string name = field.Name.Name;
        if (pointerDepth == 0)
        {
            throw new CStructLayoutException("@count applies only to a pointer declarator: " + name);
        }

        if (arrayShape.Kind != CompiledArrayKind.Scalar)
        {
            throw new CStructLayoutException("@count cannot be combined with an array of pointers: " + name);
        }

        if (type.TerminalName == "void")
        {
            throw new CStructLayoutException("@count needs a typed target; a void pointer has no element type: " + name);
        }

        if (ReferenceEquals(count, Field.UnknownArraysize) || count is Identifier { Name: "EOF", })
        {
            throw new CStructLayoutException("@count needs an element count expression: " + name);
        }

        // The target is compiled like a one-dimensional array declarator `T name[N]`, so a literal count is fixed and
        // a named count is evaluated from the operation's variables when the pointer is followed.
        this.expressionEvaluator.Compile(count);
        var targetArray = new Field(effectiveField.Type, field.Name, [count,], NoneExpr.Instance, 0);
        return this.CompileSingleArrayDimension(targetArray, count);
    }

    /// <summary>Compiles one scalar, fixed, runtime-counted, or flexible array strategy.</summary>
    private CompiledArrayShape CompileArrayShape(Field field)
    {
        if (ReferenceEquals(field.ArrayCount, Field.NoArray))
        {
            return CompiledArrayShape.Scalar;
        }

        if (field.ArrayCount.Count == 1)
        {
            return this.CompileSingleArrayDimension(field, field.ArrayCount[0]);
        }

        // A multidimensional array has fixed dimensions only: a data-dependent count is supported for a
        // one-dimensional array, where the element layout does not depend on it.
        var dimensions = ImmutableArray.CreateBuilder<CompiledArrayDimension>(field.ArrayCount.Count);
        foreach (Expr dimensionExpression in field.ArrayCount)
        {
            ImmutableArray<string> dependencies =
                this.expressionEvaluator.GetDependencies(dimensionExpression).ToImmutableArray();
            if (dependencies.Length != 0)
            {
                throw new CStructLayoutException(
                    "Every dimension of a multidimensional array must be a compile-time-fixed count; a " +
                    "runtime-sized dimension is supported only in a one-dimensional array: " + field.Name.Name);
            }

            int dimensionCount = this.layoutExpressionEvaluator.Evaluate(
                dimensionExpression,
                this.staticLayoutVariables,
                "array length for " + field.Name.Name);
            dimensions.Add(new CompiledArrayDimension(dimensionExpression, dimensionCount));
        }

        ImmutableArray<CompiledArrayDimension> dimensionList = dimensions.MoveToImmutable();
        return new CompiledArrayShape(
            CompiledArrayKind.Fixed,
            ImmutableArray<string>.Empty,
            dimensionList);
    }

    /// <summary>Compiles the sole dimension of a one-dimensional array field - unchanged from the single-dimension days.</summary>
    private CompiledArrayShape CompileSingleArrayDimension(Field field, Expr dimensionExpression)
    {
        if (ReferenceEquals(dimensionExpression, Field.UnknownArraysize))
        {
            // `char name[]` is a terminated string; `T values[]` on any other type is an array terminated by an
            // all-zero element (C's flexible member has no extent of its own, dissect's convention gives it one).
            return new CompiledArrayShape(
                CharacterFieldTypes.IsCharArrayField(field) ? CompiledArrayKind.Flexible : CompiledArrayKind.Terminated,
                ImmutableArray<string>.Empty,
                ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, null)));
        }

        if (dimensionExpression is Identifier { Name: "EOF", } && !this.staticLayoutVariables.ContainsKey("EOF"))
        {
            // `T values[EOF]`: every whole element to the end of the input. A layout that defines EOF itself keeps
            // the ordinary count meaning.
            return new CompiledArrayShape(
                CompiledArrayKind.ToEnd,
                ImmutableArray<string>.Empty,
                ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, null)));
        }

        ImmutableArray<string> dependencies = this.expressionEvaluator.GetDependencies(dimensionExpression).
            OrderBy(name => name, StringComparer.Ordinal).
            ToImmutableArray();
        if (dependencies.Length != 0)
        {
            return new CompiledArrayShape(
                CompiledArrayKind.Runtime,
                dependencies,
                ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, null)));
        }

        int count = this.layoutExpressionEvaluator.Evaluate(
            dimensionExpression,
            this.staticLayoutVariables,
            "array length for " + field.Name.Name);
        return new CompiledArrayShape(
            CompiledArrayKind.Fixed,
            ImmutableArray<string>.Empty,
            ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, count)));
    }
}
