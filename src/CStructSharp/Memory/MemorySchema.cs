namespace CStructSharp.Memory;

using System.Collections.ObjectModel;
using System.Text;
using CStructSharp.Addressing;
using CStructSharp.Diagnostics;

/// <summary>A validated, immutable graph of type definitions with explicit placement, compiled so the core codecs can decode its scalars.</summary>
/// <remarks>
/// <para>
/// A Portable <see cref="CStruct"/> computes where members go from their declaration order and the layout rules.
/// A memory schema does the opposite: it takes sizes and offsets as given, because they were decided by another
/// platform's compiler and recorded in metadata. The constructor checks that the given placement is internally
/// consistent (references exist, members fit, struct members do not overlap, arrays have the declared extent, no
/// type contains itself by value) and then compiles the pieces the core library needs to decode and encode values.
/// Pointer cycles remain legal because a pointer has a finite size.
/// </para>
/// <para>
/// <see cref="Types"/> is the semantic graph: real names, IDs, offsets, bit slices, and provenance, which is what an
/// analyzer should show. To decode, the schema also compiles an internal Portable layout in which every metadata
/// type is a union of byte arrays at the recorded offsets, so the memory APIs reuse the core scalar and bitfield
/// codecs instead of a second decoder; its generated names (<c>m0</c>, <c>f0</c>) never appear in results.
/// </para>
/// <para>
/// Compile once and reuse the schema across many regions and sessions; it holds no bytes and no mutable state.
/// </para>
/// </remarks>
public sealed class MemorySchema
{
    /// <summary>
    ///     The greatest nesting depth accepted in a type-definition graph: by-value members and array elements, typedef
    ///     chains, and imported metadata documents. It equals the core's default
    ///     <see cref="CStructCompilationOptions.MaxLayoutNestingDepth"/> (256), so a memory schema accepts the same
    ///     nesting as a compiled layout. The bound exists to protect the call stack of the recursive checks.
    /// </summary>
    internal const int MaxDefinitionNestingDepth = 256;

