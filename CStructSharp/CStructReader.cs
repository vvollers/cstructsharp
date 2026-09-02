namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using CStructSharp.Structure;
using Pidgin;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>
///     Contains the stream-reading half of <see cref="CStruct"/>.
///     These methods turn compiled layout elements into nested <see cref="ExpandoObject"/> values while keeping pointer and debug state together.
/// </summary>
public partial class CStruct
{
    /// <summary>Moves a nested struct to the same compiled parent boundary used by size, write, and address operations.</summary>
    private void PrepareNestedStructStart(
        Struct strct,
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

        int alignment = this.GetCompiledComposite(strct).Symbol.Alignment;
        state.Stream.Position = this.AlignUp(state.Stream.Position, alignment);
    }

    /// <summary>
    ///     Reads one layout element and adds its value to the current object.
    ///     Structs, typedefs, fields, arrays, unions, pointers, and debug tracking all meet here so they advance through the stream consistently.
    /// </summary>
    private void HandleCStructElement(
        CStructElement el,
        ExpandoObject currentContainer,
        CStructOperationContext state,
        CStructElement[] debugStack,
        long unionPosition = -1,
        bool alignInlineStructStart = false,
        CompiledField? fieldDescriptor = null)
    {
        // A typedef can resolve to another element, so loop until this call reaches a concrete struct, field, or define.
        while (true)
        {
            switch (el)
            {
            case Struct s:
                {
                    if (alignInlineStructStart)
                    {
                        // Inline structs arrive here as Struct instances rather than ordinary Field instances. Prepare
                        // their parent boundary explicitly so they follow the same bitfield and alignment rule as a
                        // named struct field.
                        this.PrepareNestedStructStart(s, state, unionPosition);
                    }

                    if (s.IsUnion)
                    {
                        CStructElement[] unionDebugStack = state.Debug ? [.. debugStack, s,] : debugStack;
                        IDictionary<string, object?> currentContainerDict = currentContainer;
                        currentContainerDict[s.Name.Name] = this.ReadUnionValue(s, state, unionDebugStack);
                        break;
                    }

                    // Give every struct its own dynamic object, then attach it before reading children so nested paths are preserved.
                    dynamic newContainer = new ExpandoObject();
                    IDictionary<string, object?> structContainer = currentContainer;
                    string newName = s.Name.Name;

                    structContainer[newName] = newContainer;

                    if (state.Debug)
                    {
                        // Extend the layout stack only for debug output; normal parsing does not need this allocation.
                        debugStack = [.. debugStack, s,];
                    }

                    this.ReadCompiledStructInto(s, newContainer, state, debugStack);

                    break;
                }

            case Typedef t:
                {
                    if (state.Debug)
                    {
                        // Preserve the alias in debug metadata even though its underlying type does the actual reading.
                        debugStack = [.. debugStack, t,];
                    }

                    if (t.Struct is not null)
                    {
                        // Preserve the established root-inline-typedef shape: the object is named after its inline
                        // struct declaration, while an alias to a separately named struct retains the alias name.
                        fieldDescriptor = null;
                        el = t.Struct;
                        unionPosition = -1;
                        continue;
                    }

                    // Root aliases use a precompiled field projection, including aliases of structs and pointers.
                    fieldDescriptor = this.GetCompiledRootField(t);
                    el = fieldDescriptor.EffectiveField;

                    unionPosition = -1;
                    continue;
                }

            case CstructEnum enm:
                // A direct enum root uses the same synthetic compiled scalar field as an enum typedef.
                fieldDescriptor = this.GetCompiledRootField(enm);
                el = fieldDescriptor.EffectiveField;
                unionPosition = -1;
                continue;

            case Defines d:
                // Definitions do not consume bytes; they prepare an expression value for array lengths and later fields.
                state.Variables[d.Name.Name] = new Literal(
                    this.EvaluateLayoutExpression(
                        d.Value,
                        state.Variables,
                        "definition " + d.Name.Name));
                break;
            case Field f:
                {
                    CompiledField compiledField = fieldDescriptor ??
                                                  throw new InvalidOperationException(
                                                      "Field execution requires a compiled descriptor: " +
                                                      f.Name.Name);
                    f = compiledField.EffectiveField;
                    CStructElement? resolvedNamedElement = compiledField.NamedElement;
                    Func<Stream, object>? fieldReader = compiledField.Reader;

                    // Begin with one value, then expand fixed arrays or translate unsized character arrays to terminated strings.
                    int numFieldValues = 1;
                    bool hasFixedArrayDeclarator =
                        compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime;

                    if (compiledField.Array.Kind != CompiledArrayKind.Scalar)
                    {
                        if (compiledField.Array.Kind == CompiledArrayKind.Flexible)
                        {
                            // An unsized char array means a terminated string in this layout language.
                            if (f.Type.Equals(CharType))
                            {
                                f = new Field(CstringType, f.Name, NoneExpr.Instance, 0);
                                fieldReader = compiledField.TerminatedReader;
                            }
                            else if (f.Type.Equals(WcharType))
                            {
                                f = new Field(StringType, f.Name, NoneExpr.Instance, 0);
                                fieldReader = compiledField.TerminatedReader;
                            }
                            else if (IsWideCharacterType(f.Type))
                            {
                                f = new Field(
                                    new Identifier(GetStringPointerHandlerKey(f.Type)),
                                    f.Name,
                                    NoneExpr.Instance,
                                    0);
                                fieldReader = compiledField.TerminatedReader;
                            }
                        }
                        else
                        {
                            // Evaluate the count only after earlier fields have populated the parser variable dictionary.
                            numFieldValues = this.EvaluateLayoutExpression(
                                compiledField.Array.CountExpression ??
                                throw new InvalidOperationException(
                                    "Compiled array has no count expression: " + f.Name.Name),
                                state.Variables,
                                "array length for " + f.Name.Name);
                            if (numFieldValues < 0)
                            {
                                throw new CStructReadException("Array length cannot be negative: " + f.Name.Name);
                            }

                            if (numFieldValues > state.MaxArrayElements)
                            {
                                throw new CStructReadLimitException(
                                    "Array length exceeds the configured limit: " + f.Name.Name);
                            }
                        }
                    }

                    if (state.Debug)
                    {
                        // Add this field after its parent struct so debug records identify the complete layout path.
                        debugStack = [.. debugStack, f,];
                    }

                    IDictionary<string, object?> containerDict = currentContainer;

                    bool isArray = hasFixedArrayDeclarator;
                    if (isArray)
                    {
                        // Accumulate array elements first; fixed character arrays are converted to a string after the loop.
                        containerDict[f.Name.Name] = new List<object?>();
                    }

                    if (unionPosition != -1)
                    {
                        // Rewind before each union member so all interpretations use the same bytes.
                        state.Stream.Position = unionPosition;
                        state.CurrentBitOffset = 0;
                        state.CurrentBitfieldType = null;
                    }

                    if (state.CurrentBitOffset > 0 && f.BitSize == 0)
                    {
                        // A composite or primitive field after a partially used bitfield unit begins after the complete
                        // storage unit. Doing this before type dispatch keeps enums and named structs on the same rule.
                        state.Stream.Position = state.NextPosition;
                        state.CurrentBitOffset = 0;
                        state.CurrentBitfieldType = null;
                    }

                    string fieldTypeName = f.Type.Name;

                    bool isKnownStruct = resolvedNamedElement is not null;
                    CStructElement? structElement = resolvedNamedElement;
                    bool isKnownFieldType = fieldReader is not null;

                    for (int i = 0; i < numFieldValues; i++)
                    {
                        if (f.PointerDepth == 0 && isKnownStruct)
                        {
                            // Structs and enums have layout-aware readers rather than primitive byte handlers.
                            dynamic newContainer = new ExpandoObject();

                            switch (structElement)
                            {
                            case CstructEnum enm:
                                {
                                    // Align the enum's primitive storage before reading its numeric representation.
                                    long curPos = state.Stream.Position;

                                    if (state.Aligned && unionPosition == -1)
                                    {
                                        int structAlignment = compiledField.Alignment;
                                        state.Stream.Position = this.AlignUp(curPos, structAlignment);
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
                                        ((List<object?>)containerDict[f.Name.Name]!).Add(newEnum);
                                    }
                                    else
                                    {
                                        containerDict[f.Name.Name] = newEnum;
                                    }

                                    if (!isArray)
                                    {
                                        this.UpdateExactLayoutVariable(
                                            state.Variables,
                                            f.Name.Name,
                                            newEnum.Value);
                                    }

                                    break;
                                }

                            case Struct strct:
                                {
                                    this.PrepareNestedStructStart(strct, state, unionPosition);

                                    object nestedValue;
                                    if (strct.IsUnion)
                                    {
                                        nestedValue = this.ReadUnionValue(strct, state, debugStack);
                                    }
                                    else
                                    {
                                        this.ReadCompiledStructInto(
                                            strct,
                                            newContainer,
                                            state,
                                            debugStack);
                                        nestedValue = newContainer;
                                    }

                                    if (isArray)
                                    {
                                        ((List<object?>)containerDict[f.Name.Name]!).Add(nestedValue);
                                    }
                                    else
                                    {
                                        containerDict[f.Name.Name] = nestedValue;
                                    }

                                    break;
                                }
                            }
                        }
                        else if (f.PointerDepth == 0 && !isKnownFieldType)
                        {
                            throw new InvalidOperationException($"No handler for field type {fieldTypeName}");
                        }
                        else
                        {
                            if (f.BitSize > 0)
                            {
                                int bitCapacity = checked(
                                    (compiledField.BitStorageSize ??
                                     throw new InvalidOperationException(
                                         "Compiled bitfield has no storage size: " + f.Name.Name)) * 8);
                                int activeUnitSize = state.CurrentBitfieldType is null
                                                         ? 0
                                                         : state.CurrentBitfieldSize;
                                bool startsNewStorageUnit = state.CurrentBitOffset > 0 &&
                                                           this.StartsNewBitfieldUnit(
                                                               state.CurrentBitfieldType,
                                                               activeUnitSize,
                                                               activeUnitSize,
                                                               state.CurrentBitOffset,
                                                               f,
                                                               bitCapacity / 8,
                                                               bitCapacity / 8);
                                if (startsNewStorageUnit)
                                {
                                    state.Stream.Position = state.NextPosition;
                                    state.CurrentBitOffset = 0;
                                    state.CurrentBitfieldType = null;
                                }

                                if (state.CurrentBitOffset == 0)
                                {
                                    state.CurrentBitfieldType = f.Type.Name;
                                    state.CurrentBitfieldSize = compiledField.BitStorageSize ??
                                                                throw new InvalidOperationException(
                                                                    "Compiled bitfield has no storage size: " +
                                                                    f.Name.Name);
                                }
                            }

                            long curPos = state.Stream.Position;

                            if (state.Aligned && unionPosition == -1)
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

                                state.Stream.Position = this.AlignUp(curPos, structAlignment);
                                curPos = state.Stream.Position;

                                state.CurrentFieldAlignment = structAlignment;
                            }

                            object content = f.PointerDepth > 0
                                                 ? this.ReadPointerValue(
                                                                         f.PointerDepth,
                                                                         compiledField,
                                                                         state,
                                                                         debugStack)
                                                 : fieldReader?.Invoke(state.Stream) ??
                                                   throw new InvalidOperationException(
                                                       "Compiled field has no reader: " + fieldTypeName);

                            // Remember the full primitive range before bitfield handling possibly rewinds for another slice.
                            long endPos = state.Stream.Position;

                            long finalEndPos = endPos;
                            if (f.BitSize > 0)
                            {
                                // Read the storage value once, then expose only this field's slice of its bits.
                                int elementBitSize = checked(
                                    (compiledField.BitStorageSize ??
                                     throw new InvalidOperationException(
                                         "Compiled bitfield has no storage size: " + f.Name.Name)) * 8);
                                if (state.CurrentBitOffset + f.BitSize > elementBitSize)
                                {
                                    throw new CStructReadException("Bitfield exceeds its storage unit: " + f.Name.Name);
                                }

                                ulong extracted = ExtractBitfieldValue(
                                    content,
                                    state.CurrentBitOffset,
                                    f.BitSize);
                                content = f.BitSize < 32 ? (object)(int)extracted : extracted;

                                state.CurrentBitOffset += f.BitSize;
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
                                state.RegisterDebugData(curPos, endPos, debugStack, content, fieldTypeName);
                                state.Stream.Position = finalEndPos;
                            }

                            if (isArray)
                            {
                                ((List<object?>)containerDict[f.Name.Name]!).Add(content);
                            }
                            else
                            {
                                containerDict[f.Name.Name] = content;
                            }

                            if (content is Pointer p)
                            {
                                // Expressions refer to the encoded pointer address, not the Pointer wrapper or target value.
                                try
                                {
                                    state.Variables[f.Name.Name] = new Literal(Convert.ToInt32(p.Address));
                                }
                                catch (OverflowException)
                                {
                                    // A valid signed stream address may exceed the expression language's Int32 domain.
                                    // Retain the pointer result, but remove any stale caller/define value shadowed by
                                    // this field so later expressions fail instead of using data contradicted by the stream.
                                    state.Variables.Remove(f.Name.Name);
                                }
                            }
                            else if (content is string str)
                            {
                                // Existing expression semantics preserve strings as identifiers for compatible layouts.
                                state.Variables[f.Name.Name] = new Identifier(str);
                            }
                            else if (content is IConvertible)
                            {
                                try
                                {
                                    // Scalars become literals so following array counts and expressions can use their name.
                                    state.Variables[f.Name.Name] = new Literal(Convert.ToInt32(content));
                                }
                                catch (OverflowException)
                                {
                                    // Values outside the layout expression's Int32 range cannot become variables; retain the
                                    // parsed field, but remove any stale caller/definition value shadowed by this field.
                                    state.Variables.Remove(f.Name.Name);
                                }
                            }
                        }
                    }

                    if (isArray)
                    {
                        if (!f.IsPointer && (f.Type.Equals(CharType) || IsWideCharacterType(f.Type)))
                        {
                            // Expose fixed character arrays as the string callers expect, after every character has been read.
                            var list = (List<object?>)containerDict[f.Name.Name]!;
                            string parsedString = new(list.Cast<char>().ToArray());
                            if (IsWideCharacterType(f.Type))
                            {
                                try
                                {
                                    _ = this.GetWideCharacterEncoding(f.Type).GetByteCount(parsedString);
                                }
                                catch (EncoderFallbackException exception)
                                {
                                    throw new CStructReadException(
                                        "Wide-character buffer contains an invalid UTF-16 code-unit sequence.",
                                        exception);
                                }
                            }

                            containerDict[f.Name.Name] = parsedString;
                        }
                    }

                    break;
                }
            }

            break;
        }
    }

    /// <summary>Checks the requested path, prepares variables, and chooses ordinary or debug parsing.</summary>
    private (ExpandoObject Root, IReadOnlyList<PathSegment> Segments) ParseStreamInternal(
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
        IReadOnlyList<PathSegment> segments = CStructPathResolver.Parse(elementNameOrPath);
        if (segments.Count == 0)
        {
            throw new CStructPathException("Path is empty.");
        }

        string rootName = segments[0].Name;
        if (!this.TryGetCompiledDeclaration(rootName, out _))
        {
            throw new CStructPathException("Unknown root element: " + rootName);
        }

        ExpandoObject root;
        try
        {
            if (debug)
            {
                // Debug reads use the same parser but additionally retain byte ranges and layout stacks for each value.
                (List<DebugData> DebugData, ExpandoObject Result) parsed
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
            AttachExceptionContext(exception, segments, stream);
            throw;
        }

        // The root contains the full parsed structure; the original segments select the requested child afterwards.
        return (root, segments);
    }

    /// <summary>Creates the root object and reads one named layout element without collecting debug byte ranges.</summary>
    private ExpandoObject ParseStreamRoot(
        Stream stream,
        string elementName,
        Dictionary<string, Expr> variables,
        ReadOperationSettings options)
    {
        // Create a container for the named root layout element before constructing all per-read mutable state.
        dynamic root = new ExpandoObject();

        if (!this.TryGetCompiledDeclaration(elementName, out CStructElement? cstructElement))
        {
            throw new CStructPathException("Unknown root element: " + elementName);
        }

        // The state captures stream progress, variables, alignment, and pointer policy for this one parse operation.
        var state = new CStructOperationContext(
                                                  stream,
                                                  variables,
                                                  this.Aligned,
                                                  options);

        // Start with an empty debug stack; it remains empty in ordinary parsing but keeps the shared call shape simple.
        this.HandleCStructElement(cstructElement, root, state, Array.Empty<CStructElement>());

        return root;
    }

    /// <summary>Creates the root object and reads one named layout element while collecting debug byte ranges.</summary>
    private (List<DebugData> DebugData, ExpandoObject Result) ParseStreamRootDebug(
        Stream stream,
        string elementName,
        Dictionary<string, Expr> variables,
        ReadOperationSettings options)
    {
        // Debug mode uses the same root construction as ordinary mode, with one flag enabled in the operation state.
        dynamic root = new ExpandoObject();

        if (!this.TryGetCompiledDeclaration(elementName, out CStructElement? cstructElement))
        {
            throw new CStructPathException("Unknown root element: " + elementName);
        }

        var state = new CStructOperationContext(
                                                  stream,
                                                  variables,
                                                  this.Aligned,
                                                  options)
        { Debug = true, };

        // Each nested call appends its element to the stack before recording a byte range.
        this.HandleCStructElement(cstructElement, root, state, Array.Empty<CStructElement>());

        return (state.DebugMapping, root);
    }

    /// <summary>Reads a pointer address using the pointer width and byte order chosen for this layout.</summary>
    private long ReadPointerAddress(CStructOperationContext state)
    {
        // Read exactly the configured pointer width and normalize the bytes to the layout's byte order first.
        Span<byte> addressData = ReadIntoBuffer(state.Stream, this.PointerSize, this.IsLittleEndian);
        ulong rawAddress = this.PointerSize switch
        {
            1 => addressData[0],
            2 => BitConverter.ToUInt16(addressData),
            4 => BitConverter.ToUInt32(addressData),
            8 => BitConverter.ToUInt64(addressData),
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
        CStructElement[] debugStack)
    {
        CStructElement? structElement = field.NamedElement;
        if (structElement is not null)
        {
            switch (structElement)
            {
            case CstructEnum enm:
                {
                    return this.ReadEnumValue(field, enm, state.Stream);
                }

            case Struct strct:
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
            }
        }

        if (field.TerminatedReader is not null)
        {
            // Pointer-to-char shorthand uses a terminated-string handler rather than a one-character primitive reader.
            return field.TerminatedReader(state.Stream);
        }

        // All remaining targets are ordinary primitive values read from the current target position.
        return field.Reader?.Invoke(state.Stream) ??
               throw new InvalidOperationException(
                   "Compiled pointer target has no reader: " + field.EffectiveField.Type.Name);
    }

    /// <summary>Decodes one enum through its validated backing domain and declaration-order symbolic table.</summary>
    private EnumValueResult ReadEnumValue(CompiledField field, CstructEnum enm, Stream stream)
    {
        CompiledEnumType compiled = this.GetCompiledEnum(enm);
        object storageValue = field.Reader?.Invoke(stream) ??
                              throw new InvalidOperationException(
                                  "Compiled enum has no storage reader: " + enm.Name.Name);
        BigInteger value = compiled.Integer.FromStorageValue(storageValue);
        ulong rawBits = compiled.Integer.ToRawBits(value);
        return new EnumValueResult(
            enm.Name.Name,
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
        CStructElement[] debugStack)
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

        if (!state.Stream.CanSeek)
        {
            throw new CStructReadException("Pointer dereferencing requires a seekable stream.");
        }

        if (state.PointerDereferenceDepth >= state.MaxPointerDepth)
        {
            throw new CStructReadLimitException("Maximum pointer dereference depth exceeded.");
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
            (targetAddress, field.EffectiveField.Type.Name, pointerDepth);

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
            throw new CStructReadLimitException("Pointer target exceeds the configured size limit.");
        }
    }

    /// <summary>Returns a target's known size, or <see langword="null"/> when it is variable length.</summary>
    private long? GetFixedTargetSize(CompiledField field)
    {
        if (field.TerminatedReader is not null)
        {
            // A terminator determines string length at runtime, so no finite static bound can be reported here.
            return null;
        }

        // The compiled type carries the static extent only when no runtime expression or terminator controls it.
        return field.Type.Symbol.FixedSize;
    }
}
