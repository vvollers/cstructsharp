namespace CStructSharp.Memory.Metadata;

using System.Globalization;
using System.Text.Json;

/// <content>Normalizes original CodeView storage descriptions into the existing ISF interchange contract.</content>
public sealed partial class PdbMetadata
{
    private static readonly Dictionary<uint, SimpleType> SimpleTypes = new()
    {
        [0x00] = new("__cv_no_type", "void", 0, false),
        [0x03] = new("void", "void", 0, false),
        [0x08] = new("HRESULT", "int", 4, true),
        [0x10] = new("signed char", "char", 1, true),
        [0x20] = new("unsigned char", "char", 1, false),
        [0x70] = new("char", "char", 1, true),
        [0x71] = new("wchar_t", "char", 2, false),
        [0x7A] = new("char16_t", "char", 2, false),
        [0x7B] = new("char32_t", "char", 4, false),
        [0x7C] = new("char8_t", "char", 1, false),
        [0x68] = new("__int8", "int", 1, true),
        [0x69] = new("unsigned __int8", "int", 1, false),
        [0x11] = new("short", "int", 2, true),
        [0x21] = new("unsigned short", "int", 2, false),
        [0x72] = new("__int16", "int", 2, true),
        [0x73] = new("unsigned __int16", "int", 2, false),
        [0x12] = new("long", "int", 4, true),
        [0x22] = new("unsigned long", "int", 4, false),
        [0x74] = new("int", "int", 4, true),
        [0x75] = new("unsigned int", "int", 4, false),
        [0x13] = new("long long", "int", 8, true),
        [0x23] = new("unsigned long long", "int", 8, false),
        [0x76] = new("__int64", "int", 8, true),
        [0x77] = new("unsigned __int64", "int", 8, false),
        [0x14] = new("__int128", "int", 16, true),
        [0x24] = new("unsigned __int128", "int", 16, false),
        [0x78] = new("__int128_t", "int", 16, true),
        [0x79] = new("__uint128_t", "int", 16, false),
        [0x30] = new("bool", "bool", 1, false),
        [0x31] = new("__bool16", "bool", 2, false),
        [0x32] = new("__bool32", "bool", 4, false),
        [0x33] = new("__bool64", "bool", 8, false),
        [0x40] = new("float", "float", 4, true),
        [0x41] = new("double", "float", 8, true),
        [0x42] = new("__float80", "float", 10, true),
        [0x43] = new("__float128", "float", 16, true),
        [0x46] = new("__float16", "float", 2, true),
    };