    private readonly Dictionary<string, MemoryScalarCodec> scalarLayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Type, string Field), MemoryScalarCodec> bitLayouts = new();
    private readonly Dictionary<string, string> compiledNames = new(StringComparer.Ordinal);

    /// <summary>Snapshots, validates, and compiles a type graph. Placement comes from the definitions; nothing is inferred from the host.</summary>
    /// <param name="types">Definitions to include; every ID they reference must be among them.</param>
    /// <param name="isLittleEndian">Byte order for scalars that do not specify their own.</param>
    /// <param name="options">Core compilation options, including any fixed custom codecs that scalars may name.</param>
    /// <param name="maxTypes">Maximum number of definitions accepted, bounding work on hostile metadata.</param>
    /// <param name="maxFields">Maximum total number of fields across all definitions.</param>
    /// <param name="pointerSize">Target pointer width in bytes; it describes the analyzed image, not the analyzing process.</param>
    /// <param name="cancellationToken">Checked between definitions during validation and compilation.</param>
    /// <param name="bestEffort">When true, a definition that fails its own validation is demoted to a same-sized <see cref="MemoryTypeKind.RawBytes"/> placeholder and noted in <see cref="Diagnostics"/>, instead of the whole schema failing; when false (the default), any validation failure throws.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTypes"/> or <paramref name="maxFields"/> is not positive, or <paramref name="pointerSize"/> is not 1, 2, 4, or 8.</exception>
    /// <exception cref="CStructLayoutException">
    ///     The definitions are invalid: they exceed the type or field budget, repeat an ID, reference an unknown ID,
    ///     place a member outside its container, overlap struct members, disagree with a scalar codec's size, or
    ///     contain a type by value within itself. In best-effort mode only whole-graph failures (budgets, duplicate
    ///     IDs, by-value recursion) throw.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public MemorySchema(IEnumerable<MemoryTypeDefinition> types, bool isLittleEndian = true, CStructCompilationOptions? options = null, int maxTypes = 100_000, int maxFields = 1_000_000, int pointerSize = 8, CancellationToken cancellationToken = default, bool bestEffort = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(types);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTypes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFields);
        this.IsLittleEndian = isLittleEndian;
        StoredPointer.ValidateWidth(pointerSize);
        this.PointerSize = pointerSize;
        this.Options = options ?? new CStructCompilationOptions();

        // Pass 1: collect definitions and assign each a generated compiled name, enforcing the budgets as we go.
        var definitions = new Dictionary<string, MemoryTypeDefinition>(StringComparer.Ordinal);
        int fields = 0;
        foreach (MemoryTypeDefinition type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (definitions.Count >= maxTypes || type.Fields.Count > maxFields - fields)
            {
                throw new CStructLayoutException("Metadata exceeds the type/member budget.");
            }

            fields += type.Fields.Count;
            if (!definitions.TryAdd(type.Id, type))
            {
                throw new CStructLayoutException($"Duplicate memory type ID '{type.Id}'.");
            }

            this.compiledNames.Add(type.Id, "m" + (definitions.Count - 1));
        }

        this.Types = new ReadOnlyDictionary<string, MemoryTypeDefinition>(definitions);

        // Pass 2: validate each definition on its own and compile its scalar and bit-slice codecs. In best-effort
        // mode, a definition that fails is demoted in place to a same-sized RawBytes placeholder rather than
        // aborting the whole schema: real-world metadata, especially a torn or partial forensic capture, can be
        // locally corrupt while the rest of the graph remains perfectly readable. The placeholder keeps the
        // original size, which is all most references check; the one exception is a bitfield, whose storage must
        // be a scalar, so pass 2b demotes the users of a demoted scalar whatever order they were validated in.
        var diagnostics = new List<string>();
        foreach (MemoryTypeDefinition type in new List<MemoryTypeDefinition>(definitions.Values))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                this.Validate(type);
            }
            catch (CStructLayoutException error) when (bestEffort)
            {
                Demote(definitions, type, error.Message, diagnostics);
            }
        }

        // Pass 2b: a struct validated before its bitfield storage scalar was demoted still holds a bit slice of what
        // is now raw bytes. Demote such users until no more change, so the outcome never depends on the order.
        for (bool changed = bestEffort; changed;)
        {
            changed = false;
            foreach (MemoryTypeDefinition type in new List<MemoryTypeDefinition>(definitions.Values))
            {
                if (type.Kind is not (MemoryTypeKind.Struct or MemoryTypeKind.Union))
                {
                    continue;
                }

                foreach (MemoryField field in type.Fields)
                {
                    if (field.BitWidth is not null && definitions.TryGetValue(field.TypeId, out MemoryTypeDefinition? storage) && storage.Kind == MemoryTypeKind.RawBytes)
                    {
                        Demote(definitions, type, DemotedStorageMessage(storage.Id), diagnostics);
                        changed = true;
                        break;
                    }
                }
            }
        }

        // A demoted definition may have registered bit-slice codecs before it failed; they no longer describe it.
        foreach ((string Type, string Field) key in new List<(string Type, string Field)>(this.bitLayouts.Keys))
        {
            if (definitions[key.Type].Kind == MemoryTypeKind.RawBytes)
            {
                this.bitLayouts.Remove(key);
            }
        }

        diagnostics.Sort(StringComparer.Ordinal);
        this.Diagnostics = diagnostics.AsReadOnly();

        // Pass 3: reject by-value recursion across the whole graph.
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (MemoryTypeDefinition type in definitions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.CheckRecursion(type, visiting, visited, 0);
        }

        // Pass 4: express the explicit offsets as generated union views so the core compiler can verify them.
        this.CompiledLayout = this.CompileViews(cancellationToken);
    }

    /// <summary>Gets the semantic graph: every validated definition keyed by its ID.</summary>
    public IReadOnlyDictionary<string, MemoryTypeDefinition> Types { get; }

    /// <summary>Gets the default byte order: true when the least significant byte is stored first.</summary>
    public bool IsLittleEndian { get; }

    /// <summary>Gets the target pointer width in bytes, used to compile pointer-width integer aliases.</summary>
    public int PointerSize { get; }

    /// <summary>Gets the generated Portable storage views. Their names are placement labels; semantic names live in <see cref="Types"/>.</summary>
    internal CStruct CompiledLayout { get; }

    /// <summary>Gets one note per definition that best-effort validation demoted to <see cref="MemoryTypeKind.RawBytes"/>, in ordinal order; empty unless the schema was constructed with <c>bestEffort: true</c>.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Gets the core compilation options shared by every codec the schema compiles.</summary>
    internal CStructCompilationOptions Options { get; }

    /// <summary>Looks up a definition by ID; an unknown ID is an error, never a guessed type.</summary>
    /// <param name="id">Stable identity of the definition.</param>
    /// <returns>The definition with that ID.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
    /// <exception cref="CStructPathException">No definition has that ID.</exception>
    public MemoryTypeDefinition GetType(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return this.Types.TryGetValue(id, out MemoryTypeDefinition? type) ? type : throw new CStructPathException($"Unknown memory type '{id}'.");
    }

    /// <summary>Returns the generated name under which a type appears in <see cref="CompiledLayout"/>.</summary>
    /// <param name="typeId">Stable identity of the definition.</param>
    /// <returns>The generated view name, such as <c>m3</c>.</returns>
    internal string GetCompiledName(string typeId) => this.compiledNames[typeId];

    /// <summary>Finds an immediate member by name; promoted members are resolved by session paths, not here.</summary>
    /// <param name="typeId">Stable identity of the containing struct or union.</param>
    /// <param name="name">Member name as declared in the definition.</param>
    /// <returns>The member's field descriptor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeId"/> is null.</exception>
    /// <exception cref="CStructPathException">No definition has ID <paramref name="typeId"/>, or it has no immediate member named <paramref name="name"/>.</exception>
    public MemoryField GetField(string typeId, string name)
        => this.FindField(typeId, name) ?? throw new CStructPathException($"Unknown field '{typeId}.{name}'.");

    /// <summary>Returns the core spelling used to decode a scalar or pointer; a pointer is an unsigned integer of its width.</summary>
    /// <param name="type">A scalar or pointer definition.</param>
    /// <returns>The scalar's own type name, or <c>uintN</c> with N the pointer's size in bits.</returns>
    internal static string CodecRoot(MemoryTypeDefinition type) => type.Kind == MemoryTypeKind.Pointer ? $"uint{type.Size * 8}" : type.ScalarType!;

    /// <summary>Returns the codec prepared for a whole scalar, or for one field's bit slice when the field has a width.</summary>
    /// <param name="type">Scalar or pointer definition being decoded.</param>
    /// <param name="field">The selecting field, whose bit slice chooses a slice codec; null for a whole value.</param>
    /// <param name="parentId">ID of the containing type, which keys the slice codec together with the field name.</param>
    /// <returns>The codec of the scalar or the bit slice: its one-value layout and its direct decoding.</returns>
    internal MemoryScalarCodec GetCodec(MemoryTypeDefinition type, MemoryField? field = null, string? parentId = null)
    {
        return field?.BitWidth is not null ? this.bitLayouts[(parentId!, field.Name)] : this.scalarLayouts[type.Id];
    }

    /// <summary>Finds an immediate member by name, or returns null when the definition has no such member.</summary>
    /// <param name="typeId">Stable identity of the containing struct or union.</param>
    /// <param name="name">Member name as declared in the definition.</param>
    /// <returns>The member's field descriptor, or null.</returns>
    /// <exception cref="CStructPathException">No definition has ID <paramref name="typeId"/>.</exception>
    internal MemoryField? FindField(string typeId, string? name)
    {
        foreach (MemoryField field in this.GetType(typeId).Fields)
        {
            if (field.Name == name)
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>The demotion reason for a struct or union whose bitfield storage type <paramref name="storageId"/> was demoted.</summary>
    private static string DemotedStorageMessage(string storageId) => $"its bitfield storage type '{storageId}' was demoted.";

    /// <summary>Replaces a definition with a same-sized <see cref="MemoryTypeKind.RawBytes"/> placeholder and records why.</summary>
    /// <param name="definitions">The schema's definitions, updated in place.</param>
    /// <param name="type">The definition to demote.</param>
    /// <param name="reason">Why it was demoted.</param>
    /// <param name="diagnostics">The notes that become <see cref="Diagnostics"/>.</param>
    private static void Demote(Dictionary<string, MemoryTypeDefinition> definitions, MemoryTypeDefinition type, string reason, List<string> diagnostics)
    {
        definitions[type.Id] = new MemoryTypeDefinition(type.Id, type.Name, MemoryTypeKind.RawBytes, type.Size, provenance: type.Provenance);
        diagnostics.Add($"{type.Id}: demoted to a {type.Size}-byte raw-bytes placeholder - {reason}");
    }

    /// <summary>Creates the definition error for a reference to an ID the schema lacks.</summary>
    /// <param name="owner">The definition holding the reference.</param>
    /// <param name="id">The referenced ID, or null when none was named.</param>
    /// <returns>The exception to throw.</returns>
    private static CStructLayoutException UnknownReference(MemoryTypeDefinition owner, string? id)
    {
        return new CStructLayoutException($"'{owner.Id}' references unknown memory type '{id}'.");
    }

    /// <summary>Looks up a type that a definition refers to; a reference to an ID the schema lacks is a definition error.</summary>
    /// <param name="owner">The definition holding the reference, named in the diagnostic.</param>
    /// <param name="id">The referenced ID; null when an array names no element type.</param>
    /// <returns>The referenced definition.</returns>
    /// <exception cref="CStructLayoutException">The ID is null or names no definition in the schema.</exception>
    private MemoryTypeDefinition Reference(MemoryTypeDefinition owner, string? id)
    {
        return id is not null && this.Types.TryGetValue(id, out MemoryTypeDefinition? type)
            ? type
            : throw UnknownReference(owner, id);
    }

    /// <summary>Checks that a definition's reference names a type in the schema, without needing the referenced definition.</summary>
    /// <param name="owner">The definition holding the reference, named in the diagnostic.</param>
    /// <param name="id">The referenced ID.</param>
    /// <exception cref="CStructLayoutException">The ID names no definition in the schema.</exception>
    private void ValidateReference(MemoryTypeDefinition owner, string id)
    {
        if (!this.Types.ContainsKey(id))
        {
            throw UnknownReference(owner, id);
        }
    }

    /// <summary>
    ///     Checks one definition against the others (kind, size, member extents, bitfield storage, overlap) and compiles
    ///     its scalar and bit-slice codecs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scalars are compiled as a one-member Portable struct so the core reports the codec's real size; a metadata
    /// integer described as four bytes must not silently decode through a differently sized codec. Bit slices get
    /// their own tiny layout: an anonymous bitfield of <c>BitOffset</c> bits followed by the selected bits, which
    /// makes the core's low-bit-first allocator land on exactly the requested slice.
    /// </para>
    /// <para>
    /// Overlap is checked on bit intervals. A whole member occupies <c>[offset*8, (offset+size)*8)</c>. A bit slice
    /// is converted byte by byte into the physical bits it touches, because in a big-endian storage unit bit 0 of
    /// the integer lives in the last byte. Two slices may share a storage unit as long as their bits are disjoint.
    /// </para>
    /// </remarks>
    /// <param name="type">The definition to check.</param>
    /// <exception cref="CStructLayoutException">The definition is invalid.</exception>
    private void Validate(MemoryTypeDefinition type)
    {
        if (!Enum.IsDefined(type.Kind))
        {
            throw new CStructLayoutException($"Unknown metadata type kind for '{type.Id}'.");
        }

        if (type.Kind is MemoryTypeKind.Scalar or MemoryTypeKind.Pointer)
        {
            if (type.Kind == MemoryTypeKind.Pointer)
            {
                if (type.Size is not (1 or 2 or 4 or 8))
                {
                    throw new CStructLayoutException($"Pointer '{type.Id}' must be 1, 2, 4, or 8 bytes wide.");
                }

                if (type.ElementTypeId is not null)
                {
                    this.ValidateReference(type, type.ElementTypeId);
                }
            }

            // Wrap the codec in a one-member struct so the core tells us its size; disagreement is a metadata error.
            string root = CodecRoot(type);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new CStructLayoutException($"Scalar '{type.Id}' names no codec type.");
            }

            var codec = new CStruct((type.Declaration ?? string.Empty) + $"\nstruct __memory_scalar {{ {root} value; }};", pointerSize: (byte)this.PointerSize, isLittleEndian: type.IsLittleEndian ?? this.IsLittleEndian, compilationOptions: this.Options);
            if (codec.GetStructSizeInBytes("__memory_scalar") != type.Size)
            {
                throw new CStructLayoutException($"Scalar size disagrees with codec for '{type.Id}'.");
            }

            this.scalarLayouts.Add(type.Id, MemoryScalarCodec.ForValue(codec, root));
        }
        else if (type.Kind == MemoryTypeKind.Array)
        {
            // A zero-size element is allowed exactly when it makes the whole array zero bytes wide - the
            // multiplication below already forces that, since count times zero can only ever equal a
            // recorded size of zero. This is not a hypothetical: kernel BTF genuinely declares empty marker
            // structs (for example Linux's lock_class_key, used only for its address, never its contents)
            // and arrays of them, so rejecting every zero-size element would reject correct metadata.
            MemoryTypeDefinition element = this.Reference(type, type.ElementTypeId);
            if (element.Kind == MemoryTypeKind.Incomplete || (long)element.Size * type.Count != type.Size)
            {
                throw new CStructLayoutException($"Invalid array extent for '{type.Id}'.");
            }
        }
        else if (type.Kind == MemoryTypeKind.Incomplete && (type.Size != 0 || type.Fields.Count != 0))
        {
            throw new CStructLayoutException($"Incomplete type '{type.Id}' has no storage.");
        }
        else if (type.Kind == MemoryTypeKind.RawBytes && type.Fields.Count != 0)
        {
            throw new CStructLayoutException($"Raw-bytes type '{type.Id}' has no members.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var intervals = new List<(long Start, long End)>();
        foreach (MemoryField field in type.Fields)
        {
            if (type.Kind is not (MemoryTypeKind.Struct or MemoryTypeKind.Union) || !names.Add(field.Name) || field.Name.Length == 0)
            {
                throw new CStructLayoutException($"Invalid or duplicate member in '{type.Id}'.");
            }

            if (!CStructPathResolver.IsIdentifier(field.Name))
            {
                // A session path names members with the layout path grammar, so a member it cannot spell is unreachable.
                throw new CStructLayoutException(
                    $"Member name '{field.Name}' in '{type.Id}' is not an identifier (a letter or underscore, then letters, digits and underscores), so no path can select it.");
            }

            MemoryTypeDefinition member = this.Reference(type, field.TypeId);
            if (member.Kind == MemoryTypeKind.Incomplete || field.Offset > type.Size || member.Size > type.Size - field.Offset)
            {
                throw new CStructLayoutException($"Member '{type.Id}.{field.Name}' exceeds its containing extent.");
            }

            long start = (long)field.Offset * 8;
            long length = (long)member.Size * 8;
            if (field.BitWidth is int width)
            {
                if (member.Kind == MemoryTypeKind.RawBytes)
                {
                    throw new CStructLayoutException(DemotedStorageMessage(member.Id));
                }

                if (member.Kind != MemoryTypeKind.Scalar || member.Size is not (1 or 2 or 4 or 8) || width > (member.Size * 8) - field.BitOffset!.Value)
                {
                    throw new CStructLayoutException($"Invalid bitfield storage extent for '{type.Id}.{field.Name}'.");
                }

                // Build "struct __bits { storage :BitOffset; storage value:BitWidth; }": the anonymous prefix skips
                // the low bits so the core's low-bit-first allocator places "value" on the requested slice.
                string storage = CodecRoot(member);
                string padding = field.BitOffset == 0 ? string.Empty : $"{storage} :{field.BitOffset};";

                // An explicit-endian codec spelling ("uint16>" or "uint16<") overrides the definition and schema order.
                bool littleEndian = member.ScalarType?.EndsWith('>') == true ? false : member.ScalarType?.EndsWith('<') == true || (member.IsLittleEndian ?? this.IsLittleEndian);

                // The slice is numbered from the low bit of the whole storage integer (the memory schema's own
                // BitOffset), so the codec keeps declared-size units - MSVC packing never clamps or widens a unit - and
                // allocates from the low bit, whatever the schema's own options say.
                CStructCompilationOptions bitOptions = this.Options with
                {
                    BitfieldPacking = BitfieldPacking.Msvc,
                    BitfieldAllocation = BitfieldAllocation.LowBitFirst,
                };
                var slice = new CStruct((member.Declaration ?? string.Empty) + $"\nstruct __bits {{ {padding} {storage} value:{width}; }};", pointerSize: (byte)this.PointerSize, isLittleEndian: littleEndian, compilationOptions: bitOptions);
                this.bitLayouts.Add((type.Id, field.Name), MemoryScalarCodec.ForSlice(slice, field.BitOffset.Value, width));

                // Translate the logical slice into physical bit intervals, one per byte it touches. Logical bit b of
                // the storage integer lives in byte b/8 for little-endian, or in byte (size-1-b/8) for big-endian.
                for (int bit = field.BitOffset.Value; bit < field.BitOffset.Value + width;)
                {
                    int byteIndex = littleEndian ? bit / 8 : member.Size - 1 - (bit / 8);
                    int bitsInByte = Math.Min(8 - (bit % 8), field.BitOffset.Value + width - bit);
                    long physicalBit = (((long)field.Offset + byteIndex) * 8) + (bit % 8);
                    intervals.Add((physicalBit, physicalBit + bitsInByte));
                    bit += bitsInByte;
                }
            }
            else
            {
                intervals.Add((start, checked(start + length)));
            }

            if (field.Promoted && member.Kind is not (MemoryTypeKind.Struct or MemoryTypeKind.Union or MemoryTypeKind.RawBytes))
            {
                throw new CStructLayoutException($"Only composite members can be promoted: '{type.Id}.{field.Name}'.");
            }
        }

        // Sorted intervals overlap only if one starts before the running maximum end of those before it.
        intervals.Sort();
        if (type.Kind == MemoryTypeKind.Struct)
        {
            long end = 0;
            foreach ((long start, long nextEnd) in intervals)
            {
                if (start < end && nextEnd > start)
                {
                    throw new CStructLayoutException($"Overlapping members in struct '{type.Id}'. Use a union for overlays.");
                }

                end = Math.Max(end, nextEnd);
            }
        }
    }

    /// <summary>Rejects types that contain themselves by value; pointers stop the search so recursive native types stay legal.</summary>
    /// <remarks>This is a depth-first search with two sets. <paramref name="visiting"/> holds the current recursion
    /// path: meeting a type that is already on the path means it contains itself, which would need infinite storage.
    /// <paramref name="visited"/> holds types whose subgraph has already been cleared, so a type reused by many
    /// containers is checked once. Pointer targets are not followed because a pointer contributes only its own
    /// fixed size to its container.</remarks>
    /// <param name="type">Definition whose by-value members are being explored.</param>
    /// <param name="visiting">Types on the current recursion path.</param>
    /// <param name="visited">Types already proven free of by-value cycles.</param>
    /// <param name="depth">Current recursion depth, bounded to protect the call stack.</param>
    /// <exception cref="CStructLayoutException">A type contains itself by value, or the by-value graph is deeper than <see cref="MaxDefinitionNestingDepth"/> levels.</exception>
    private void CheckRecursion(MemoryTypeDefinition type, HashSet<string> visiting, HashSet<string> visited, int depth)
    {
        if (visited.Contains(type.Id))
        {
            return;
        }

        if (depth > MaxDefinitionNestingDepth || !visiting.Add(type.Id))
        {
            throw new CStructLayoutException($"By-value recursion or excessive metadata depth at '{type.Id}'.");
        }

        if (type.Kind == MemoryTypeKind.Array)
        {
            this.CheckRecursion(this.Reference(type, type.ElementTypeId), visiting, visited, depth + 1);
        }

        foreach (MemoryField field in type.Fields)
        {
            this.CheckRecursion(this.Reference(type, field.TypeId), visiting, visited, depth + 1);
        }

        visiting.Remove(type.Id);
        visited.Add(type.Id);
    }

    /// <summary>Expresses every definition as a generated Portable union view, then checks that the core agrees with the recorded placement.</summary>
    /// <remarks>
    /// <para>
    /// Portable structs place members by declaration order, so they cannot state "this member is at offset 4"
    /// directly. A union can: each member of a union starts at offset 0, and a member that is a struct of
    /// <c>uint8 _[offset]</c> followed by <c>uint8 value[size]</c> puts <c>value</c> exactly at <c>offset</c>.
    /// A record with a four-byte member at offset 4 therefore becomes
    /// <c>union m0 { uint8 raw[8]; struct { uint8 _[4]; uint8 value[4]; } f0; };</c>.
    /// </para>
    /// <para>
    /// The views are then compiled once and each size and member address is read back through the core's
    /// introspection. This turns the recorded placement into something the compiler has verified, without
    /// changing Portable placement rules. Generated names are deliberately separate from semantic IDs and names;
    /// the original definitions remain the public metadata model.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Checked between definitions.</param>
    /// <returns>The compiled generated views.</returns>
    /// <exception cref="CStructLayoutException">The core compiler places a view differently from the recorded metadata.</exception>
    private CStruct CompileViews(CancellationToken cancellationToken)
    {
        var source = new StringBuilder();
        foreach (MemoryTypeDefinition type in this.Types.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.Kind == MemoryTypeKind.Incomplete)
            {
                continue;
            }

            string name = this.compiledNames[type.Id];
            source.Append("union ").Append(name).Append(" { uint8 raw[").Append(type.Size).Append("]; ");
            for (int i = 0; i < type.Fields.Count; i++)
            {
                MemoryField field = type.Fields[i];
                source.Append("struct { uint8 _[").Append(field.Offset).Append("]; uint8 value[").Append(this.Reference(type, field.TypeId).Size).Append("]; } f").Append(i).Append(';');
            }

            source.AppendLine("};");
        }

        if (source.Length == 0)
        {
            source.Append("struct __memory_empty { uint8 _; };");
        }

        var compiled = new CStruct(source.ToString(), compilationOptions: new CStructCompilationOptions { MaxDefinitionLength = Math.Max(128 * 1024, source.Length), });
        foreach (MemoryTypeDefinition type in this.Types.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.Kind != MemoryTypeKind.Incomplete && compiled.GetStructSizeInBytes(this.compiledNames[type.Id]) != type.Size)
            {
                throw new CStructLayoutException($"Compiled metadata extent differs for '{type.Id}'.");
            }

            for (int index = 0; index < type.Fields.Count; index++)
            {
                string path = this.compiledNames[type.Id] + ".f" + index + ".value";
                if (compiled.ResolveAddress(Stream.Null, path) != type.Fields[index].Offset)
                {
                    throw new CStructLayoutException($"Compiled metadata placement differs for '{type.Id}.{type.Fields[index].Name}'.");
                }
            }
        }

        return compiled;
    }
}
