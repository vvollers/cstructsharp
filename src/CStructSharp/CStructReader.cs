namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Contains the stream-reading half of <see cref="CStruct"/>.
///     These methods turn compiled layout elements into nested <see cref="StructValue"/> values while keeping pointer and debug state together.
/// </summary>
public partial class CStruct
{
    /// <summary>
    ///     Groups a flat, row-major list of leaf values into nested lists matching every dimension but the
    ///     outermost one, which the caller's own loop already accounted for by producing this flat list in the
    ///     first place. Each pass groups the previous level by one dimension's size, from the innermost dimension
    ///     outward - the same grouping a single-dimension array already performs once, repeated once per
    ///     additional dimension.
    /// </summary>
    private static List<object?> ReshapeFlatArrayValues(List<object?> flatValues, IReadOnlyList<int> dimensionSizes)
    {
        List<object?> currentLevel = flatValues;
        for (int dimensionIndex = dimensionSizes.Count - 1; dimensionIndex >= 1; dimensionIndex--)
        {
            int groupSize = dimensionSizes[dimensionIndex];
            var nextLevel = new List<object?>(currentLevel.Count / groupSize);
            for (int start = 0; start < currentLevel.Count; start += groupSize)
            {
                nextLevel.Add(currentLevel.GetRange(start, groupSize));
            }

            currentLevel = nextLevel;
        }

        return currentLevel;
    }

    /// <summary>Moves a nested struct to the same compiled parent boundary used by size, write, and address operations.</summary>
    private void PrepareNestedStructStart(
        CompiledCompositeType strct,
        CStructOperationContext state,
        long unionPosition)
    {
        if (state.CurrentBitOffset > 0)
        {
            // A nested object cannot share the unfinished primitive storage unit of a preceding bitfield.
            state.Stream.Position = state.NextPosition;
            state.CurrentBitOffset = 0;
            state.CurrentBitfieldType = null;
        }

        if (!state.Aligned || unionPosition != -1)
        {
            return;
        }

        state.Stream.Position = LayoutMath.AlignUp(state.Stream.Position, strct.Symbol.Alignment);
    }

    /// <summary>
    ///     Reads one struct or union member (a root, a named nested composite, or an inline body) into
    ///     <paramref name="currentContainer"/>: a named composite becomes a nested value, an anonymous one is
    ///     promoted into the parent, and a union is decoded through <see cref="ReadUnionValue"/>.
    /// </summary>
    private void ReadCompositeMember(
        CompiledCompositeType composite,
        string name,
        StructValue currentContainer,
        CStructOperationContext state,
        DebugPath? debugStack,
        long unionPosition,
        bool alignInlineStructStart,
        CompiledField? fieldDescriptor,
        CompositeFieldPlacementCursor? cursor)
    {
        bool usesCursor = cursor is not null && unionPosition == -1;
        if (unionPosition != -1)
        {
            // An inline composite member of a union starts at the union's address like every member.
            state.Stream.Position = unionPosition;
            state.CurrentBitOffset = 0;
            state.CurrentBitfieldType = null;
        }

        if (alignInlineStructStart)
        {
            // Inline structs arrive here as Struct instances rather than ordinary Field instances. Prepare
            // their parent boundary explicitly so they follow the same bitfield and alignment rule as a
            // named struct field.
            if (usesCursor && fieldDescriptor is not null)
            {
                (long inlineFieldStart, _, _) = cursor!.AdvanceToField(fieldDescriptor);
                this.ValidateOffsetAssertionAtRuntime(fieldDescriptor, inlineFieldStart, state.Variables);
                state.Stream.Position = inlineFieldStart;
                state.CurrentBitOffset = 0;
                state.CurrentBitfieldType = null;
            }
            else
            {
                this.PrepareNestedStructStart(composite, state, unionPosition);
            }
        }

        if (composite.IsUnion)
        {
            IDictionary<string, object?> currentContainerDict = currentContainer;
            if (name.Length == 0)
            {
                // An anonymous promoted union (the promoted-member rule extended to unions): its members are spliced
                // into the parent's container exactly like an anonymous struct's, read from the
                // union's own decoded views so every member sees the same overlapping bytes.
                UnionValue promoted = this.ReadUnionValue(composite, state, debugStack);
                foreach (KeyValuePair<string, object?> member in promoted.Members)
                {
                    currentContainerDict[member.Key] = member.Value;
                }
            }
            else
            {
                string unionName = name;
                DebugPath? unionDebugStack = state.Debug ? new DebugPath(debugStack, unionName) : debugStack;
                currentContainerDict[unionName] = this.ReadUnionValue(composite, state, unionDebugStack);
            }

            if (usesCursor)
            {
                cursor!.CompleteField(state.Stream.Position);
            }

            return;
        }

        if (name.Length == 0)
        {
            // An anonymous promoted member has no name of its own - its children are read
            // directly into the parent's own container, with no nested StructValue, and its own
            // element is excluded from the debug stack so a descendant's path reads `root.x`, not
            // `root..x`. Transitive promotion works for free: a promoted member's own promoted child
            // re-enters this same branch with `currentContainer` still the original root container.
            this.ReadCompiledStructInto(composite, currentContainer, state, debugStack);

            if (usesCursor)
            {
                cursor!.CompleteField(state.Stream.Position);
            }

            return;
        }

        // Give every struct its own value, then attach it before reading children so nested paths are preserved.
        var newContainer = new StructValue(composite.Shape);
        IDictionary<string, object?> structContainer = currentContainer;
        string newName = name;

        structContainer[newName] = newContainer;

        if (state.Debug)
        {
            // Extend the layout stack only for debug output; normal parsing does not need this allocation.
            debugStack = new DebugPath(debugStack, newName);
        }

        this.ReadCompiledStructInto(composite, newContainer, state, debugStack);

        if (usesCursor)
        {
            cursor!.CompleteField(state.Stream.Position);
        }
    }

