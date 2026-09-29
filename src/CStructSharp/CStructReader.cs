namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Syntax;
using CStructSharp.Values;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Contains the stream-reading half of <see cref="CStruct"/>.
///     These methods turn compiled layout elements into nested <see cref="StructValue"/> values while keeping pointer and debug state together.
/// </summary>
public sealed partial class CStruct
{
    private static readonly List<DebugData> NoDebugData = new(0);

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

    /// <summary>
    ///     Prepares a struct read without a parent cursor - a root, a union member, or a pointer target - which starts
    ///     exactly at the stream position: its members align from that first byte (see <see cref="PlacementCursor"/>),
    ///     so the start itself is never moved. Only an unfinished bitfield unit of a preceding read is closed.
    /// </summary>
    /// <param name="state">The read state.</param>
    private static void PrepareNestedStructStart(CStructOperationContext state)
    {
        if (state.CurrentBitOffset > 0)
        {
            // A nested object cannot share the unfinished primitive storage unit of a preceding bitfield.
            state.Stream.Position = state.NextPosition;
            state.ResetBitfieldUnit();
        }
    }

    /// <summary>
    ///     Reads one struct or union member (a root, a named nested composite, or an inline body) into
    ///     <paramref name="currentContainer"/>: a named composite becomes a nested value, an anonymous one is
    ///     promoted into the parent, and a union is decoded through <see cref="ReadUnionValue"/>. A named inline member
    ///     (<c>struct { ... } hdr;</c>) publishes its fields under its qualified prefix (<c>hdr.n</c>) while its body is
    ///     read, exactly as a typed member (<c>h hdr;</c>) does.
    /// </summary>
    /// <param name="composite">The struct or union to read.</param>
    /// <param name="name">The member name, or empty for an anonymous promoted member.</param>
    /// <param name="currentContainer">The value receiving the member.</param>
    /// <param name="state">The input position, limits, variables and optional debug records.</param>
    /// <param name="debugStack">The enclosing debug path, or null when no path is needed.</param>
    /// <param name="unionPosition">The containing union's start, or -1 outside a union.</param>
    /// <param name="alignInlineStructStart">Whether an inline member needs its parent placement applied.</param>
    /// <param name="fieldDescriptor">The member's compiled field, or null for a root.</param>
    /// <param name="cursor">The containing struct's placement cursor, when traversing its fields.</param>
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
            state.ResetBitfieldUnit();
        }

        if (alignInlineStructStart)
        {
            // Inline structs arrive here as Struct instances rather than ordinary Field instances. Prepare
            // their parent boundary explicitly so they follow the same bitfield and alignment rule as a
            // named struct field.
            if (usesCursor && fieldDescriptor is not null)
            {
                (long inlineFieldStart, _, _) = cursor!.AdvanceToField(fieldDescriptor);
                state.Stream.Position = inlineFieldStart;
                state.ResetBitfieldUnit();
            }
            else
            {
                PrepareNestedStructStart(state);
            }
        }

        // A named inline member republishes its fields under its prefix; the prefix is restored when the body is read.
        string? outerPrefix = state.QualifiedPrefix;
        if (name.Length > 0 && fieldDescriptor is { HasQualifiedPrefix: true, } && fieldDescriptor.Array.Kind == CompiledArrayKind.Scalar)
        {
            state.QualifiedPrefix = outerPrefix is null ? fieldDescriptor.QualifiedPrefix : outerPrefix + fieldDescriptor.QualifiedPrefix;
        }

        if (composite.IsUnion)
        {
            IDictionary<string, object?> currentContainerDict = currentContainer;
            if (name.Length == 0)
            {
                // An anonymous promoted union (the promoted-member rule extended to unions): its members are spliced
                // into the parent's container exactly like an anonymous struct's, read from the
                // union's own decoded views so every member sees the same overlapping bytes.
                UnionValue promoted = this.ReadUnionValue(composite, state, debugStack, promoted: true);
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
                state.QualifiedPrefix = outerPrefix;
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
        state.QualifiedPrefix = outerPrefix;

        if (usesCursor)
        {
            cursor!.CompleteField(state.Stream.Position);
        }
    }

    /// <summary>
    ///     Reads one layout element and adds its value to the current object.
    ///     Structs, typedefs, fields, arrays, unions, pointers, and debug tracking all meet here so they advance through the stream consistently.
    /// </summary>
    /// <param name="el">The declaration to read.</param>
    /// <param name="currentContainer">The destination object receiving the decoded value.</param>
    /// <param name="state">The current stream, limits and expression context.</param>
    /// <param name="debugStack">The optional parent path for recorded byte ranges.</param>
    /// <param name="unionPosition">The common union-member byte address, or -1 outside a union view.</param>
    /// <param name="alignInlineStructStart">Whether an inline composite needs its parent placement applied.</param>
    /// <param name="fieldDescriptor">The compiled field metadata, when the caller already resolved it.</param>
    /// <param name="cursor">The containing composite's placement cursor, when traversing its fields.</param>
    /// <param name="positionIsResolvedTarget">Whether the stream is already at the field's exact resolved address.</param>
    private void HandleCStructElement(
        CStructElement el,
        StructValue currentContainer,
        CStructOperationContext state,
        DebugPath? debugStack,
        long unionPosition = -1,
        bool alignInlineStructStart = false,
        CompiledField? fieldDescriptor = null,
        CompositeFieldPlacementCursor? cursor = null,
        bool positionIsResolvedTarget = false)
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

                    // The root field projection carries the alias name, including for structs and pointers, and adds
                    // that name to the debug path exactly once.
                    this.ReadField(this.compiledModelQueries.GetCompiledRootField(t), currentContainer, state, debugStack, -1, cursor, positionIsResolvedTarget);
                    break;
                }

            case CstructEnum enm:
                // A direct enum root uses the same synthetic compiled scalar field as an enum typedef.
                this.ReadField(this.compiledModelQueries.GetCompiledRootField(enm), currentContainer, state, debugStack, -1, cursor, positionIsResolvedTarget);
                break;

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
                this.ReadField(
                    fieldDescriptor ?? throw new InvalidOperationException("Field execution requires a compiled descriptor: " + f.Name.Name),
                    currentContainer,
                    state,
                    debugStack,
                    unionPosition,
                    cursor,
                    positionIsResolvedTarget);
                break;
            }

            break;
        }
    }

    /// <summary>
    ///     Reads a whole <c>char[count]</c> as one block and decodes it as the per-character reader would: one Latin-1
    ///     character per byte, then <c>TrimFixedText</c>. A memory source is read in place; another stream through a
    ///     pooled block. Returns <see langword="false"/>, having read nothing, when the extent is not all there or not
    ///     within the read budget, so the per-character reader reports the failure where it always has.
    /// </summary>
    /// <param name="state">The read state.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="text">The text when the method returns <see langword="true"/>.</param>
    /// <returns>Whether the characters were read.</returns>
    private bool TryReadCharacterBlock(CStructOperationContext state, int count, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        using StagedBytes staged = StagedBytes.Take(state.Stream, count);
        text = staged.Available ? state.FixedText(ReadLatin1Characters(staged.Bytes)) : null;
        return staged.Available;
    }

    /// <summary>
    ///     The fixed size of every dimension of a multidimensional array. A separate method because a lambda over the
    ///     field inside <see cref="HandleCStructElement"/> made the compiler allocate its closure for every field read.
    /// </summary>
    /// <param name="field">A multidimensional array field.</param>
    /// <returns>The dimension sizes, outermost first.</returns>
    private static int[] FixedDimensionSizes(CompiledField field)
    {
        var sizes = new int[field.Array.Dimensions.Length];
        for (int dimension = 0; dimension < sizes.Length; dimension++)
        {
            sizes[dimension] = field.Array.Dimensions[dimension].FixedCount ??
                               throw new InvalidOperationException("Multidimensional array dimension has no fixed count: " + field.Name);
        }

        return sizes;
    }

    /// <summary>Reads a whole root declaration, choosing ordinary or debug parsing.</summary>
    /// <param name="stream">The source, positioned at the root.</param>
    /// <param name="segments">The parsed one-segment path that names the root.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The operation's read settings.</param>
    /// <param name="debug">Whether to record debug byte ranges.</param>
    /// <param name="debugData">The debug records, or a shared empty list for an ordinary read.</param>
    /// <returns>The container holding the root's value under its name.</returns>
    private StructValue ParseStreamInternal(
        Stream stream,
        IReadOnlyList<PathSegment> segments,
        LayoutVariableInput variables,
        ReadOperationSettings options,
        bool debug,
        out List<DebugData> debugData)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (debug && !stream.CanSeek)
        {
            throw new ArgumentException(
                "Debug mapping and address resolution require a seekable stream.",
                nameof(stream));
        }

        // Copy caller variables and resolve layout-wide definitions without mutating caller-owned state.
        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        string rootName = segments[0].Name;
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(rootName, out CStructElement? declaration))
        {
            throw this.compiledModelQueries.UnknownRoot(rootName);
        }

        try
        {
            return this.ParseStreamRoot(stream, rootName, declaration, effectiveVariables, options, debug, out debugData);
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
    }

    /// <summary>Reads one declared root into a new root object, recording byte ranges in debug mode.</summary>
    /// <param name="stream">The source, at the root's first byte.</param>
    /// <param name="rootName">The root's declared name.</param>
    /// <param name="declaration">The root's declaration.</param>
    /// <param name="variables">The operation's layout variables.</param>
    /// <param name="options">The read settings.</param>
    /// <param name="debug">Whether to record each value's byte range and layout path.</param>
    /// <param name="debugData">The records; one shared empty list outside debug mode, which callers only discard.</param>
    /// <returns>The root object, holding the root's value under its name.</returns>
    private StructValue ParseStreamRoot(
        Stream stream,
        string rootName,
        CStructElement declaration,
        Dictionary<string, Expr> variables,
        ReadOperationSettings options,
        bool debug,
        out List<DebugData> debugData)
    {
        var root = new StructValue(this.compiledModelQueries.GetRootShape(rootName));
        var state = new CStructOperationContext(stream, variables, this.Aligned, options) { Debug = debug, };
        try
        {
            this.HandleCStructElement(declaration, root, state, null);
        }
        finally
        {
            state.Complete();
        }

        debugData = debug ? state.DebugMapping : NoDebugData;
        return root;
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
            throw new CStructReadException(ReadFailures.PointerAddressRange, exception);
        }
    }

    /// <summary>Reads the value found at a pointer target after the address has passed safety checks.</summary>
    /// <param name="field">The pointer field or its target view; its compiled type is the target's type.</param>
    /// <param name="state">The read state, positioned at the target.</param>
    /// <param name="debugStack">The debug path for a composite target's records.</param>
    /// <param name="elementCount">The evaluated <c>@count</c> of a counted target; ignored otherwise.</param>
    /// <returns>The decoded target: one value, or the elements of a counted target.</returns>
    private object ReadPointerTargetValue(
        CompiledField field,
        CStructOperationContext state,
        DebugPath? debugStack,
        int elementCount = 1)
    {
        if (field.HasCountedTarget)
        {
            return this.ReadCountedTarget(field, state, debugStack, elementCount);
        }

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

    /// <summary>
    ///     Evaluates a counted pointer's <c>@count(N)</c> against the operation's variables and applies the array
    ///     limits, exactly as a runtime array length is checked.
    /// </summary>
    /// <param name="field">A pointer field with a counted target.</param>
    /// <param name="state">The read state whose variables hold the containing struct's fields.</param>
    /// <returns>The number of target elements.</returns>
    /// <exception cref="CStructReadException">The count is negative or cannot be evaluated.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <see cref="ReadOptions.MaxArrayElements"/>.</exception>
    private int EvaluatePointerCount(CompiledField field, CStructOperationContext state)
    {
        CompiledArrayShape elements = field.PointerElements!;
        Int128 count = elements.FixedCount ??
                    this.layoutExpressionEvaluator.Evaluate(
                        elements.CountExpression!,
                        state.Variables,
                        "pointer element count for " + field.Name,
                        ExpressionFailureDomain.Read);
        if (count < 0)
        {
            throw new CStructReadException(LayoutFailures.NegativeArrayLength(field.Name));
        }

        if (count > state.MaxArrayElements)
        {
            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(count, state.MaxArrayElements));
        }

        return (int)count;
    }

    /// <summary>
    ///     Reads the consecutive elements of a counted pointer target from the current position: characters become
    ///     a string, fixed-width numbers a typed array, and structs, unions, enums and other values a list.
    /// </summary>
    /// <param name="field">The pointer field or its target view.</param>
    /// <param name="state">The read state, positioned at the first element.</param>
    /// <param name="debugStack">The pointer's debug path; composite elements are recorded as <c>name[i]</c>.</param>
    /// <param name="count">The evaluated element count.</param>
    /// <returns>The target's value.</returns>
    private object ReadCountedTarget(CompiledField field, CStructOperationContext state, DebugPath? debugStack, int count)
    {
        CompiledField element = field.CountedElement(this.PointerSize);
        if (element.IsCharElement || element.IsWideCharElement)
        {
            Func<Stream, object> readCharacter = this.codecs.ReaderOf(element) ??
                                                 throw new InvalidOperationException("Counted text has no reader: " + field.TypeSpelling);
            var characters = new StringBuilder(count);
            for (int index = 0; index < count; index++)
            {
                characters.Append((char)readCharacter(state.Stream));
            }

            string text = state.FixedText(characters.ToString());
            if (element.IsWideCharElement)
            {
                PrimitiveCodecs.ValidateWideText(text, this.GetWideCharacterEncoding(element));
            }

            return text;
        }

        if (element.Codec.IsFixedWidthNumeric && element.Enum is null)
        {
            return count == 0 ? PrimitiveArrayReader.Empty(element.Codec) : PrimitiveArrayReader.Read(state.Stream, element.Codec, count);
        }

        var values = new List<object?>(count);
        for (int index = 0; index < count; index++)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            if (element.Type.Symbol.Definition is CompiledCompositeType composite)
            {
                // Each element starts where the previous one ended; a struct read ends after its tail padding.
                DebugPath? elementPath = state.Debug && debugStack is not null ? new DebugPath(debugStack.Parent, debugStack.Name + "[" + index + "]") : debugStack;
                values.Add(this.ParseCompiledStructAt(state, state.Stream.Position, composite, elementPath, state.StructureDepth, state.PointerDereferenceDepth, state.Debug).Result);
            }
            else if (element.Enum is { } enm)
            {
                values.Add(this.ReadEnumValue(element, enm, state.Stream));
            }
            else
            {
                values.Add(this.codecs.ReaderOf(element)?.Invoke(state.Stream) ??
                           throw new InvalidOperationException("Counted target has no reader: " + field.TypeSpelling));
            }
        }

        return values;
    }

    /// <summary>Decodes one enum through its validated backing domain and declaration-order symbolic table.</summary>
    private EnumValueResult ReadEnumValue(CompiledField field, CompiledEnumType enm, Stream stream)
    {
        object storageValue = this.codecs.ReaderOf(field)?.Invoke(stream) ??
                              throw new InvalidOperationException(
                                  "Compiled enum has no storage reader: " + enm.Name);
        return CreateEnumValue(enm, storageValue);
    }

    /// <summary>
    ///     Decodes one bitfield from its placed storage unit, the one rule the reader and the address resolver share: the
    ///     field's own bits, as an <see cref="int"/> below 32 bits and a <see cref="ulong"/> otherwise, and for an enum or
    ///     flag bitfield the enum result of those bits.
    /// </summary>
    /// <param name="field">The bitfield.</param>
    /// <param name="unit">The storage unit's value as read.</param>
    /// <param name="bitOffset">The field's bit offset in the unit, in declaration order.</param>
    /// <param name="unitBits">The unit's width in bits.</param>
    /// <returns>The field's value.</returns>
    private object DecodeBitfield(CompiledField field, object unit, int bitOffset, int unitBits)
    {
        ulong extracted = BitfieldCodecTable.ExtractBitfieldValue(
            unit,
            BitfieldCodecTable.EffectiveShift(bitOffset, field.BitSize, unitBits, this.highBitFirst),
            field.BitSize);
        object content = field.BitSize < 32 ? (object)(int)extracted : extracted;
        return field.Enum is { } enm ? CreateEnumValue(enm, content) : content;
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
    /// <param name="pointerDepth">The pointer levels to read and follow.</param>
    /// <param name="field">The pointer field, which describes the final target.</param>
    /// <param name="state">The read state, positioned at the stored address.</param>
    /// <param name="debugStack">The debug path for the target's records.</param>
    /// <param name="elementCount">The evaluated @count of a counted target, or -1 to evaluate it at the final level.</param>
    /// <returns>The pointer, dereferenced when it was followed.</returns>
    private Pointer ReadPointerValue(
        int pointerDepth,
        CompiledField field,
        CStructOperationContext state,
        DebugPath? debugStack,
        int elementCount = -1)
    {
        // Reading the address always advances the parent stream by exactly one pointer storage width.
        long address = this.ReadPointerAddress(state);
        return this.FollowPointerAddress(address, pointerDepth, field, state, debugStack, elementCount);
    }

    /// <summary>
    ///     Reads a pointer field inside a struct and defers its target: the returned unresolved pointer is stored in
    ///     the result now and resolved by <see cref="FollowPendingPointers"/> after the struct's last field is read.
    ///     A pointer that will not be followed (null, <c>void *</c>, following disabled, or a union view) is
    ///     returned in its final form immediately.
    /// </summary>
    /// <param name="field">The pointer field; its full pointer depth is read.</param>
    /// <param name="state">The read state; the stream advances by one pointer width.</param>
    /// <param name="debugStack">The field's debug path, kept for the target's debug records.</param>
    /// <returns>The pointer as stored in the result.</returns>
    private Pointer ReadDeferredPointer(
        CompiledField field,
        CStructOperationContext state,
        DebugPath? debugStack)
    {
        long address = this.ReadPointerAddress(state);
        if (address == 0 || !state.DereferencePointers || state.SuppressPointerDereference ||
            (field.PointerDepth == 1 && field.Type.TerminalName == "void"))
        {
            // None of these seeks, so the immediate form is the final one.
            return this.FollowPointerAddress(address, field.PointerDepth, field, state, debugStack);
        }

        var placeholder = new Pointer(address, null, field.PointerDepth, false);
        state.PendingPointers.Add(new PendingPointer(placeholder, field, debugStack, state.Stream.Position));
        return placeholder;
    }

    /// <summary>
    ///     Follows the pointers a struct deferred, in field order, and resolves each stored pointer in place. The
    ///     struct's own fields are all read, so each target's count and every later sibling are available as layout
    ///     variables. The stream position is left for the caller to restore.
    /// </summary>
    /// <param name="state">The read state holding the deferred pointers.</param>
    /// <param name="start">The number of deferred pointers that belong to enclosing structs and stay queued.</param>
    private void FollowPendingPointers(CStructOperationContext state, int start)
    {
        List<PendingPointer> pending = state.PendingPointers;
        int end = pending.Count;

        // A target struct defers and follows its own pointers before its read returns, so each follow leaves the
        // list exactly as long as it found it and the indexes below stay valid.
        for (int index = start; index < end; index++)
        {
            PendingPointer entry = pending[index];
            try
            {
                state.Stream.Position = entry.AddressEnd;

                // The count is evaluated before any pointer level is checked or followed, in the order generated
                // readers use, so both report the same first failure.
                int elementCount = entry.Field.HasCountedTarget ? this.EvaluatePointerCount(entry.Field, state) : 1;
                object? target = this.FollowPointerTarget(entry.Placeholder.Address, entry.Field.PointerDepth, entry.Field, state, entry.DebugStack, elementCount);
                if (target is not null)
                {
                    entry.Placeholder.Resolve(target);
                }
            }
            catch (CStructException exception) when (exception.NoteMember(entry.Field.Name, entry.Field.DisplayTypeSpelling))
            {
                // Never entered: the filter names the pointer field, as a failure during its own read would.
                throw;
            }
        }

        pending.RemoveRange(start, end - start);
    }

    /// <summary>
    ///     Follows an already read pointer address to its target when the options allow it, and restores the stream
    ///     position afterwards so the pointer consumes only its address in the parent layout.
    /// </summary>
    /// <param name="address">The stored address; 0 is the null pointer.</param>
    /// <param name="pointerDepth">The pointer levels still to follow, at least 1.</param>
    /// <param name="field">The pointer field, which describes the final target.</param>
    /// <param name="state">The read state with the pointer limits and cycle tracking.</param>
    /// <param name="debugStack">The debug path for the target's records.</param>
    /// <param name="elementCount">The evaluated @count of a counted target, or -1 to evaluate it when the final level is reached.</param>
    /// <returns>The pointer, dereferenced when it was followed.</returns>
    /// <exception cref="CStructReadException">The target is outside the input, cyclic, or cannot be decoded.</exception>
    private Pointer FollowPointerAddress(
        long address,
        int pointerDepth,
        CompiledField field,
        CStructOperationContext state,
        DebugPath? debugStack,
        int elementCount = -1)
    {
        object? target = this.FollowPointerTarget(address, pointerDepth, field, state, debugStack, elementCount);
        return target is null ? new Pointer(address, null, pointerDepth, false) : new Pointer(address, target, pointerDepth, true);
    }

    /// <summary>
    ///     Follows an already read pointer address and returns its decoded target, or <see langword="null"/> when the
    ///     pointer is null or is not followed (following disabled, a union view, or a <c>void *</c>). The stream
    ///     position is restored afterwards.
    /// </summary>
    /// <param name="address">The stored address; 0 is the null pointer.</param>
    /// <param name="pointerDepth">The pointer levels still to follow, at least 1.</param>
    /// <param name="field">The pointer field, which describes the final target.</param>
    /// <param name="state">The read state with the pointer limits and cycle tracking.</param>
    /// <param name="debugStack">The debug path for the target's records.</param>
    /// <param name="elementCount">The evaluated @count of a counted target, or -1 to evaluate it when the final level is reached.</param>
    /// <returns>The target value (another <see cref="Pointer"/> above the last level), or <see langword="null"/>.</returns>
    /// <exception cref="CStructReadException">The target is outside the input, cyclic, or cannot be decoded.</exception>
    private object? FollowPointerTarget(
        long address,
        int pointerDepth,
        CompiledField field,
        CStructOperationContext state,
        DebugPath? debugStack,
        int elementCount)
    {
        if (address == 0)
        {
            // A null pointer has no target to seek to and is represented explicitly without dereferencing.
            return null;
        }

        if (!state.DereferencePointers || state.SuppressPointerDereference)
        {
            // Callers can inspect addresses only; retain that choice on the Pointer result for downstream consumers.
            return null;
        }

        if (pointerDepth == 1 && field.Type.TerminalName == "void")
        {
            // A `void *` (or a function pointer) is an opaque address: there is nothing typed to read at its target.
            return null;
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
            throw new CStructReadException(ReadFailures.RelativePointerOverflow, exception);
        }

        if (targetAddress < 0 || targetAddress >= state.Stream.Length)
        {
            throw new CStructReadException(ReadFailures.PointerTargetOutside(targetAddress));
        }

        if (elementCount < 0)
        {
            // Only a pointer followed in place gets here without a count; a deferred pointer evaluated it up front.
            elementCount = pointerDepth == 1 && field.HasCountedTarget ? this.EvaluatePointerCount(field, state) : 1;
        }

        // Apply the optional fixed-target budget before seeking, preventing unexpectedly large referenced reads.
        this.EnsurePointerTargetSize(pointerDepth, field, state, elementCount);

        (long Address, string TypeName, int PointerDepth) targetKey =
            (targetAddress, field.TypeSpelling, pointerDepth);

        // The same target on the active path means a cycle. Detect it before recursive reads can loop forever.
        if (!state.ActivePointerTargets.Add(targetKey))
        {
            throw new CStructReadException(ReadFailures.CyclicPointer(targetAddress));
        }

        state.CancellationToken.ThrowIfCancellationRequested();
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
                                                       debugStack,
                                                       elementCount)
                               : this.ReadPointerTargetValue(field, state, debugStack, elementCount);
            return value;
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
    /// <param name="pointerDepth">The pointer levels still to follow; above 1 the target is another pointer.</param>
    /// <param name="field">The pointer field.</param>
    /// <param name="state">The read state holding the limit.</param>
    /// <param name="elementCount">The number of target elements: the evaluated <c>@count</c>, otherwise 1.</param>
    private void EnsurePointerTargetSize(
        int pointerDepth,
        CompiledField field,
        CStructOperationContext state,
        int elementCount)
    {
        if (!state.MaxPointerTargetBytes.HasValue)
        {
            // No configured budget means the existing pointer behavior remains unrestricted.
            return;
        }

        long? targetSize = pointerDepth > 1
                               ? this.PointerSize
                               : field.HasCountedTarget
                                   ? field.Type.Symbol.FixedSize * (long)elementCount
                                   : this.GetFixedTargetSize(field);
        if (!targetSize.HasValue)
        {
            // A fixed budget cannot safely approve a string or an unsized structure whose eventual length is unknown.
            throw new CStructReadLimitException(ReadFailures.PointerTargetVariableLength);
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

    /// <summary>
    ///     Reads one compiled field into <paramref name="container"/>: a scalar, an array of any element kind, or a
    ///     <c>: 0</c> separator (which reads nothing). Layout variables the field supplies are captured as it is read.
    /// </summary>
    /// <param name="compiledField">The field.</param>
    /// <param name="container">The value receiving the field.</param>
    /// <param name="state">The read state: stream, limits, variables, and bitfield unit.</param>
    /// <param name="debugStack">The parent path for debug records, or <see langword="null"/>.</param>
    /// <param name="unionPosition">The union's start when the field is a union member; -1 otherwise.</param>
    /// <param name="cursor">The containing struct's placement cursor, or <see langword="null"/> for a standalone field.</param>
    /// <param name="positionIsResolvedTarget">Whether the stream is already at the field's resolved address.</param>
    private void ReadField(
        CompiledField compiledField,
        StructValue container,
        CStructOperationContext state,
        DebugPath? debugStack,
        long unionPosition,
        CompositeFieldPlacementCursor? cursor,
        bool positionIsResolvedTarget)
    {
        if (compiledField.IsZeroWidthBitfield)
        {
            // A `: 0` separator has no bytes and no value; the cursor applies its placement effect.
            if (cursor is not null && unionPosition == -1)
            {
                (long separatorEnd, _, _) = cursor.AdvanceToField(compiledField);
                state.Stream.Position = separatorEnd;
                state.NextPosition = separatorEnd;
            }

            state.ResetBitfieldUnit();
            return;
        }

        // An unsized character array is a terminated string in this layout language; the compiled view already
        // carries its terminated codec.
        Func<Stream, object>? fieldReader = compiledField.Array.Kind == CompiledArrayKind.Flexible
                                                ? this.codecs.TerminatedReaderOf(compiledField)
                                                : this.codecs.ReaderOf(compiledField);

        // A scalar, the most common field, skips the call.
        int count = compiledField.Array.Kind == CompiledArrayKind.Scalar ? 1 : this.DeclaredElementCount(compiledField, state);

        if (state.Debug)
        {
            // Add this field after its parent struct so debug records identify the complete layout path.
            // An unnamed padding field shows as `_`, the name it was declared with.
            debugStack = new DebugPath(debugStack, compiledField.Name.Length == 0 && compiledField.BitSize == 0 && !compiledField.IsInlineComposite ? "_" : compiledField.Name);
        }

        bool standalone = PlaceField(compiledField, state, unionPosition, cursor);
        if (compiledField.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            // The placement put the stream at the field start; count the whole elements from there.
            int elementSize = compiledField.FixedElementSize ??
                              throw new InvalidOperationException("Data-sized array has no fixed element size: " + compiledField.Name);
            count = compiledField.Array.Kind == CompiledArrayKind.ToEnd
                        ? DynamicArrayExtent.CountToEnd(state.Stream, state.Stream.Position, elementSize, state.MaxArrayElements, compiledField.Name)
                        : DynamicArrayExtent.CountTerminated(state.Stream, state.Stream.Position, elementSize, state.MaxArrayElements, compiledField.Name);
        }

        bool isArray = compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or
                           CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;
        var read = new FieldRead(compiledField, container, debugStack, unionPosition, standalone, positionIsResolvedTarget, isArray, fieldReader);
        if (isArray && !compiledField.IsPointer && BoundedTextCodec.IsType(compiledField.TypeSpelling))
        {
            this.ReadBoundedTextField(in read, state, count, cursor);
            return;
        }

        bool charactersRead = false;
        int firstElement = isArray ? this.ReadArrayFastPaths(in read, state, count, cursor, out charactersRead) : 0;
        for (int index = firstElement; index < count; index++)
        {
            this.ReadFieldElement(in read, state, index, count);
        }

        if (compiledField.Array.Kind == CompiledArrayKind.Terminated)
        {
            // The all-zero terminator element belongs to the field but not to its value.
            long terminatorEnd = checked(state.Stream.Position + (compiledField.FixedElementSize ?? 0));
            state.Stream.Position = terminatorEnd;
            state.NextPosition = terminatorEnd;
        }

        if (!standalone && compiledField.BitSize == 0)
        {
            // Bitfields skip this: the cursor already reserved their whole storage unit's span when it opened,
            // mirroring how CStructAddressResolver's own cursor usage never completes a bitfield.
            cursor!.CompleteField(state.Stream.Position);
        }

        if (isArray)
        {
            this.FinishArray(in read, state, charactersRead);
        }
    }

    /// <summary>
    ///     The element count the declaration gives before the field is placed: 1 for a scalar or a terminated string, the
    ///     product of a multidimensional array's fixed dimensions, or a one-dimensional count evaluated against the
    ///     variables read so far. A data-sized array is counted after placement, from the data.
    /// </summary>
    /// <param name="compiledField">The field.</param>
    /// <param name="state">The read state, whose variables and limits apply.</param>
    /// <returns>The count.</returns>
    /// <exception cref="CStructReadException">The count is negative.</exception>
    /// <exception cref="CStructReadLimitException">The count is past <c>MaxArrayElements</c>.</exception>
    private int DeclaredElementCount(CompiledField compiledField, CStructOperationContext state)
    {
        if (compiledField.Array.Kind is CompiledArrayKind.Scalar or CompiledArrayKind.Flexible or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            return 1;
        }

        // The count is checked in the expression domain, so a uint64 count beyond Int32 fails the limit check with
        // its exact value instead of wrapping.
        Int128 count;
        if (compiledField.Array.Dimensions.Length > 1)
        {
            // Every dimension of a multidimensional array is fixed, so the total leaf count is known without evaluating
            // an expression. Elements are still read in flat row-major order; the shape is built afterwards.
            count = compiledField.Array.TotalFixedElementCount ??
                    throw new InvalidOperationException("Multidimensional array has no fixed total element count: " + compiledField.Name);
        }
        else
        {
            // Evaluate the count only after earlier fields have populated the variables.
            count = this.layoutExpressionEvaluator.Evaluate(
                compiledField.Array.CountExpression ??
                throw new InvalidOperationException("Compiled array has no count expression: " + compiledField.Name),
                state.Variables,
                "array length for " + compiledField.Name,
                ExpressionFailureDomain.Read);
            if (count < 0)
            {
                throw new CStructReadException(LayoutFailures.NegativeArrayLength(compiledField.Name));
            }
        }

        if (count > state.MaxArrayElements)
        {
            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(count, state.MaxArrayElements));
        }

        return (int)count;
    }

    /// <summary>
    ///     Places a field: a union member at the union's start, a struct member where the composite cursor puts it (with
    ///     its bitfield unit, once for every array element). A standalone field - a root declaration, a union member,
    ///     or a resolved path target - has no cursor and starts with no open bitfield unit.
    /// </summary>
    /// <param name="compiledField">The field.</param>
    /// <param name="state">The read state, whose stream and bitfield unit are set.</param>
    /// <param name="unionPosition">The union's start, or -1.</param>
    /// <param name="cursor">The containing struct's cursor, or <see langword="null"/>.</param>
    /// <returns>Whether the field is standalone.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PlaceField(CompiledField compiledField, CStructOperationContext state, long unionPosition, CompositeFieldPlacementCursor? cursor)
    {
        if (unionPosition != -1)
        {
            // Rewind before each union member so all interpretations use the same bytes.
            state.Stream.Position = unionPosition;
            state.ResetBitfieldUnit();
        }

        bool standalone = cursor is null || unionPosition != -1;
        if (!standalone)
        {
            (long fieldStart, int bitOffset, int unitSize) = cursor!.AdvanceToField(compiledField);
            state.Stream.Position = fieldStart;
            if (compiledField.BitSize > 0)
            {
                state.CurrentBitOffset = bitOffset;
                state.BitfieldUnitOpen = true;
                state.CurrentBitfieldSize = unitSize;
            }
            else
            {
                state.ResetBitfieldUnit();
            }
        }

        return standalone;
    }

    /// <summary>Reads an encoded text buffer (<c>utf8 text[N]</c> and the like) as one string.</summary>
    /// <param name="read">The field being read.</param>
    /// <param name="state">The read state.</param>
    /// <param name="capacity">The buffer's declared element count.</param>
    /// <param name="cursor">The containing struct's cursor, or <see langword="null"/>.</param>
    private void ReadBoundedTextField(in FieldRead read, CStructOperationContext state, int capacity, CompositeFieldPlacementCursor? cursor)
    {
        CompiledField compiledField = read.Field;
        long start = state.Stream.Position;
        string text = state.FixedText(PrimitiveCodecs.ReadBoundedText(state.Stream, capacity, compiledField.TypeSpelling));
        long end = state.Stream.Position;
        ((IDictionary<string, object?>)read.Container)[compiledField.Name] = text;
        if (state.Debug)
        {
            state.RegisterDebugData(start, end, read.DebugStack, text, compiledField.DisplayTypeSpelling);
        }

        state.NextPosition = end;
        if (!read.Standalone)
        {
            cursor!.CompleteField(end);
        }
    }

    /// <summary>
    ///     Reads what an array's fast paths can and prepares the element list for the rest: a numeric array in one block
    ///     (a one-dimensional one as a typed array), a one-dimensional array of a fully fixed struct through its static
    ///     plan, and a <c>char[N]</c> as one block. Each applies only where the per-element loop would have no other
    ///     observable effect.
    /// </summary>
    /// <param name="read">The array field.</param>
    /// <param name="state">The read state.</param>
    /// <param name="count">The element count.</param>
    /// <param name="cursor">The containing struct's cursor, or <see langword="null"/>.</param>
    /// <param name="charactersRead">Whether the characters were read as one string.</param>
    /// <returns>The index of the first element the per-element loop still reads.</returns>
    private int ReadArrayFastPaths(in FieldRead read, CStructOperationContext state, int count, CompositeFieldPlacementCursor? cursor, out bool charactersRead)
    {
        CompiledField compiledField = read.Field;
        IDictionary<string, object?> containerDict = read.Container;
        charactersRead = false;

        // Bulk path for arrays of fixed-width numeric primitives: one block read and span decoding instead of the
        // per-element loop. Restricted to the shapes whose per-element side effects are exactly reproducible there:
        // cursor placement (the field start is already set), no debug records, no union rewinds, no bitfields,
        // pointers, enums, structs, or character types.
        bool bulkNumeric = !state.Debug && cursor is not null && read.UnionPosition == -1 && compiledField.BitSize == 0 &&
                           compiledField.PointerDepth == 0 && compiledField.Composite is null && compiledField.Enum is null && compiledField.Name.Length > 0 &&
                           count > 0 && compiledField.Codec.IsFixedWidthNumeric;

        // A one-dimensional numeric array becomes a typed PrimitiveArray<T>; every other array accumulates boxed
        // elements first (fixed character arrays are converted to a string at the end).
        bool typedArray = bulkNumeric && compiledField.Array.Dimensions.Length == 1;

        // A one-dimensional char[n] is read as one block and decoded to its string at once, where the per-character
        // loop would have no other observable effect: no debug records, cursor placement outside a union, and no
        // layout variable to capture from each character. The block is taken only when the whole extent is present
        // and within the budget; otherwise the loop runs.
        bool bulkCharacters = !state.Debug && !read.Standalone && count > 0 &&
                              compiledField.IsCharElement && !compiledField.IsWideCharElement && !compiledField.IsPointer &&
                              compiledField.BitSize == 0 && compiledField.Array.Dimensions.Length == 1 && compiledField.Name.Length > 0 &&
                              !compiledField.CapturesLayoutVariable && !state.CaptureAllLayoutVariables && !state.GeneralPathOnly;
        if (!typedArray && !bulkCharacters)
        {
            containerDict[compiledField.Name] = new List<object?>(count);
        }

        int firstElement = 0;
        if (bulkNumeric)
        {
            object? lastElement;
            if (typedArray)
            {
                IList<object?> typed = PrimitiveArrayReader.Read(state.Stream, compiledField.Codec, count);
                containerDict[compiledField.Name] = typed;
                lastElement = typed[count - 1];
            }
            else
            {
                lastElement = PrimitiveArrayReader.ReadInto(state.Stream, compiledField.Codec, count, (List<object?>)containerDict[compiledField.Name]!);
            }

            state.NextPosition = state.Stream.Position;

            // An array is not an integer; the capture makes a shared name unusable, as every path does.
            if (compiledField.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
            {
                LayoutVariableCapture.Capture(state.Variables, compiledField.Name, compiledField, lastElement);
                state.PublishQualified(compiledField.Name);
            }

            firstElement = count;
        }

        // A one-dimensional array of a fully fixed struct whose whole extent is in memory is read by looping the
        // element's static plan over one span instead of dispatching per element.
        if (firstElement == 0 && count > 0 && !state.Debug && !read.Standalone && !state.GeneralPathOnly &&
            compiledField.PointerDepth == 0 && compiledField.Composite is { IsUnion: false } composite && compiledField.Array.Dimensions.Length == 1)
        {
            if (composite.StaticPlan is StaticReadPlan plan && plan.Size > 0 && compiledField.FixedElementSize == plan.Size &&
                state.CoversPlan(plan) &&
                (long)count * plan.Size <= int.MaxValue &&
                state.Stream.TryReadSpanWithinBudget(count * plan.Size, out ReadOnlySpan<byte> elements))
            {
                var list = (List<object?>)containerDict[compiledField.Name]!;
                for (int element = 0; element < count; element++)
                {
                    state.CancellationToken.ThrowIfCancellationRequested();
                    var value = new StructValue(composite.Shape);
                    this.ExecuteStaticPlan(plan, elements.Slice(element * plan.Size, plan.Size), value, state);
                    list.Add(value);
                }

                state.ResetBitfieldUnit();
                state.NextPosition = state.Stream.Position;
                firstElement = count;
            }
        }

        if (bulkCharacters)
        {
            charactersRead = this.TryReadCharacterBlock(state, count, out string? characters);
            if (charactersRead)
            {
                containerDict[compiledField.Name] = characters;
                state.NextPosition = state.Stream.Position;
                firstElement = count;
            }
            else
            {
                containerDict[compiledField.Name] = new List<object?>(count);
            }
        }

        return firstElement;
    }

    /// <summary>Reads one element of a field (or the scalar itself): an enum, a nested struct or union, or a scalar, pointer, or bitfield.</summary>
    /// <param name="read">The field being read.</param>
    /// <param name="state">The read state.</param>
    /// <param name="index">The element's flat row-major index.</param>
    /// <param name="count">The field's element count.</param>
    /// <exception cref="InvalidOperationException">The field's type has no reader.</exception>
    /// <remarks>Inlined into the element loop: it only dispatches, and it runs once for every field and element read.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReadFieldElement(in FieldRead read, CStructOperationContext state, int index, int count)
    {
        CompiledField compiledField = read.Field;

        // Composite leaves need the containing element's coordinates. A primitive array's debug records are grouped
        // under the array field instead.
        DebugPath? elementDebugStack = state.Debug && read.IsArray && compiledField.TargetComposite is not null
                                           ? ElementDebugPath(in read, index, count)
                                           : read.DebugStack;

        // An enum-typed bitfield takes the primitive bit-slicing path and is wrapped afterwards.
        if (compiledField.PointerDepth == 0 && compiledField.Enum is { } enm && compiledField.BitSize == 0)
        {
            this.ReadEnumElement(in read, state, enm);
        }
        else if (compiledField.PointerDepth == 0 && compiledField.Composite is { } nested)
        {
            this.ReadNestedElement(in read, state, nested, elementDebugStack);
        }
        else if (compiledField.PointerDepth == 0 && read.Reader is null)
        {
            throw new InvalidOperationException($"No handler for field type {compiledField.DisplayTypeSpelling}");
        }
        else
        {
            this.ReadScalarElement(in read, state, elementDebugStack);
        }
    }

    /// <summary>The debug path of one element of a composite array: the field name with the element's coordinates.</summary>
    /// <param name="read">The array field; its debug path is the field's own.</param>
    /// <param name="index">The element's flat row-major index.</param>
    /// <param name="count">The field's element count, the size of a dimension without a fixed count.</param>
    /// <returns>The element's path, beside the field's under the same parent (<c>items[2][1]</c>).</returns>
    private static DebugPath ElementDebugPath(in FieldRead read, int index, int count)
    {
        CompiledField compiledField = read.Field;
        string indices = string.Empty;
        int remainingIndex = index;
        for (int dimension = compiledField.Array.Dimensions.Length - 1; dimension >= 0; dimension--)
        {
            int size = compiledField.Array.Dimensions[dimension].FixedCount ?? count;
            indices = "[" + (remainingIndex % size) + "]" + indices;
            remainingIndex /= size;
        }

        return new DebugPath(read.DebugStack!.Parent, compiledField.Name + indices);
    }

    /// <summary>Reads one enum value through its storage type and captures its number for later expressions.</summary>
    /// <param name="read">The enum field.</param>
    /// <param name="state">The read state.</param>
    /// <param name="enm">The compiled enum.</param>
    private void ReadEnumElement(in FieldRead read, CStructOperationContext state, CompiledEnumType enm)
    {
        CompiledField compiledField = read.Field;

        // The cursor already aligned the field once (not per array element); a standalone field starts where it is.
        long start = state.Stream.Position;

        EnumValueResult value = this.ReadEnumValue(compiledField, enm, state.Stream);
        if (state.Debug)
        {
            state.RegisterDebugData(start, state.Stream.Position, read.DebugStack, value.Value, compiledField.DisplayTypeSpelling);
        }

        read.Store(value);
        if (compiledField.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
        {
            LayoutVariableCapture.Capture(state.Variables, compiledField.Name, compiledField, value);
            state.PublishQualified(compiledField.Name);
        }
    }

    /// <summary>
    ///     Reads one nested struct or union value. A field named through a dotted path (<c>hdr.n</c>) republishes its
    ///     nested values under the qualified prefix while its body is read.
    /// </summary>
    /// <param name="read">The composite field.</param>
    /// <param name="state">The read state.</param>
    /// <param name="nested">The nested composite.</param>
    /// <param name="elementDebugStack">The element's debug path.</param>
    private void ReadNestedElement(in FieldRead read, CStructOperationContext state, CompiledCompositeType nested, DebugPath? elementDebugStack)
    {
        CompiledField compiledField = read.Field;
        if (read.Standalone && !read.PositionIsResolvedTarget)
        {
            PrepareNestedStructStart(state);
        }

        string? outerPrefix = state.QualifiedPrefix;
        if (compiledField.HasQualifiedPrefix && !read.IsArray)
        {
            state.QualifiedPrefix = outerPrefix is null ? compiledField.QualifiedPrefix : outerPrefix + compiledField.QualifiedPrefix;
        }

        object value;
        if (nested.IsUnion)
        {
            value = this.ReadUnionValue(nested, state, elementDebugStack);
        }
        else
        {
            var container = new StructValue(nested.Shape);
            this.ReadCompiledStructInto(nested, container, state, elementDebugStack);
            value = container;
        }

        state.QualifiedPrefix = outerPrefix;
        read.Store(value);
    }

    /// <summary>
    ///     Reads one scalar, pointer, or bitfield value. A bitfield reads its storage unit and exposes only its own bits;
    ///     while later bitfields share the unit the stream stays at the unit's start. An anonymous bitfield is padding:
    ///     its bits are read (and appear in debug output) but it stores and captures nothing.
    /// </summary>
    /// <param name="read">The field.</param>
    /// <param name="state">The read state.</param>
    /// <param name="elementDebugStack">The element's debug path.</param>
    /// <exception cref="CStructReadException">A bitfield overruns its storage unit.</exception>
    private void ReadScalarElement(in FieldRead read, CStructOperationContext state, DebugPath? elementDebugStack)
    {
        CompiledField compiledField = read.Field;
        if (read.Standalone && compiledField.BitSize > 0 && state.BitfieldUnitSeeded)
        {
            // A resolved target arrives with its placed unit; nothing to derive.
            state.BitfieldUnitSeeded = false;
        }
        else if (read.Standalone && compiledField.BitSize > 0)
        {
            // A standalone bitfield opens its own storage unit.
            state.BitfieldUnitOpen = true;
            state.CurrentBitfieldSize = compiledField.BitStorageSize ??
                                        throw new InvalidOperationException("Compiled bitfield has no storage size: " + compiledField.Name);
        }

        // A standalone field - a root, a union member, a pointer target or a resolved target - starts exactly where the
        // stream is: alignment is measured from the start of the value being read, never from the stream's origin.
        long start = state.Stream.Position;

        // A bitfield whose placed unit differs from its declared type (a packed SysV window) is read as a raw unsigned
        // unit of that size. Fixed-width numerics decode straight from a memory-backed stream; every other codec, and
        // every other stream, takes the field's reader. Inside a struct traversal a pointer's target is followed after
        // the struct's last field, so its @count may name a later field; elsewhere it is followed now.
        bool windowedUnit = compiledField.BitSize > 0 && state.CurrentBitfieldSize != compiledField.Codec.Size;
        object content = compiledField.PointerDepth > 0
                             ? read.Standalone || !compiledField.FollowsAfterStruct
                                 ? this.ReadPointerValue(compiledField.PointerDepth, compiledField, state, elementDebugStack)
                                 : this.ReadDeferredPointer(compiledField, state, elementDebugStack)
                             : windowedUnit
                                 ? BinaryPrimitiveIO.ReadBitfieldUnit(state.Stream, state.CurrentBitfieldSize, compiledField.BitStorageIsLittleEndian ?? true)
                                 : compiledField.Codec.IsFixedWidthNumeric && state.Stream.TryReadSpan(compiledField.Codec.Size, out ReadOnlySpan<byte> numericBytes)
                                     ? compiledField.Codec.ReadNumeric(numericBytes)
                                     : read.Reader?.Invoke(state.Stream) ??
                                       throw new InvalidOperationException("Compiled field has no reader: " + compiledField.DisplayTypeSpelling);

        // Remember the full primitive range before bitfield handling possibly rewinds for another slice.
        long end = state.Stream.Position;
        long finalEnd = end;
        if (compiledField.BitSize > 0)
        {
            int unitBits = checked(state.CurrentBitfieldSize * 8);
            if (state.CurrentBitOffset + compiledField.BitSize > unitBits)
            {
                throw new CStructReadException(LayoutFailures.BitfieldExceedsUnit(compiledField.Name));
            }

            content = this.DecodeBitfield(compiledField, content, state.CurrentBitOffset, unitBits);
            state.CurrentBitOffset += compiledField.BitSize;
            if (1 + (state.CurrentBitOffset / 8) > end - start)
            {
                state.CurrentBitOffset -= unitBits;
                state.BitfieldUnitOpen = false;
            }
            else
            {
                state.Stream.Position = start;
                state.NextPosition = end;
                finalEnd = start;
            }
        }
        else
        {
            state.NextPosition = end;
        }

        if (state.Debug)
        {
            // Debug collection rereads the full source bytes and then restores the logical parser position.
            state.RegisterDebugData(start, end, elementDebugStack, content, compiledField.DisplayTypeSpelling);
            state.Stream.Position = finalEnd;
        }

        if (compiledField.Name.Length == 0)
        {
            return;
        }

        read.Store(content);
        if (compiledField.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
        {
            // Later counts and expressions read the value through the field's name; see LayoutVariableCapture for the
            // rule every path shares.
            LayoutVariableCapture.Capture(state.Variables, compiledField.Name, compiledField, content);
            if (state.HasQualifiedPrefix)
            {
                state.PublishQualified(compiledField.Name);
            }
        }
    }

    /// <summary>
    ///     Gives an array its final shape: a multidimensional array nests its flat elements by dimension (a character
    ///     table's rows become strings), and a one-dimensional character array becomes its string.
    /// </summary>
    /// <param name="read">The array field.</param>
    /// <param name="state">The read state.</param>
    /// <param name="charactersRead">Whether the characters were already read as one string.</param>
    /// <exception cref="CStructReadException">Wide characters do not form valid UTF-16.</exception>
    private void FinishArray(in FieldRead read, CStructOperationContext state, bool charactersRead)
    {
        CompiledField compiledField = read.Field;
        IDictionary<string, object?> containerDict = read.Container;
        bool isCharacterElement = !compiledField.IsPointer && (compiledField.IsCharElement || compiledField.IsWideCharElement);
        if (compiledField.Array.Dimensions.Length > 1)
        {
            int[] dimensionSizes = FixedDimensionSizes(compiledField);
            var flatValues = (List<object?>)containerDict[compiledField.Name]!;
            if (!isCharacterElement)
            {
                containerDict[compiledField.Name] = ReshapeFlatArrayValues(flatValues, dimensionSizes);
                return;
            }

            // The innermost dimension of a fixed string table (char names[10][32]) collapses to a string, like a
            // one-dimensional char[32]; every outer dimension nests around those rows like any other element type.
            int rowSize = dimensionSizes[^1];
            var rows = new List<object?>(flatValues.Count / rowSize);
            for (int start = 0; start < flatValues.Count; start += rowSize)
            {
                rows.Add(this.CharacterText(compiledField, state, flatValues.GetRange(start, rowSize)));
            }

            containerDict[compiledField.Name] = ReshapeFlatArrayValues(rows, dimensionSizes[..^1]);
        }
        else if (isCharacterElement && !charactersRead)
        {
            // Expose a fixed character array as the string callers expect, after every character has been read.
            containerDict[compiledField.Name] = this.CharacterText(compiledField, state, (List<object?>)containerDict[compiledField.Name]!);
        }
        else if (containerDict[compiledField.Name] is List<object?> elements && PrimitiveArrayReader.IsTyped(compiledField))
        {
            // Elements read one at a time - a debug parse, a union member view, a selected or empty array - take the
            // typed shape the bulk path gives, so a numeric array has one shape wherever it is read.
            containerDict[compiledField.Name] = PrimitiveArrayReader.FromBoxed(PrimitiveArrayReader.GetElementType(compiledField.Codec), elements);
        }
    }

    /// <summary>The text of a row of characters read one by one, trimmed as the options say; wide characters must be valid UTF-16.</summary>
    /// <param name="compiledField">The character array field.</param>
    /// <param name="state">The read state, whose fixed-text trimming applies.</param>
    /// <param name="characters">The boxed characters.</param>
    /// <returns>The text.</returns>
    /// <exception cref="CStructReadException">Wide characters do not form valid UTF-16.</exception>
    private string CharacterText(CompiledField compiledField, CStructOperationContext state, List<object?> characters)
    {
        string text = state.FixedText(new string(characters.Cast<char>().ToArray()));
        if (compiledField.IsWideCharElement)
        {
            PrimitiveCodecs.ValidateWideText(text, this.GetWideCharacterEncoding(compiledField));
        }

        return text;
    }

    /// <summary>The facts every step of one field's read shares, passed by reference so the steps copy nothing.</summary>
    private readonly struct FieldRead
    {
        /// <summary>Captures one field's read.</summary>
        /// <param name="field">The field.</param>
        /// <param name="container">The value receiving the field.</param>
        /// <param name="debugStack">The field's debug path.</param>
        /// <param name="unionPosition">The union's start, or -1.</param>
        /// <param name="standalone">Whether the field has no composite cursor.</param>
        /// <param name="positionIsResolvedTarget">Whether the stream is at the field's resolved address.</param>
        /// <param name="isArray">Whether the field is an array.</param>
        /// <param name="reader">The field's element reader, or <see langword="null"/>.</param>
        public FieldRead(CompiledField field, StructValue container, DebugPath? debugStack, long unionPosition, bool standalone, bool positionIsResolvedTarget, bool isArray, Func<Stream, object>? reader)
        {
            this.Field = field;
            this.Container = container;
            this.DebugStack = debugStack;
            this.UnionPosition = unionPosition;
            this.Standalone = standalone;
            this.PositionIsResolvedTarget = positionIsResolvedTarget;
            this.IsArray = isArray;
            this.Reader = reader;
        }

        /// <summary>Gets the field.</summary>
        public CompiledField Field { get; }

        /// <summary>Gets the value receiving the field.</summary>
        public StructValue Container { get; }

        /// <summary>Gets the field's debug path.</summary>
        public DebugPath? DebugStack { get; }

        /// <summary>Gets the union's start, or -1.</summary>
        public long UnionPosition { get; }

        /// <summary>Gets whether the field has no composite cursor.</summary>
        public bool Standalone { get; }

        /// <summary>Gets whether the stream is at the field's resolved address.</summary>
        public bool PositionIsResolvedTarget { get; }

        /// <summary>Gets whether the field is an array.</summary>
        public bool IsArray { get; }

        /// <summary>Gets the field's element reader, or <see langword="null"/>.</summary>
        public Func<Stream, object>? Reader { get; }

        /// <summary>Stores one element: appended to the array's list, or as the field's value.</summary>
        /// <param name="value">The element.</param>
        public void Store(object? value)
        {
            IDictionary<string, object?> container = this.Container;
            if (this.IsArray)
            {
                ((List<object?>)container[this.Field.Name]!).Add(value);
            }
            else
            {
                container[this.Field.Name] = value;
            }
        }
    }
}
