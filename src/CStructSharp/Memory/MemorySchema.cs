namespace CStructSharp.Memory;

using System.Collections.ObjectModel;
using System.Text;

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
/// Two views of the same information coexist. <see cref="Types"/> is the semantic graph: real names, IDs, offsets,
/// bit slices, and provenance, which is what an analyzer should show. <see cref="CompiledLayout"/> is a generated
/// Portable layout in which every metadata type is expressed as a union of byte arrays at the recorded offsets.
/// That construction lets the memory APIs reuse the core scalar and bitfield codecs without writing a second
/// decoder; its generated names (<c>m0</c>, <c>f0</c>) are implementation details, not metadata names.
/// </para>
/// <para>
/// Compile once and reuse the schema across many regions and sessions; it holds no bytes and no mutable state.
/// </para>
/// </remarks>
public sealed class MemorySchema
{
    private readonly Dictionary<string, CStruct> scalarLayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Type, string Field), CStruct> bitLayouts = new();
    private readonly Dictionary<string, string> compiledNames = new(StringComparer.Ordinal);

    /// <summary>Snapshots, validates, and compiles a type graph. Placement comes from the definitions; nothing is inferred from the host.</summary>
    /// <param name="types">Definitions to include; every ID they reference must be among them.</param>
    /// <param name="isLittleEndian">Byte order for scalars that do not specify their own.</param>
    /// <param name="options">Core compilation options, including any fixed custom codecs that scalars may name.</param>
    /// <param name="maxTypes">Maximum number of definitions accepted, bounding work on hostile metadata.</param>
    /// <param name="maxFields">Maximum total number of fields across all definitions.</param>
    /// <param name="pointerSize">Target pointer width in bytes; it describes the analyzed image, not the analyzing process.</param>
    /// <param name="cancellationToken">Checked between definitions during validation and compilation.</param>
    public MemorySchema(IEnumerable<MemoryTypeDefinition> types, bool isLittleEndian = true, CStructCompilationOptions? options = null, int maxTypes = 100_000, int maxFields = 1_000_000, int pointerSize = 8, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(types);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTypes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFields);
        this.IsLittleEndian = isLittleEndian;
        _ = new StoredPointer(0, pointerSize);
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
                throw new ArgumentException("Metadata exceeds the type/member budget.", nameof(types));
            }

            fields += type.Fields.Count;
            definitions.Add(type.Id, type);
            this.compiledNames.Add(type.Id, "m" + (definitions.Count - 1));
        }

        this.Types = new ReadOnlyDictionary<string, MemoryTypeDefinition>(definitions);

        // Pass 2: validate each definition on its own and compile its scalar and bit-slice codecs.
        foreach (MemoryTypeDefinition type in definitions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Validate(type);
        }

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
    public CStruct CompiledLayout { get; }

    /// <summary>Gets the core compilation options shared by every codec the schema compiles.</summary>
    internal CStructCompilationOptions Options { get; }

    /// <summary>Looks up a definition by ID; an unknown ID is an error, never a guessed type.</summary>
    /// <param name="id">Stable identity of the definition.</param>
    /// <returns>The definition with that ID.</returns>
    public MemoryTypeDefinition GetType(string id) => this.Types.TryGetValue(id, out MemoryTypeDefinition? type) ? type : throw new ArgumentException($"Unknown memory type '{id}'.", nameof(id));

    /// <summary>Returns the generated name under which a type appears in <see cref="CompiledLayout"/>.</summary>
    /// <param name="typeId">Stable identity of the definition.</param>
    /// <returns>The generated view name, such as <c>m3</c>.</returns>
    public string GetCompiledName(string typeId) => this.compiledNames[typeId];

    /// <summary>Finds an immediate member by name; promoted members are resolved by session paths, not here.</summary>
    /// <param name="typeId">Stable identity of the containing struct or union.</param>
    /// <param name="name">Member name as declared in the definition.</param>
    /// <returns>The member's field descriptor.</returns>
    public MemoryField GetField(string typeId, string name)
    {
        foreach (MemoryField field in this.GetType(typeId).Fields)
        {
            if (field.Name == name)
            {
                return field;
            }
        }

        throw new ArgumentException($"Unknown field '{typeId}.{name}'.", nameof(name));
    }

    /// <summary>Returns the core spelling used to decode a scalar or pointer; a pointer is an unsigned integer of its width.</summary>
    internal static string CodecRoot(MemoryTypeDefinition type) => type.Kind == MemoryTypeKind.Pointer ? $"uint{type.Size * 8}" : type.ScalarType!;

    /// <summary>Returns the codec compiled for a whole scalar, or for one field's bit slice when the field has a width.</summary>
    /// <param name="type">Scalar or pointer definition being decoded.</param>
    /// <param name="field">The selecting field, whose bit slice chooses a slice codec; null for a whole value.</param>
    /// <param name="parentId">ID of the containing type, which keys the slice codec together with the field name.</param>
    internal CStruct GetCodec(MemoryTypeDefinition type, MemoryField? field = null, string? parentId = null)
    {
        return field?.BitWidth is not null ? this.bitLayouts[(parentId!, field.Name)] : this.scalarLayouts[type.Id];
    }

    /// <summary>Validates one definition and compiles its leaf codecs, checking metadata extents against actual codec sizes.</summary>
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
    private void Validate(MemoryTypeDefinition type)
    {
        if (!Enum.IsDefined(type.Kind))
        {
            throw new ArgumentException("Unknown metadata type kind.");
        }

        if (type.Kind is MemoryTypeKind.Scalar or MemoryTypeKind.Pointer)
        {
            if (type.Kind == MemoryTypeKind.Pointer)
            {
                _ = new StoredPointer(0, type.Size);
                if (type.ElementTypeId is not null)
                {
                    _ = this.GetType(type.ElementTypeId);
                }
            }

            // Wrap the codec in a one-member struct so the core tells us its size; disagreement is a metadata error.
            string root = CodecRoot(type);
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            var codec = new CStruct((type.Declaration ?? string.Empty) + $"\nstruct __memory_scalar {{ {root} value; }};", pointerSize: (byte)this.PointerSize, isLittleEndian: type.IsLittleEndian ?? this.IsLittleEndian, compilationOptions: this.Options);
            if (codec.GetStructSizeInBytes("__memory_scalar") != type.Size)
            {
                throw new ArgumentException($"Scalar size disagrees with codec for '{type.Id}'.");
            }

            this.scalarLayouts.Add(type.Id, codec);
        }
        else if (type.Kind == MemoryTypeKind.Array)
        {
            MemoryTypeDefinition element = this.GetType(type.ElementTypeId!);
            if (element.Kind == MemoryTypeKind.Incomplete || element.Size == 0 || checked(element.Size * type.Count) != type.Size)
            {
                throw new ArgumentException($"Invalid array extent for '{type.Id}'.");
            }
        }
        else if (type.Kind == MemoryTypeKind.Incomplete && (type.Size != 0 || type.Fields.Count != 0))
        {
            throw new ArgumentException("Incomplete types have no storage.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var intervals = new List<(long Start, long End)>();
        foreach (MemoryField field in type.Fields)
        {
            if (type.Kind is not (MemoryTypeKind.Struct or MemoryTypeKind.Union) || !names.Add(field.Name) || field.Name.Length == 0)
            {
                throw new ArgumentException($"Invalid or duplicate member in '{type.Id}'.");
            }

            MemoryTypeDefinition member = this.GetType(field.TypeId);
            if (member.Kind == MemoryTypeKind.Incomplete || field.Offset > type.Size || member.Size > type.Size - field.Offset)
            {
                throw new ArgumentException($"Member '{type.Id}.{field.Name}' exceeds its containing extent.");
            }

            long start = (long)field.Offset * 8;
            long length = (long)member.Size * 8;
            if (field.BitWidth is int width)
            {
                if (member.Kind != MemoryTypeKind.Scalar || member.Size is not (1 or 2 or 4 or 8) || width > (member.Size * 8) - field.BitOffset!.Value)
                {
                    throw new ArgumentException("Invalid bitfield storage extent.");
                }

                // Build "struct __bits { storage :BitOffset; storage value:BitWidth; }": the anonymous prefix skips
                // the low bits so the core's low-bit-first allocator places "value" on the requested slice.
                string storage = CodecRoot(member);
                string padding = field.BitOffset == 0 ? string.Empty : $"{storage} :{field.BitOffset};";

                // An explicit-endian codec spelling ("uint16>" or "uint16<") overrides the definition and schema order.
                bool littleEndian = member.ScalarType?.EndsWith('>') == true ? false : member.ScalarType?.EndsWith('<') == true || (member.IsLittleEndian ?? this.IsLittleEndian);

                // The slice is numbered inside the whole storage integer (the memory schema's own BitOffset), so the
                // codec keeps declared-size units: MSVC packing never clamps or widens a unit.
                var bitOptions = new CStructCompilationOptions
                {
                    Codecs = this.Options.Codecs,
                    CLongWidth = this.Options.CLongWidth,
                    DefaultEnumStorage = this.Options.DefaultEnumStorage,
                    Prelude = this.Options.Prelude,
                    MaxDefinitionLength = this.Options.MaxDefinitionLength,
                    BitfieldPacking = BitfieldPacking.Msvc,
                };
                this.bitLayouts.Add((type.Id, field.Name), new CStruct((member.Declaration ?? string.Empty) + $"\nstruct __bits {{ {padding} {storage} value:{width}; }};", pointerSize: (byte)this.PointerSize, isLittleEndian: littleEndian, compilationOptions: bitOptions));

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

            if (field.Promoted && member.Kind is not (MemoryTypeKind.Struct or MemoryTypeKind.Union))
            {
                throw new ArgumentException("Only composite members can be promoted.");
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
                    throw new ArgumentException($"Overlapping members in struct '{type.Id}'. Use a union for overlays.");
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
    private void CheckRecursion(MemoryTypeDefinition type, HashSet<string> visiting, HashSet<string> visited, int depth)
    {
        if (visited.Contains(type.Id))
        {
            return;
        }

        if (depth > 128 || !visiting.Add(type.Id))
        {
            throw new ArgumentException("By-value recursion or excessive metadata depth.");
        }

        if (type.Kind == MemoryTypeKind.Array)
        {
            this.CheckRecursion(this.GetType(type.ElementTypeId!), visiting, visited, depth + 1);
        }

        foreach (MemoryField field in type.Fields)
        {
            this.CheckRecursion(this.GetType(field.TypeId), visiting, visited, depth + 1);
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
                source.Append("struct { uint8 _[").Append(field.Offset).Append("]; uint8 value[").Append(this.GetType(field.TypeId).Size).Append("]; } f").Append(i).Append(';');
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
                throw new ArgumentException($"Compiled metadata extent differs for '{type.Id}'.");
            }

            for (int index = 0; index < type.Fields.Count; index++)
            {
                string path = this.compiledNames[type.Id] + ".f" + index + ".value";
                if (compiled.ResolveAddress(Stream.Null, path) != type.Fields[index].Offset)
                {
                    throw new ArgumentException($"Compiled metadata placement differs for '{type.Id}.{type.Fields[index].Name}'.");
                }
            }
        }

        return compiled;
    }
}