    /// <summary>
    ///     Reads one layout element and adds its value to the current object.
    ///     Structs, typedefs, fields, arrays, unions, pointers, and debug tracking all meet here so they advance through the stream consistently.
    /// </summary>
    private void HandleCStructElement(
        CStructElement el,
        StructValue currentContainer,
        CStructOperationContext state,
        DebugPath? debugStack,
        long unionPosition = -1,
        bool alignInlineStructStart = false,
        CompiledField? fieldDescriptor = null,
        CompositeFieldPlacementCursor? cursor = null)
    {
        // A typedef can resolve to another element, so loop until this call reaches a concrete struct, field, or define.
        // A root requested through a typedef alias (`typedef struct _X { } X;` parsed as `X`) is stored and
        // reported under the name the caller used, not the tag.
        string? aliasName = null;
        while (true)
        {
            switch (el)
            {
            case Struct s:
                this.ReadCompositeMember(
                    fieldDescriptor?.Composite ?? this.compiledSizeQueries.GetCompiledComposite(s),
                    aliasName ?? s.Name.Name,
                    currentContainer,
                    state,
                    debugStack,
                    unionPosition,
                    alignInlineStructStart,
                    fieldDescriptor,
                    cursor);
                break;

            case Typedef t:
                {
                    if (t.Struct is not null)
                    {
                        // The alias names the value and its debug path; the inline body is read as the struct it is.
                        aliasName = t.Name.Name;
                        fieldDescriptor = null;
                        el = t.Struct;
                        unionPosition = -1;
                        continue;
                    }

                    if (state.Debug)
                    {
                        // Preserve the alias in debug metadata even though its underlying type does the actual reading.
                        debugStack = new DebugPath(debugStack, t.Name.Name);
                    }

                    // Root aliases use a precompiled field projection, including aliases of structs and pointers.
                    fieldDescriptor = this.compiledModelQueries.GetCompiledRootField(t);
                    el = fieldDescriptor.EffectiveField;

                    unionPosition = -1;
                    continue;
                }

            case CstructEnum enm:
                // A direct enum root uses the same synthetic compiled scalar field as an enum typedef.
                fieldDescriptor = this.compiledModelQueries.GetCompiledRootField(enm);
                el = fieldDescriptor.EffectiveField;
                unionPosition = -1;
                continue;

            case Defines d:
                // Definitions do not consume bytes; they prepare an expression value for array lengths and later fields.
                state.Variables[d.Name.Name] = new Literal(
                    this.layoutExpressionEvaluator.Evaluate(
                        d.Value,
                        state.Variables,
                        "definition " + d.Name.Name,
                        ExpressionFailureDomain.Read));
                break;
            case Field f:
                {
                    CompiledField compiledField = fieldDescriptor ??
                                                  throw new InvalidOperationException(
                                                      "Field execution requires a compiled descriptor: " +
                                                      f.Name.Name);
                    if (compiledField.IsZeroWidthBitfield)
                    {
                        // A `: 0` separator has no bytes and no value; the cursor applies its placement effect.
                        if (cursor is not null && unionPosition == -1)
                        {
                            (long separatorEnd, _, _) = cursor.AdvanceToField(compiledField);
                            state.Stream.Position = separatorEnd;
                            state.NextPosition = separatorEnd;
                        }

                        state.CurrentBitOffset = 0;
                        state.CurrentBitfieldType = null;
                        break;
                    }

                    Func<Stream, object>? fieldReader = this.codecs.ReaderOf(compiledField);
                    CompiledCompositeType? nestedComposite = compiledField.Composite;
                    CompiledEnumType? fieldEnum = compiledField.Enum;

                    // Begin with one value, then expand fixed arrays or translate unsized character arrays to terminated strings.
                    int numFieldValues = 1;
                    bool hasFixedArrayDeclarator =
                        compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or
                        CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;

                    // A data-sized array (T v[EOF], T v[]) is counted once its start is known, after placement below.
                    bool dataSizedArray = compiledField.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;

                    if (compiledField.Array.Kind != CompiledArrayKind.Scalar && !dataSizedArray)
                    {
                        if (compiledField.Array.Kind == CompiledArrayKind.Flexible)
                        {
                            // An unsized character array is a terminated string in this layout language; the
                            // compiled view already carries its terminated codec.
                            fieldReader = this.codecs.TerminatedReaderOf(compiledField);
                        }
                        else if (compiledField.Array.Dimensions.Length > 1)
                        {
                            // Every dimension of a multidimensional array is compile-time-fixed (the multidimensional array's
                            // fixed-dimensions-only slice), so the total leaf count is already known without
                            // re-evaluating any expression against the current stream's variables. Elements are
                            // still read in the same flat, sequential, row-major order a 1-D array would use; only
                            // the container shape built after the loop differs (see the reshape step below).
                            numFieldValues = compiledField.Array.TotalFixedElementCount ??
                                             throw new InvalidOperationException(
                                                 "Multidimensional array has no fixed total element count: " +
                                                 compiledField.Name);
                            if (numFieldValues > state.MaxArrayElements)
                            {
                                throw new CStructReadLimitException(
                                    ReadFailures.ArrayLengthLimit(numFieldValues, state.MaxArrayElements));
                            }
                        }
                        else
                        {
                            // Evaluate the count only after earlier fields have populated the parser variable dictionary.
                            numFieldValues = this.layoutExpressionEvaluator.Evaluate(
                                compiledField.Array.CountExpression ??
                                throw new InvalidOperationException(
                                    "Compiled array has no count expression: " + compiledField.Name),
                                state.Variables,
                                "array length for " + compiledField.Name,
                                ExpressionFailureDomain.Read);
                            if (numFieldValues < 0)
                            {
                                throw new CStructReadException("Array length cannot be negative: " + compiledField.Name);
                            }

                            if (numFieldValues > state.MaxArrayElements)
                            {
                                throw new CStructReadLimitException(
                                    ReadFailures.ArrayLengthLimit(numFieldValues, state.MaxArrayElements));
                            }
                        }
                    }

                    if (state.Debug)
                    {
                        // Add this field after its parent struct so debug records identify the complete layout path.
                        // An unnamed padding field shows as `_`, the name it was declared with.
                        debugStack = new DebugPath(debugStack, compiledField.Name.Length == 0 && compiledField.BitSize == 0 && !compiledField.IsInlineComposite ? "_" : compiledField.Name);
                    }

                    IDictionary<string, object?> containerDict = currentContainer;

                    if (unionPosition != -1)
                    {
                        // Rewind before each union member so all interpretations use the same bytes.
                        state.Stream.Position = unionPosition;
                        state.CurrentBitOffset = 0;
                        state.CurrentBitfieldType = null;
                    }

                    bool useLegacyPlacement = cursor is null || unionPosition != -1;

                    if (useLegacyPlacement)
                    {
                        if (state.CurrentBitOffset > 0 && compiledField.BitSize == 0)
                        {
                            // A composite or primitive field after a partially used bitfield unit begins after the
                            // complete storage unit. Doing this before type dispatch keeps enums and named structs on
                            // the same rule.
                            state.Stream.Position = state.NextPosition;
                            state.CurrentBitOffset = 0;
                            state.CurrentBitfieldType = null;
                        }
                    }
                    else
                    {
                        // The cursor already knows this field's start (and, for a bitfield, whether it continues the
                        // active storage unit or opens a new one) - apply its decision once, for every array element,
                        // instead of re-deriving it per element or per type branch below.
                        (long fieldStart, int bitOffset, int unitSize) = cursor!.AdvanceToField(compiledField);
                        this.ValidateOffsetAssertionAtRuntime(compiledField, fieldStart, state.Variables);
                        state.Stream.Position = fieldStart;
                        if (compiledField.BitSize > 0)
                        {
                            state.CurrentBitOffset = bitOffset;
                            state.CurrentBitfieldType = compiledField.BitUnitType;
                            state.CurrentBitfieldSize = unitSize;
                        }
                        else
                        {
                            state.CurrentBitOffset = 0;
                            state.CurrentBitfieldType = null;
                        }
                    }

                    if (dataSizedArray)
                    {
                        // The placement above put the stream at the field start; count the whole elements from there.
                        int elementSize = compiledField.FixedElementSize ??
                                          throw new InvalidOperationException("Data-sized array has no fixed element size: " + compiledField.Name);
                        numFieldValues = compiledField.Array.Kind == CompiledArrayKind.ToEnd
                                             ? DynamicArrayExtent.CountToEnd(state.Stream, state.Stream.Position, elementSize, state.MaxArrayElements, compiledField.Name)
                                             : DynamicArrayExtent.CountTerminated(state.Stream, state.Stream.Position, elementSize, state.MaxArrayElements, compiledField.Name);
                    }

                    bool isArray = hasFixedArrayDeclarator;

                    // Bulk path for arrays of fixed-width numeric primitives: one block read and span decoding
                    // instead of the per-element loop below. Restricted to the shapes whose per-element side effects
                    // are exactly reproducible there: cursor placement (the field start is already set), no debug
                    // records, no union rewinds, no bitfields, pointers, enums, structs, or character types.
                    bool bulkNumeric = isArray && !state.Debug && cursor is not null && unionPosition == -1 && compiledField.BitSize == 0 &&
                                       compiledField.PointerDepth == 0 && nestedComposite is null && fieldEnum is null && compiledField.Name.Length > 0 &&
                                       numFieldValues > 0 && compiledField.Codec.IsFixedWidthNumeric;

                    // A one-dimensional numeric array becomes a typed PrimitiveArray<T> (stage 2); every other array
                    // accumulates boxed elements first (fixed character arrays are converted to a string after the loop).
                    bool typedArray = bulkNumeric && compiledField.Array.Dimensions.Length == 1;
                    if (isArray && !typedArray)
                    {
                        containerDict[compiledField.Name] = new List<object?>(numFieldValues);
                    }

                    string fieldTypeName = compiledField.DisplayTypeSpelling;

                    // An enum-typed bitfield takes the primitive bit-slicing path below and is wrapped afterwards.
                    bool isKnownStruct = nestedComposite is not null || (fieldEnum is not null && compiledField.BitSize == 0);
                    bool isKnownFieldType = fieldReader is not null;

                    if (isArray && !compiledField.IsPointer && BoundedTextCodec.IsType(compiledField.TypeSpelling))
                    {
                        long start = state.Stream.Position;
                        string text = state.FixedText(PrimitiveCodecs.ReadBoundedText(state.Stream, numFieldValues, compiledField.TypeSpelling));
                        long end = state.Stream.Position;
                        containerDict[compiledField.Name] = text;
                        if (state.Debug)
                        {
                            state.RegisterDebugData(start, end, debugStack, text, fieldTypeName);
                        }

                        state.NextPosition = end;
                        if (!useLegacyPlacement)
                        {
                            cursor!.CompleteField(end);
                        }

                        break;
                    }

                    int firstElement = 0;
                    if (bulkNumeric)
                    {
                        object? lastElement;
                        if (typedArray)
                        {
                            IList<object?> typed = PrimitiveArrayReader.Read(state.Stream, compiledField.Codec, numFieldValues);
                            containerDict[compiledField.Name] = typed;
                            lastElement = typed[numFieldValues - 1];
                        }
                        else
                        {
                            lastElement = PrimitiveArrayReader.ReadInto(
                                state.Stream,
                                compiledField.Codec,
                                numFieldValues,
                                (List<object?>)containerDict[compiledField.Name]!);
                        }

                        state.NextPosition = state.Stream.Position;

                        // The per-element loop captured every element into the layout variables, so the value that
                        // survives is the last one; reproduce exactly that.
                        if (compiledField.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
                        {
                            LayoutVariableCapture.Capture(state.Variables, compiledField.Name, lastElement);

                            state.PublishQualified(compiledField.Name);
                        }

                        firstElement = numFieldValues;
                    }

                    // A one-dimensional array of a fully fixed struct whose whole extent is in memory
                    // is read by looping the element's static plan over one span instead of dispatching per element.
                    if (isArray && firstElement == 0 && numFieldValues > 0 && !state.Debug && !useLegacyPlacement && !StaticReadPlan.DisabledForTesting &&
                        compiledField.PointerDepth == 0 && nestedComposite is { IsUnion: false } composite && compiledField.Array.Dimensions.Length == 1)
                    {
                        if (composite.StaticPlan is StaticReadPlan plan && plan.Size > 0 && compiledField.FixedElementSize == plan.Size &&
                            state.StructureDepth + plan.NestingDepth <= state.MaxNestingDepth && plan.MaximumArrayCount <= state.MaxArrayElements &&
                            (!this.Aligned || state.Stream.Position % composite.Symbol.Alignment == 0) &&
                            (long)numFieldValues * plan.Size <= int.MaxValue &&
                            state.Stream.TryReadSpanWithinBudget(numFieldValues * plan.Size, out ReadOnlySpan<byte> elements))
                        {
                            var list = (List<object?>)containerDict[compiledField.Name]!;
                            for (int element = 0; element < numFieldValues; element++)
                            {
                                var container = new StructValue(composite.Shape);
                                this.ExecuteStaticPlan(plan, elements.Slice(element * plan.Size, plan.Size), container, state);
                                list.Add(container);
                            }

                            state.CurrentBitOffset = 0;
                            state.CurrentBitfieldType = null;
                            state.NextPosition = state.Stream.Position;
                            firstElement = numFieldValues;
                        }
                    }

                    for (int i = firstElement; i < numFieldValues; i++)
                    {
                        // Composite leaves need the containing element's coordinates. Keep primitive-array
                        // debug records unchanged: consumers historically group those under the array field.
                        DebugPath? elementDebugStack = debugStack;
                        if (state.Debug && isArray && compiledField.TargetComposite is not null)
                        {
                            string indices = string.Empty;
                            int remainingIndex = i;
                            for (int dimension = compiledField.Array.Dimensions.Length - 1; dimension >= 0; dimension--)
                            {
                                int size = compiledField.Array.Dimensions[dimension].FixedCount ?? numFieldValues;
                                indices = "[" + (remainingIndex % size) + "]" + indices;
                                remainingIndex /= size;
                            }

                            elementDebugStack = new DebugPath(debugStack!.Parent, compiledField.Name + indices);
                        }

                        if (compiledField.PointerDepth == 0 && isKnownStruct)
                        {
                            // Structs and enums have layout-aware readers rather than primitive byte handlers.
                            if (fieldEnum is { } enm)
                            {
                                // Align the enum's primitive storage before reading its numeric representation.
                                // The cursor already applied this once per field (not per array element) when
                                // it is available; only the legacy single-field/root path still aligns here.
                                long curPos = state.Stream.Position;

                                if (useLegacyPlacement && state.Aligned && unionPosition == -1)
                                {
                                    int structAlignment = compiledField.Alignment;
                                    state.Stream.Position = LayoutMath.AlignUp(curPos, structAlignment);
                                    curPos = state.Stream.Position;
                                }

                                EnumValueResult newEnum = this.ReadEnumValue(
                                    compiledField,
                                    enm,
                                    state.Stream);
                                long endPos = state.Stream.Position;

                                if (state.Debug)
                                {
                                    state.RegisterDebugData(
                                        curPos,
                                        endPos,
                                        debugStack,
                                        newEnum.Value,
                                        fieldTypeName);
                                }

                                if (isArray)
                                {
                                    ((List<object?>)containerDict[compiledField.Name]!).Add(newEnum);
                                }
                                else
                                {
                                    containerDict[compiledField.Name] = newEnum;
                                }

                                if (!isArray && (compiledField.CapturesLayoutVariable || state.CaptureAllLayoutVariables))
                                {
                                    this.UpdateExactLayoutVariable(
                                        state.Variables,
                                        compiledField.Name,
                                        newEnum.Value);
                                    state.PublishQualified(compiledField.Name);
                                }
                            }
                            else
                            {
                                CompiledCompositeType strct = nestedComposite!;
                                if (useLegacyPlacement)
                                {
                                    this.PrepareNestedStructStart(strct, state, unionPosition);
                                }

                                // A field named through a dotted path (`hdr.n`) republishes its nested values under
                                // the qualified prefix while its body is read.
                                string? outerPrefix = state.QualifiedPrefix;
                                if (compiledField.HasQualifiedPrefix && !isArray)
                                {
                                    state.QualifiedPrefix = outerPrefix is null ? compiledField.QualifiedPrefix : outerPrefix + compiledField.QualifiedPrefix;
                                }

                                object nestedValue;
                                if (strct.IsUnion)
                                {
                                    nestedValue = this.ReadUnionValue(strct, state, elementDebugStack);
                                }
                                else
                                {
                                    var newContainer = new StructValue(strct.Shape);
                                    this.ReadCompiledStructInto(
                                        strct,
                                        newContainer,
                                        state,
                                        elementDebugStack);
                                    nestedValue = newContainer;
                                }

                                state.QualifiedPrefix = outerPrefix;

                                if (isArray)
                                {
                                    ((List<object?>)containerDict[compiledField.Name]!).Add(nestedValue);
                                }
                                else
                                {
                                    containerDict[compiledField.Name] = nestedValue;
                                }
                            }
                        }
                        else if (compiledField.PointerDepth == 0 && !isKnownFieldType)
                        {
                            throw new InvalidOperationException($"No handler for field type {fieldTypeName}");
                        }
                        else
                        {
                            if (useLegacyPlacement && compiledField.BitSize > 0 && state.BitfieldUnitSeeded)
                            {
                                // A resolved target arrives with its placed unit; nothing to derive.
                                state.BitfieldUnitSeeded = false;
                            }
                            else if (useLegacyPlacement && compiledField.BitSize > 0)
                            {
                                int bitCapacity = checked(
                                    (compiledField.BitStorageSize ??
                                     throw new InvalidOperationException(
                                         "Compiled bitfield has no storage size: " + compiledField.Name)) * 8);
                                int activeUnitSize = state.CurrentBitfieldType is null
                                                         ? 0
                                                         : state.CurrentBitfieldSize;

                                // Legacy placement only sees a union member or a root bitfield, which always opens
                                // its own unit; the rule below is the MSVC size rule for completeness.
                                bool startsNewStorageUnit = state.CurrentBitOffset > 0 &&
                                                           (activeUnitSize != bitCapacity / 8 ||
                                                            state.CurrentBitOffset + compiledField.BitSize > bitCapacity);
                                if (startsNewStorageUnit)
                                {
                                    state.Stream.Position = state.NextPosition;
                                    state.CurrentBitOffset = 0;
                                    state.CurrentBitfieldType = null;
                                }

                                if (state.CurrentBitOffset == 0)
                                {
                                    state.CurrentBitfieldType = compiledField.BitUnitType;
                                    state.CurrentBitfieldSize = compiledField.BitStorageSize ??
                                                                throw new InvalidOperationException(
                                                                    "Compiled bitfield has no storage size: " +
                                                                    compiledField.Name);
                                }
                            }

                            long curPos = state.Stream.Position;

                            if (useLegacyPlacement && state.Aligned && unionPosition == -1)
                            {
                                // A union's compiled start already establishes its boundary; every member begins exactly
                                // there, including when a pointer target is not naturally aligned in the containing stream.
                                // Ordinary fields align at their own boundaries, while bitfields in one unit share bytes.
                                int structAlignment = compiledField.Alignment;
                                if (structAlignment != state.CurrentFieldAlignment && state.CurrentBitOffset > 0)
                                {
                                    curPos = state.NextPosition;
                                    state.CurrentBitOffset = 0;
                                    state.CurrentBitfieldType = null;
                                }

                                state.Stream.Position = LayoutMath.AlignUp(curPos, structAlignment);
                                curPos = state.Stream.Position;

                                state.CurrentFieldAlignment = structAlignment;
                            }

                            // Fixed-width numerics are decoded straight from a memory-backed cursor; every
                            // other codec, and every stream source, keeps the delegate path.
                            // A bitfield whose placed unit differs from its declared type (a packed SysV window) is
                            // read as a raw unsigned unit of that size; every other field takes its codec.
                            bool windowedUnit = compiledField.BitSize > 0 && state.CurrentBitfieldSize != compiledField.Codec.Size;
                            object content = compiledField.PointerDepth > 0
                                                 ? this.ReadPointerValue(
                                                                         compiledField.PointerDepth,
                                                                         compiledField,
                                                                         state,
                                                                         elementDebugStack)
                                                 : windowedUnit
                                                     ? BinaryPrimitiveIO.ReadBitfieldUnit(state.Stream, state.CurrentBitfieldSize, compiledField.BitStorageIsLittleEndian ?? true)
                                                     : compiledField.Codec.IsFixedWidthNumeric &&
                                                       state.Stream.TryReadSpan(compiledField.Codec.Size, out ReadOnlySpan<byte> numericBytes)
                                                         ? compiledField.Codec.ReadNumeric(numericBytes)
                                                         : fieldReader?.Invoke(state.Stream) ??
                                                           throw new InvalidOperationException(
                                                               "Compiled field has no reader: " + fieldTypeName);

                            // Remember the full primitive range before bitfield handling possibly rewinds for another slice.
                            long endPos = state.Stream.Position;

                            long finalEndPos = endPos;
                            if (compiledField.BitSize > 0)
                            {
                                // Read the storage value once, then expose only this field's slice of its bits.
                                int elementBitSize = checked(state.CurrentBitfieldSize * 8);
                                if (state.CurrentBitOffset + compiledField.BitSize > elementBitSize)
                                {
                                    throw new CStructReadException("Bitfield exceeds its storage unit: " + compiledField.Name);
                                }

                                ulong extracted = BitfieldCodecTable.ExtractBitfieldValue(
                                    content,
                                    BitfieldCodecTable.EffectiveShift(state.CurrentBitOffset, compiledField.BitSize, elementBitSize, this.highBitFirst),
                                    compiledField.BitSize);
                                content = compiledField.BitSize < 32 ? (object)(int)extracted : extracted;
                                if (fieldEnum is { } bitfieldEnum)
                                {
                                    content = CreateEnumValue(bitfieldEnum, content);
                                }

                                state.CurrentBitOffset += compiledField.BitSize;
                                int bitOffsetInBytes = 1 + (state.CurrentBitOffset / 8);
                                long elementByteSize = endPos - curPos;
                                if (bitOffsetInBytes > elementByteSize)
                                {
                                    state.CurrentBitOffset -= elementBitSize;
                                    state.CurrentBitfieldType = null;
                                }
                                else
                                {
                                    state.Stream.Position = curPos;
                                    state.NextPosition = endPos;
                                    finalEndPos = curPos;
                                }
                            }
                            else
                            {
                                state.NextPosition = endPos;
                            }

                            if (state.Debug)
                            {
                                // Debug collection rereads the full source bytes and then restores the logical parser position.
                                state.RegisterDebugData(curPos, endPos, elementDebugStack, content, fieldTypeName);
                                state.Stream.Position = finalEndPos;
                            }

                            // An anonymous nonzero-width bitfield is pure padding: its bits are read and
                            // consumed above (and still appear in debug output, registered before this point), but
                            // it has no name to store into the result container or capture as an expression variable.
                            if (compiledField.Name.Length > 0)
                            {
                                if (isArray)
                                {
                                    ((List<object?>)containerDict[compiledField.Name]!).Add(content);
                                }
                                else
                                {
                                    containerDict[compiledField.Name] = content;
                                }

                                if (!compiledField.CapturesLayoutVariable && !state.CaptureAllLayoutVariables)
                                {
                                    // No expression in this layout can name the field: skip the capture.
                                }
                                else if (content is Pointer p)
                                {
                                    // Expressions refer to the encoded pointer address, not the Pointer wrapper or target value.
                                    // A valid signed stream address may exceed the expression language's Int32 domain: retain
                                    // the pointer result, but remove any stale caller/define value shadowed by this field so
                                    // later expressions fail instead of using data contradicted by the stream. The range check
                                    // replaces a former try/catch around Convert.ToInt32, which threw for every such address.
                                    if (Int32Capture.TryFromInt64(p.Address, out int address))
                                    {
                                        state.Variables[compiledField.Name] = new Literal(address);
                                    }
                                    else
                                    {
                                        state.Variables.Remove(compiledField.Name);
                                    }
                                }
                                else if (content is string str)
                                {
                                    // Existing expression semantics preserve strings as identifiers for compatible layouts.
                                    state.Variables[compiledField.Name] = new Identifier(str);
                                }
                                else if (compiledField.IsFixedPoint || content is Guid)
                                {
                                    state.Variables.Remove(compiledField.Name);
                                }
                                else if (content is IConvertible)
                                {
                                    // Scalars become literals so following array counts and expressions can use their name.
                                    // The capture is exception-free: an out-of-range integer becomes an exact literal that
                                    // fails with its value only when an expression selects it.
                                    LayoutVariableCapture.Capture(state.Variables, compiledField.Name, content);
                                }

                                if (state.HasQualifiedPrefix && (compiledField.CapturesLayoutVariable || state.CaptureAllLayoutVariables))
                                {
                                    state.PublishQualified(compiledField.Name);
                                }
                            }
                        }
                    }

                    if (compiledField.Array.Kind == CompiledArrayKind.Terminated)
                    {
                        // The all-zero terminator element belongs to the field but not to its value.
                        long terminatorEnd = checked(state.Stream.Position + (compiledField.FixedElementSize ?? 0));
                        state.Stream.Position = terminatorEnd;
                        state.NextPosition = terminatorEnd;
                    }

                    if (!useLegacyPlacement && compiledField.BitSize == 0)
                    {
                        // Bitfields skip this: the cursor already reserved their whole storage unit's span when it
                        // opened, mirroring how CStructAddressResolver's own cursor usage never completes a bitfield.
                        cursor!.CompleteField(state.Stream.Position);
                    }

                    if (isArray)
                    {
                        bool isCharacterElement =
                            !compiledField.IsPointer && (compiledField.IsCharElement || compiledField.IsWideCharElement);

                        if (compiledField.Array.Dimensions.Length > 1)
                        {
                            int[] dimensionSizes = compiledField.Array.Dimensions
                                .Select(
                                    dimension => dimension.FixedCount ??
                                                 throw new InvalidOperationException(
                                                     "Multidimensional array dimension has no fixed count: " +
                                                     compiledField.Name))
                                .ToArray();
                            var flatValues = (List<object?>)containerDict[compiledField.Name]!;

                            if (isCharacterElement)
                            {
                                // The innermost dimension of a fixed string table (char names[10][32]) collapses
                                // to a string, exactly like today's single-dimension char[32] buffer; every outer
                                // dimension then nests around that row of strings the same way any other element
                                // type nests, so only this one step is character-specific.
                                int rowSize = dimensionSizes[^1];
                                var rows = new List<object?>(flatValues.Count / rowSize);
                                for (int start = 0; start < flatValues.Count; start += rowSize)
                                {
                                    string row = state.FixedText(new string(flatValues.GetRange(start, rowSize).Cast<char>().ToArray()));
                                    if (compiledField.IsWideCharElement)
                                    {
                                        try
                                        {
                                            _ = this.GetWideCharacterEncoding(compiledField).GetByteCount(row);
                                        }
                                        catch (EncoderFallbackException exception)
                                        {
                                            throw new CStructReadException(
                                                "Wide-character buffer contains an invalid UTF-16 code-unit sequence.",
                                                exception);
                                        }
                                    }

                                    rows.Add(row);
                                }

                                containerDict[compiledField.Name] = ReshapeFlatArrayValues(rows, dimensionSizes[..^1]);
                            }
                            else
                            {
                                containerDict[compiledField.Name] = ReshapeFlatArrayValues(flatValues, dimensionSizes);
                            }
                        }
                        else if (isCharacterElement)
                        {
                            // Expose fixed character arrays as the string callers expect, after every character has been read.
                            var list = (List<object?>)containerDict[compiledField.Name]!;
                            string parsedString = state.FixedText(new string(list.Cast<char>().ToArray()));
                            if (compiledField.IsWideCharElement)
                            {
                                try
                                {
                                    _ = this.GetWideCharacterEncoding(compiledField).GetByteCount(parsedString);
                                }
                                catch (EncoderFallbackException exception)
                                {
                                    throw new CStructReadException(
                                        "Wide-character buffer contains an invalid UTF-16 code-unit sequence.",
                                        exception);
                                }
                            }

                            containerDict[compiledField.Name] = parsedString;
                        }
                    }

                    break;
                }
            }

            break;
        }
    }

    /// <summary>Checks the requested path, prepares variables, and chooses ordinary or debug parsing.</summary>
    private (StructValue Root, IReadOnlyList<PathSegment> Segments) ParseStreamInternal(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOperationSettings options,
        bool debug,
        out List<DebugData> debugData)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (string.IsNullOrWhiteSpace(elementNameOrPath))
        {
            throw new CStructPathException("Path is empty.");
        }

        // Normalize optional settings once before passing them through the rest of the parsing pipeline.
        ReadOperationSettings effectiveOptions = options;
        if (debug && !stream.CanSeek)
        {
            throw new ArgumentException(
                "Debug mapping and address resolution require a seekable stream.",
                nameof(stream));
        }

        // Copy caller variables and resolve layout-wide definitions without mutating caller-owned state.
        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
        if (segments.Count == 0)
        {
            throw new CStructPathException("Path is empty.");
        }

        string rootName = segments[0].Name;
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(rootName, out _))
        {
            throw this.compiledModelQueries.UnknownRoot(rootName);
        }