    /// <summary>Writes a deterministic Windows ISF 6.2.0 profile without opening another input or acquiring symbols.</summary>
    /// <remarks>
    /// The Windows identity records the original DBI age. The separate <see cref="InformationAge"/> remains available
    /// for the caller's preparation manifest; it is not silently replaced or asserted equal. Incomplete declarations
    /// remain references without invented layouts. Unsupported storage encodings fail rather than becoming raw-byte types.
    /// The caller owns the output and should publish it atomically only after this operation succeeds.
    /// </remarks>
    /// <param name="output">Writable destination; it stays open.</param>
    /// <param name="pdbName">Original PDB basename from the target's CodeView request, not a local path.</param>
    /// <param name="maxTraversalSteps">
    /// Maximum traversal steps for this export, counted across types, field-list records, members, storage
    /// descriptors and symbols. It is independent of the constructor's input limits because one loaded record can
    /// be visited several times (for example a field list shared by several types).
    /// </param>
    /// <param name="cancellationToken">Checked between types, members and symbols.</param>
    /// <exception cref="ArgumentNullException">The destination or PDB name is null.</exception>
    /// <exception cref="ArgumentException">The destination is not writable or the name is not a basename.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTraversalSteps"/> is not positive.</exception>
    /// <exception cref="Diagnostics.CStructLayoutException">A required storage description is malformed, unsupported or exceeds the traversal limit.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public void WriteIsf(Stream output, string pdbName, long maxTraversalSteps = 100_000_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdbName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTraversalSteps);
        if (!output.CanWrite || pdbName.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
        {
            throw new ArgumentException("ISF preparation needs a writable output and the original PDB basename.");
        }

        var budget = new PdbExportBudget(maxTraversalSteps, cancellationToken);
        budget.Take();
        using var writer = new Utf8JsonWriter(output);
        writer.WriteStartObject();
        writer.WriteStartObject("metadata");
        writer.WriteString("format", "6.2.0");
        writer.WriteStartObject("windows");
        writer.WriteStartObject("pdb");
        writer.WriteString("GUID", this.Guid.ToString("N").ToUpperInvariant());
        writer.WriteNumber("age", this.DbiAge);
        writer.WriteNumber("machine_type", this.Machine);
        writer.WriteString("database", pdbName);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartObject("base_types");
        foreach (SimpleType simple in SimpleTypes.Values.OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            WriteBase(writer, simple);
        }

        WriteBase(writer, new("pointer", "int", this.PointerSize, false));
        WriteBase(writer, new("__cv_pointer32", "int", 4, false));
        WriteBase(writer, new("__cv_pointer64", "int", 8, false));
        writer.WriteEndObject();
        writer.WriteStartObject("user_types");
        foreach (NamedType type in this.definitions.Where(type => type.Kind != 0x1507).OrderBy(type => this.names[type.Index], StringComparer.Ordinal))
        {
            budget.Take();
            writer.WriteStartObject(this.names[type.Index]);
            writer.WriteString("kind", type.Kind == 0x1506 ? "union" : "struct");
            writer.WriteNumber("size", type.Size);
            writer.WriteStartObject("fields");
            List<Field> fields = this.ReadFields(type, budget);
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < fields.Count; index++)
            {
                budget.Take();
                Field field = fields[index];
                string name = field.Name.Length == 0 ? $"__anonymous_cv_{index:X}" : field.Name;
                if (!used.Add(name))
                {
                    throw Invalid($"type '{type.Name}' has repeated instance-member names");
                }

                writer.WriteStartObject(name);
                writer.WriteNumber("offset", field.Offset);
                if (field.Name.Length == 0)
                {
                    writer.WriteBoolean("anonymous", true);
                }

                writer.WritePropertyName("type");
                this.WriteDescriptor(writer, field.Type, 0, budget);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        foreach ((uint index, List<NamedType> alternatives) in this.ambiguousViews.OrderBy(entry => this.names[entry.Key], StringComparer.Ordinal))
        {
            budget.Take();
            writer.WriteStartObject(this.names[index]);
            writer.WriteString("kind", "union");
            writer.WriteNumber("size", alternatives[0].Size);
            writer.WriteStartObject("fields");
            foreach (NamedType alternative in alternatives)
            {
                budget.Take();
                writer.WriteStartObject($"cv_{alternative.Index:X8}");
                writer.WriteNumber("offset", 0);
                writer.WriteStartObject("type");
                writer.WriteString("kind", alternative.Kind == 0x1506 ? "union" : "struct");
                writer.WriteString("name", this.names[alternative.Index]);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteStartObject("enums");
        foreach (NamedType type in this.definitions.Where(type => type.Kind == 0x1507).OrderBy(type => this.names[type.Index], StringComparer.Ordinal))
        {
            budget.Take();
            SimpleType storage = this.EnumBase(type.Underlying, 0, budget);
            writer.WriteStartObject(this.names[type.Index]);
            writer.WriteString("base", storage.Name);
            writer.WriteNumber("size", storage.Size);
            writer.WriteStartObject("constants");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (Field field in this.ReadFields(type, budget))
            {
                if (field.Value is not { } value || !used.Add(field.Name))
                {
                    throw Invalid($"enum '{type.Name}' has malformed or repeated constants");
                }

                writer.WritePropertyName(field.Name);
                writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteStartObject("symbols");
        foreach ((string name, ulong address) in this.symbols.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            budget.Take();
            writer.WriteStartObject(name);
            writer.WriteNumber("address", address);
            if (this.symbolTypes.TryGetValue(name, out uint type))
            {
                writer.WritePropertyName("type");
                this.WriteDescriptor(writer, type, 0, budget);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>Writes a recorded primitive's width and signedness, independent of the host C ABI.</summary>
    /// <param name="writer">The preparation output.</param>
    /// <param name="type">Explicit target primitive description.</param>
    private static void WriteBase(Utf8JsonWriter writer, SimpleType type)
    {
        writer.WriteStartObject(type.Name);
        writer.WriteString("kind", type.Kind);
        writer.WriteNumber("size", type.Size);
        writer.WriteString("endian", "little");
        writer.WriteBoolean("signed", type.Signed);
        writer.WriteEndObject();
    }

    /// <summary>Reads one complete native pointer representation, rejecting member-pointer encodings.</summary>
    /// <param name="bytes">Original LF_POINTER payload.</param>
    /// <returns>Original target identity and width defined by the flat pointer kind.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">The representation is not a supported flat 32/64-bit pointer.</exception>
    private static (uint Target, int Width) Pointer(ReadOnlySpan<byte> bytes)
    {
        var reader = new PdbRecordReader(bytes);
        uint target = reader.UInt32();
        uint attributes = reader.UInt32();
        reader.End();
        uint kind = attributes & 31;
        uint mode = (attributes >> 5) & 7;
        int width = (int)((attributes >> 13) & 63);
        int kindWidth = kind switch { 10 => 4, 12 => 8, _ => 0 };

        // Older CodeView records omit size bits, but their near32/near64 kind still fixes storage width.
        if (mode is not (0 or 1 or 4) || kindWidth == 0 || (width != 0 && width != kindWidth))
        {
            throw Invalid("a native pointer is not a supported flat 32/64-bit representation");
        }

        return (target, kindWidth);
    }

    /// <summary>Reads an array's declared byte extent without traversing its element graph.</summary>
    /// <param name="bytes">Original LF_ARRAY payload.</param>
    /// <returns>Original byte size.</returns>
    private static int ArraySize(ReadOnlySpan<byte> bytes)
    {
        var reader = new PdbRecordReader(bytes);
        _ = reader.UInt32();
        _ = reader.UInt32();
        return reader.Size();
    }

    /// <summary>Maps CodeView's explicit simple-pointer mode to stored width.</summary>
    /// <param name="mode">Original mode bits.</param>
    /// <returns>Four or eight bytes.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">The pointer is segmented or uses an unsupported width.</exception>
    private static int SimplePointerWidth(uint mode) => mode switch
    {
        0x400 => 4,
        0x600 => 8,
        _ => throw Invalid("a simple pointer uses an unsupported segmented or nonnative width"),
    };

    /// <summary>Looks up an explicitly sized CodeView primitive.</summary>
    /// <param name="index">Original direct primitive index.</param>
    /// <returns>The target primitive description.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">The primitive is not supported.</exception>
    private static SimpleType Primitive(uint index) => SimpleTypes.TryGetValue(index, out SimpleType type) ? type : throw Invalid($"primitive 0x{index:X4} is not supported");

    /// <summary>Consumes one C++ field-list element that has no instance storage of its own.</summary>
    /// <remarks>
    /// Methods, static members, nested types, vtable pointers and base classes are skipped. A base-class subobject
    /// does occupy bytes, but its members are not exported into the derived type; those bytes remain unnamed.
    /// Layouts follow the CodeView field-leaf definitions referenced by the memory-schema guide.
    /// </remarks>
    /// <param name="reader">Reader positioned after the leaf code and its leading two-byte word.</param>
    /// <param name="kind">Original field leaf code.</param>
    /// <param name="attributes">The leading word: member attributes, padding or an overload count.</param>
    /// <returns>True when the leaf was recognized and consumed; false for an unsupported leaf.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">A recognized leaf is truncated or malformed.</exception>
    private static bool SkipNonStorageField(ref PdbRecordReader reader, ushort kind, ushort attributes)
    {
        switch (kind)
        {
            case 0x1400 or 0x151A:
                // LF_BCLASS, LF_BINTERFACE: base type and its offset within the derived object.
                _ = reader.UInt32();
                _ = reader.Integer();
                return true;
            case 0x1401 or 0x1402:
                // LF_VBCLASS, LF_IVBCLASS: base type, virtual-base pointer type, pointer offset and table index.
                _ = reader.UInt32();
                _ = reader.UInt32();
                _ = reader.Integer();
                _ = reader.Integer();
                return true;
            case 0x1409:
                // LF_VFUNCTAB: the vtable pointer's type.
                _ = reader.UInt32();
                return true;
            case 0x140C:
                // LF_VFUNCOFF: vtable pointer type and offset.
                _ = reader.UInt32();
                _ = reader.UInt32();
                return true;
            case 0x150E or 0x1510 or 0x1512:
                // LF_STMEMBER, LF_NESTTYPE, LF_NESTTYPEEX: a type index and a name.
                _ = reader.UInt32();
                _ = reader.Name();
                return true;
            case 0x150F:
                // LF_METHOD: overload count (the leading word), method-list index and name.
                _ = reader.UInt32();
                _ = reader.Name();
                return true;
            case 0x1511:
                // LF_ONEMETHOD: an introducing virtual method (property 4 or 6) also records its vtable offset.
                _ = reader.UInt32();
                if (((attributes >> 2) & 7) is 4 or 6)
                {
                    _ = reader.UInt32();
                }

                _ = reader.Name();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Writes a storage reference; named composites terminate recursive pointer graphs.</summary>
    /// <param name="writer">The preparation output.</param>
    /// <param name="index">Original CodeView type identity.</param>
    /// <param name="depth">Current modifier/reference nesting, not graph-wide type count.</param>
    /// <param name="budget">Operation-local shared work and cancellation limit.</param>
    /// <exception cref="Diagnostics.CStructLayoutException">The storage kind, extent or nesting is invalid.</exception>
    private void WriteDescriptor(Utf8JsonWriter writer, uint index, int depth, PdbExportBudget budget)
    {
        budget.Take();
        if (depth >= MemorySchema.MaxDefinitionNestingDepth)
        {
            throw Invalid("storage descriptor nesting exceeds its depth limit");
        }

        if (index < 0x1000)
        {
            uint mode = index & 0xF00;
            if (mode != 0)
            {
                int width = SimplePointerWidth(mode);
                writer.WriteStartObject();
                writer.WriteString("kind", "pointer");
                writer.WriteString("base", width == 4 ? "__cv_pointer32" : "__cv_pointer64");
                writer.WritePropertyName("subtype");
                this.WriteDescriptor(writer, index & 0xFF, depth + 1, budget);
                writer.WriteEndObject();
                return;
            }

            SimpleType simple = Primitive(index);
            writer.WriteStartObject();
            writer.WriteString("kind", "base");
            writer.WriteString("name", simple.Name);
            writer.WriteEndObject();
            return;
        }

        TypeRecord record = this.Type(index);
        var reader = new PdbRecordReader(record.Bytes.Span);
        if (this.namedTypes.TryGetValue(index, out NamedType? named))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", this.ambiguousViews.ContainsKey(index) || named.Kind == 0x1506 ? "union" : named.Kind == 0x1507 ? "enum" : "struct");
            writer.WriteString("name", this.names[index]);
            writer.WriteEndObject();
            return;
        }

        switch (record.Kind)
        {
            case 0x1001:
                uint modified = reader.UInt32();
                ushort flags = reader.UInt16();
                reader.End();
                if ((flags & ~7) != 0)
                {
                    throw Invalid("a modifier contains unknown qualifier bits");
                }

                this.WriteDescriptor(writer, modified, depth + 1, budget);
                break;
            case 0x1002:
                (uint target, int width) = Pointer(record.Bytes.Span);
                writer.WriteStartObject();
                writer.WriteString("kind", "pointer");
                writer.WriteString("base", width == 4 ? "__cv_pointer32" : "__cv_pointer64");
                writer.WritePropertyName("subtype");
                this.WriteDescriptor(writer, target, depth + 1, budget);
                writer.WriteEndObject();
                break;
            case 0x1503:
                uint element = reader.UInt32();
                _ = reader.UInt32();
                int bytes = reader.Size();
                _ = reader.Name();
                reader.End();
                int elementSize = this.StorageSize(element, depth + 1, budget);
                if (elementSize <= 0 || bytes % elementSize != 0)
                {
                    throw Invalid("an array extent is not a whole number of its recorded element type");
                }

                writer.WriteStartObject();
                writer.WriteString("kind", "array");
                writer.WriteNumber("count", bytes / elementSize);
                writer.WritePropertyName("subtype");
                this.WriteDescriptor(writer, element, depth + 1, budget);
                writer.WriteEndObject();
                break;
            case 0x1205:
                uint storage = reader.UInt32();
                byte bits = reader.Byte();
                byte position = reader.Byte();
                reader.End();
                int storageSize = this.StorageSize(storage, depth + 1, budget);
                if (bits == 0 || position + bits > storageSize * 8)
                {
                    throw Invalid("a bitfield extends outside its recorded storage integer");
                }

                writer.WriteStartObject();
                writer.WriteString("kind", "bitfield");
                writer.WriteNumber("bit_position", position);
                writer.WriteNumber("bit_length", bits);
                writer.WritePropertyName("type");
                this.WriteDescriptor(writer, storage, depth + 1, budget);
                writer.WriteEndObject();
                break;
            case 0x1008 or 0x1009:
                int minimum = record.Kind == 0x1008 ? 12 : 24;
                if (record.Bytes.Length < minimum)
                {
                    throw Invalid("a function description is truncated");
                }

                writer.WriteStartObject();
                writer.WriteString("kind", "function");
                writer.WriteEndObject();
                break;
            default:
                throw Invalid($"storage leaf 0x{record.Kind:X4} is not supported");
        }
    }

    /// <summary>Reads every instance member or enum constant, following bounded original field-list continuations.</summary>
    /// <param name="type">Original containing type.</param>
    /// <param name="budget">Operation-local shared work and cancellation limit.</param>
    /// <returns>Members in original order, without expanding pointed-to graphs.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">A list cycles, is truncated or contradicts its declared count.</exception>
    private List<Field> ReadFields(NamedType type, PdbExportBudget budget)
    {
        var result = new List<Field>(type.Count);
        var visited = new HashSet<uint>();
        bool onlyStorage = true;
        uint next = type.Fields;
        while (next != 0)
        {
            // Each continuation charges the shared budget, which also bounds the visited set's size.
            budget.Take();
            if (!visited.Add(next))
            {
                throw Invalid("an original field-list continuation cycles");
            }

            TypeRecord record = this.Type(next);
            if (record.Kind != 0x1203)
            {
                throw Invalid("a member-list reference does not name a field list");
            }

            var reader = new PdbRecordReader(record.Bytes.Span);
            next = 0;
            while (reader.Remaining != 0)
            {
                budget.Take();
                if (reader.Padding())
                {
                    continue;
                }

                ushort kind = reader.UInt16();
                if (kind == 0x1404)
                {
                    if (reader.UInt16() != 0)
                    {
                        throw Invalid("a continuation has nonzero reserved bytes");
                    }

                    next = reader.UInt32();
                    reader.End();
                    break;
                }

                if (result.Count >= this.maximumRecords)
                {
                    throw Invalid("an original member list exceeds its record budget");
                }

                // Every field leaf starts with a two-byte word: attributes, padding or a method-overload count.
                ushort attributes = reader.UInt16();
                if (kind == 0x150D && type.Kind != 0x1507)
                {
                    uint storage = reader.UInt32();
                    int offset = reader.Size();
                    string name = reader.Name();
                    result.Add(new(name, storage, offset, null));
                }
                else if (kind == 0x1502 && type.Kind == 0x1507)
                {
                    Int128 value = reader.Integer();
                    string name = reader.Name();
                    result.Add(new(name, 0, 0, value));
                }
                else if (type.Kind != 0x1507 && SkipNonStorageField(ref reader, kind, attributes))
                {
                    onlyStorage = false;
                }
                else
                {
                    throw Invalid($"field leaf 0x{kind:X4} is not supported for this containing type");
                }
            }
        }

        // CodeView's count also includes C++ methods, bases, nested types and similar elements. It can be compared
        // exactly only when the lists contain instance storage alone; otherwise it is an upper bound.
        if (onlyStorage ? result.Count != type.Count : result.Count > type.Count)
        {
            throw Invalid($"type '{type.Name}' has a different original member count from its field lists");
        }

        return result;
    }

    /// <summary>Gets a fixed target storage size for array strides and bitfield bounds.</summary>
    /// <param name="index">Original type identity.</param>
    /// <param name="depth">Current by-value/modifier nesting.</param>
    /// <param name="budget">Operation-local shared work and cancellation limit.</param>
    /// <returns>The recorded byte extent.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">No complete fixed extent can be established.</exception>
    private int StorageSize(uint index, int depth, PdbExportBudget budget)
    {
        budget.Take();
        if (depth >= MemorySchema.MaxDefinitionNestingDepth)
        {
            throw Invalid("by-value storage nesting exceeds its depth limit");
        }

        if (index < 0x1000)
        {
            uint mode = index & 0xF00;
            return mode != 0 ? SimplePointerWidth(mode) : Primitive(index).Size;
        }

        if (this.namedTypes.TryGetValue(index, out NamedType? type))
        {
            if (type.Forward)
            {
                if (this.ambiguousViews.TryGetValue(index, out List<NamedType>? alternatives))
                {
                    return alternatives[0].Size;
                }

                if (!this.forwardTypes.TryGetValue(index, out uint complete))
                {
                    throw Invalid("an array element has only an incomplete forward declaration");
                }

                return this.StorageSize(complete, depth + 1, budget);
            }

            return type.Kind == 0x1507 ? this.StorageSize(type.Underlying, depth + 1, budget) : type.Size;
        }

        TypeRecord record = this.Type(index);
        var reader = new PdbRecordReader(record.Bytes.Span);
        return record.Kind switch
        {
            0x1001 => this.StorageSize(reader.UInt32(), depth + 1, budget),
            0x1002 => Pointer(record.Bytes.Span).Width,
            0x1503 => ArraySize(record.Bytes.Span),
            _ => throw Invalid($"storage size for leaf 0x{record.Kind:X4} is not available"),
        };
    }

    /// <summary>Finds an enum's integral storage without assuming the host's long width.</summary>
    /// <param name="index">Original underlying type reference.</param>
    /// <param name="depth">Modifier depth.</param>
    /// <param name="budget">Operation-local shared work and cancellation limit.</param>
    /// <returns>The explicit primitive storage type.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">The enum does not have bounded integral storage.</exception>
    private SimpleType EnumBase(uint index, int depth, PdbExportBudget budget)
    {
        budget.Take();
        if (depth >= MemorySchema.MaxDefinitionNestingDepth)
        {
            throw Invalid("enum storage nesting exceeds its depth limit");
        }

        if (index < 0x100)
        {
            SimpleType type = Primitive(index);
            return type.Kind is "int" or "char" ? type : throw Invalid("an enum has nonintegral storage");
        }

        TypeRecord record = this.Type(index);
        return record.Kind == 0x1001 ? this.EnumBase(U32(record.Bytes.Span, 0), depth + 1, budget) : throw Invalid("an enum has unsupported storage");
    }

    /// <summary>Explicit primitive storage independent of the host ABI.</summary>
    /// <param name="Name">Stable ISF base-type name.</param>
    /// <param name="Kind">ISF scalar category.</param>
    /// <param name="Size">Recorded byte width.</param>
    /// <param name="Signed">Whether integral storage is signed.</param>
    private readonly record struct SimpleType(string Name, string Kind, int Size, bool Signed);

    /// <summary>An original instance member or enum constant in declaration order.</summary>
    /// <param name="Name">Complete original name.</param>
    /// <param name="Type">Native storage reference for an instance member.</param>
    /// <param name="Offset">Original byte offset within the containing record.</param>
    /// <param name="Value">Original enum value, or null for an instance member.</param>
    private readonly record struct Field(string Name, uint Type, int Offset, Int128? Value);
}