        StructValue root;
        try
        {
            if (debug)
            {
                // Debug reads use the same parser but additionally retain byte ranges and layout stacks for each value.
                (List<DebugData> DebugData, StructValue Result) parsed
                    = this.ParseStreamRootDebug(stream, rootName, effectiveVariables, effectiveOptions);
                debugData = parsed.DebugData;
                root = parsed.Result;
            }
            else
            {
                // Keep a non-null empty list so callers can handle both modes through the same return shape.
                debugData = new List<DebugData>();
                root = this.ParseStreamRoot(stream, rootName, effectiveVariables, effectiveOptions);
            }
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }

        // The root contains the full parsed structure; the original segments select the requested child afterwards.
        return (root, segments);
    }

    /// <summary>Creates the root object and reads one named layout element without collecting debug byte ranges.</summary>
    private StructValue ParseStreamRoot(
        Stream stream,
        string elementName,
        Dictionary<string, Expr> variables,
        ReadOperationSettings options)
    {
        // Create a container for the named root layout element before constructing all per-read mutable state.
        var root = new StructValue(this.compiledModelQueries.GetRootShape(elementName));

        if (!this.compiledModelQueries.TryGetCompiledDeclaration(elementName, out CStructElement? cstructElement))
        {
            throw this.compiledModelQueries.UnknownRoot(elementName);
        }

        // The state captures stream progress, variables, alignment, and pointer policy for this one parse operation.
        var state = new CStructOperationContext(
                                                  stream,
                                                  variables,
                                                  this.Aligned,
                                                  options);

        // Start with an empty debug stack; it remains empty in ordinary parsing but keeps the shared call shape simple.
        try
        {
            this.HandleCStructElement(cstructElement, root, state, null);
        }
        finally
        {
            state.Complete();
        }

        return root;
    }

    /// <summary>Creates the root object and reads one named layout element while collecting debug byte ranges.</summary>
    private (List<DebugData> DebugData, StructValue Result) ParseStreamRootDebug(
        Stream stream,
        string elementName,
        Dictionary<string, Expr> variables,
        ReadOperationSettings options)
    {
        // Debug mode uses the same root construction as ordinary mode, with one flag enabled in the operation state.
        var root = new StructValue(this.compiledModelQueries.GetRootShape(elementName));

        if (!this.compiledModelQueries.TryGetCompiledDeclaration(elementName, out CStructElement? cstructElement))
        {
            throw this.compiledModelQueries.UnknownRoot(elementName);
        }

        var state = new CStructOperationContext(
                                                  stream,
                                                  variables,
                                                  this.Aligned,
                                                  options)
        { Debug = true, };

        // Each nested call appends its element to the stack before recording a byte range.
        try
        {
            this.HandleCStructElement(cstructElement, root, state, null);
        }
        finally
        {
            state.Complete();
        }

        return (state.DebugMapping, root);
    }

    /// <summary>Reads a pointer address using the pointer width and byte order chosen for this layout.</summary>
    private long ReadPointerAddress(CStructOperationContext state)
    {
        // Read exactly the configured pointer width in the layout's byte order, without allocating a buffer.
        ulong rawAddress = this.PointerSize switch
        {
            1 or 2 or 4 or 8 => BinaryPrimitiveIO.ReadUnsignedBySize(state.Stream, this.PointerSize, this.IsLittleEndian),
            _ => throw new ArgumentOutOfRangeException("Unknown pointer size: " + this.PointerSize),
        };
        try
        {
            return CStructPointerArithmetic.DecodeStoredAddress(rawAddress);
        }
        catch (OverflowException exception)
        {
            // Stream positions use signed long values, so reject an otherwise valid unsigned address before any seek.
            throw new CStructReadException(
                "Pointer address exceeds the supported stream address range.",
                exception);
        }
    }

    /// <summary>Reads the value found at a pointer target after the address has passed safety checks.</summary>
    private object ReadPointerTargetValue(
        CompiledField field,
        CStructOperationContext state,
        DebugPath? debugStack)
    {
        // The target view has no pointer depth left, so its compiled type is the value's own type.
        if (field.Type.Symbol.Definition is CompiledEnumType enm)
        {
            return this.ReadEnumValue(field, enm, state.Stream);
        }

        if (field.Type.Symbol.Definition is CompiledCompositeType strct)
        {
            // Pointer targets use the same compiled composite executor as selected struct/union reads. In
            // particular, union members all rewind to this target address rather than consuming sequentially.
            return this.ParseCompiledStructAt(
                state,
                state.Stream.Position,
                strct,
                debugStack,
                state.StructureDepth,
                state.PointerDereferenceDepth,
                state.Debug).Result;
        }

        if (this.codecs.TerminatedReaderOf(field) is { } terminatedReader)
        {
            // Pointer-to-char shorthand uses a terminated-string handler rather than a one-character primitive reader.
            return terminatedReader(state.Stream);
        }

        // All remaining targets are ordinary primitive values read from the current target position.
        return this.codecs.ReaderOf(field)?.Invoke(state.Stream) ??
               throw new InvalidOperationException(
                   "Compiled pointer target has no reader: " + field.TypeSpelling);
    }

    /// <summary>Decodes one enum through its validated backing domain and declaration-order symbolic table.</summary>
    private EnumValueResult ReadEnumValue(CompiledField field, CompiledEnumType enm, Stream stream)
    {
        object storageValue = this.codecs.ReaderOf(field)?.Invoke(stream) ??
                              throw new InvalidOperationException(
                                  "Compiled enum has no storage reader: " + enm.Name);
        return CreateEnumValue(enm, storageValue);
    }

    /// <summary>Maps a decoded storage value to the enum result (shared by the general and static readers).</summary>
    private static EnumValueResult CreateEnumValue(CompiledEnumType compiled, object storageValue)
    {
        BigInteger value = compiled.Integer.FromStorageValue(storageValue);
        ulong rawBits = compiled.Integer.ToRawBits(value);
        if (compiled.IsFlag)
        {
            // The member decomposition is deferred to first use so a flag read costs what an enum read costs.
            return new FlagValueResult(
                compiled.Name,
                compiled.FindName(rawBits),
                value,
                rawBits,
                compiled.Integer.StorageType,
                compiled.Integer.BitWidth,
                compiled.Integer.IsSigned,
                compiled.Decompose);
        }

        return new EnumValueResult(
            compiled.Name,
            compiled.FindName(rawBits),
            value,
            rawBits,
            compiled.Integer.StorageType,
            compiled.Integer.BitWidth,
            compiled.Integer.IsSigned);
    }

    /// <summary>
    ///     Reads a pointer and, when allowed, follows it to its target.
    ///     It restores the original stream position before returning so a pointer field consumes only its address in the parent layout.
    /// </summary>
    private Pointer ReadPointerValue(
        int pointerDepth,
        CompiledField field,
        CStructOperationContext state,
        DebugPath? debugStack)
    {
        // Reading the address always advances the parent stream by exactly one pointer storage width.
        long address = this.ReadPointerAddress(state);
        if (address == 0)
        {
            // A null pointer has no target to seek to and is represented explicitly without dereferencing.
            return new Pointer(address, null, pointerDepth);
        }

        if (!state.DereferencePointers || state.SuppressPointerDereference)
        {
            // Callers can inspect addresses only; retain that choice on the Pointer result for downstream consumers.
            return new Pointer(address, null, pointerDepth, false);
        }

        if (pointerDepth == 1 && field.Type.TerminalName == "void")
        {
            // A `void *` (or a function pointer) is an opaque address: there is nothing typed to read at its target.
            return new Pointer(address, null, pointerDepth, false);
        }

        if (!state.Stream.CanSeek)
        {
            throw new CStructReadException("Pointer dereferencing requires a seekable stream.");
        }

        if (state.PointerDereferenceDepth >= state.MaxPointerDepth)
        {
            throw new CStructReadLimitException(ReadFailures.PointerDepthLimit);
        }

        // Preserve the post-address location so target parsing cannot disturb the parent struct's sequential read.
        long oldPos = state.Stream.Position;
        long targetAddress;
        try
        {
            targetAddress = CStructPointerArithmetic.ResolveTargetAddress(
                address,
                state.AddressingMode,
                state.PointerOrigin);
        }
        catch (OverflowException exception)
        {
            throw new CStructReadException("Relative pointer address overflowed the supported stream address range.", exception);
        }

        if (targetAddress < 0 || targetAddress >= state.Stream.Length)
        {
            throw new CStructReadException("Pointer target is outside the readable stream range: " + targetAddress);
        }

        // Apply the optional fixed-target budget before seeking, preventing unexpectedly large referenced reads.
        this.EnsurePointerTargetSize(pointerDepth, field, state);

        (long Address, string TypeName, int PointerDepth) targetKey =
            (targetAddress, field.TypeSpelling, pointerDepth);

        // The same target on the active path means a cycle. Detect it before recursive reads can loop forever.
        if (!state.ActivePointerTargets.Add(targetKey))
        {
            throw new CStructReadException("Cyclic pointer target detected at stream address " + targetAddress + ".");
        }

        state.PointerDereferenceDepth++;
        try
        {
            // Seek to the target, then either follow another address or decode the final pointed-to value.
            state.Stream.Position = targetAddress;
            object value = pointerDepth > 1
                               ? this.ReadPointerValue(
                                                       pointerDepth - 1,
                                                       field,
                                                       state,
                                                       debugStack)
                               : this.ReadPointerTargetValue(field, state, debugStack);
            return new Pointer(address, value, pointerDepth, true);
        }
        finally
        {
            // Restore all recursion bookkeeping and the parent location even if the target could not be decoded.
            state.PointerDereferenceDepth--;
            state.ActivePointerTargets.Remove(targetKey);
            state.Stream.Position = oldPos;
        }
    }

    /// <summary>Checks an optional caller limit before reading a fixed-size pointer target.</summary>
    private void EnsurePointerTargetSize(
        int pointerDepth,
        CompiledField field,
        CStructOperationContext state)
    {
        if (!state.MaxPointerTargetBytes.HasValue)
        {
            // No configured budget means the existing pointer behavior remains unrestricted.
            return;
        }

        long? targetSize = pointerDepth > 1
                               ? this.PointerSize
                               : this.GetFixedTargetSize(field);
        if (!targetSize.HasValue)
        {
            // A fixed budget cannot safely approve a string or an unsized structure whose eventual length is unknown.
            throw new CStructReadLimitException(
                "The configured pointer target limit does not allow a variable-length target.");
        }

        if (targetSize.Value > state.MaxPointerTargetBytes.Value)
        {
            // Refuse the target before decoding so malformed data cannot bypass the caller's memory-safety policy.
            throw new CStructReadLimitException(ReadFailures.PointerTargetLimit);
        }
    }

    /// <summary>Returns a target's known size, or <see langword="null"/> when it is variable length.</summary>
    private long? GetFixedTargetSize(CompiledField field)
    {
        if (field.HasTerminatedCodec)
        {
            // A terminator determines string length at runtime, so no finite static bound can be reported here.
            return null;
        }

        // The compiled type carries the static extent only when no runtime expression or terminator controls it.
        return field.Type.Symbol.FixedSize;
    }
}
