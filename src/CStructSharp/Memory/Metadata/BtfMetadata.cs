namespace CStructSharp.Memory.Metadata;

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

/// <summary>A parsed BTF v1 type table, optionally split on top of a base table, from which reachable types can be imported into a <see cref="MemorySchema"/>.</summary>
/// <remarks>
/// <para>
/// BTF (BPF Type Format) is the compact binary type description the Linux kernel carries for itself and its
/// modules. A blob is three parts: a 24-byte header, a type section, and a string section. Each type record in
/// the type section is three 32-bit words (name offset, an <c>info</c> word packing the kind and a count, and a
/// size or referenced type ID) followed by a kind-specific payload such as struct members. Records are numbered
/// from 1 in order of appearance; ID 0 is <c>void</c>. Names are offsets into the zero-terminated string section.
/// </para>
/// <para>
/// The kinds this parser understands, by number: 1 INT, 2 PTR, 3 ARRAY, 4 STRUCT, 5 UNION, 6 ENUM, 7 FWD,
/// 8 TYPEDEF, 9 VOLATILE, 10 CONST, 11 RESTRICT, 12 FUNC, 13 FUNC_PROTO, 14 VAR, 15 DATASEC, 16 FLOAT,
/// 17 DECL_TAG, 18 TYPE_TAG, 19 ENUM64. Kinds 8 through 11, 17, and 18 are modifiers that point at another type;
/// 0, 7, 12, and 13 have no value representation and are imported as incomplete.
/// </para>
/// <para>
/// Construction validates and indexes the whole blob within byte and type-count limits; <see cref="Import"/> then
/// compiles only the graph reachable from one chosen root, so several roots can reuse one parsed table. The magic
/// number decides the metadata's byte order, while the target pointer width is a separate caller setting. Split
/// BTF (used by kernel modules) extends a base table: its IDs continue after the base's and its string offsets
/// continue after the base string section, so references into the base keep their identity. Names may repeat,
/// which is why numeric IDs are the authoritative handle. The application obtains the blob; this class does not
/// read ELF sections, discover symbols, or translate page tables.
/// </para>
/// </remarks>
public sealed class BtfMetadata
{
    /// <summary>Decodes names; a string that is not valid UTF-8 fails rather than being replaced.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly Dictionary<uint, BtfType> types;
    private readonly byte[] strings;
    private readonly BtfMetadata? baseMetadata;
    private readonly int baseStringLength;
    private readonly int splitDepth;

    /// <summary>Parses and indexes a BTF v1 blob, reading its byte order from the magic number.</summary>
    /// <remarks>The header is: magic (2 bytes, the value 0xeb9f in the producer's order), version (1), flags (1), then four little- or
    /// big-endian words: header length, type section offset, type section length, string section offset, string
    /// section length. Offsets are relative to the end of the header. Each type record's <c>info</c> word holds the
    /// member or element count in bits 0-15, the kind in bits 24-28, and a kind-specific flag in bit 31.</remarks>
    /// <param name="data">The BTF v1 blob.</param>
    /// <param name="baseMetadata">Previously parsed base table when <paramref name="data"/> is split BTF; otherwise null.</param>
    /// <param name="maxBytes">Maximum accepted blob length, including header, type section, and string section.</param>
    /// <param name="maxTypes">Maximum number of types including those inherited from the base table.</param>
    /// <param name="cancellationToken">Checked once per type record.</param>
    public BtfMetadata(ReadOnlyMemory<byte> data, BtfMetadata? baseMetadata = null, int maxBytes = 16 * 1024 * 1024, int maxTypes = 100_000, CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0 || maxTypes <= 0 || data.Length > maxBytes || data.Length < 24)
        {
            throw new ArgumentException("BTF input is truncated or exceeds its budget.", nameof(data));
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = data.ToArray();

        // The magic 0xeb9f is stored in the producer's byte order, so its byte sequence tells us that order.
        this.IsLittleEndian = bytes[0] == 0x9f && bytes[1] == 0xeb;
        if ((!this.IsLittleEndian && !(bytes[0] == 0xeb && bytes[1] == 0x9f)) || bytes[2] != 1 || bytes[3] != 0)
        {
            throw new ArgumentException("Unsupported BTF magic, version, or flags.", nameof(data));
        }

        this.baseMetadata = baseMetadata;
        this.splitDepth = baseMetadata is null ? 0 : baseMetadata.splitDepth + 1;
        if (this.splitDepth > 128)
        {
            throw new ArgumentException("Split BTF exceeds the base-chain depth limit.", nameof(baseMetadata));
        }

        if (baseMetadata is not null && baseMetadata.IsLittleEndian != this.IsLittleEndian)
        {
            throw new ArgumentException("Split BTF byte order differs from its base.", nameof(baseMetadata));
        }

        // A split table's string offsets continue after every base string section, so remember where ours begins.
        this.baseStringLength = baseMetadata is null ? 0 : checked(baseMetadata.baseStringLength + baseMetadata.strings.Length);
        uint header = this.Word(bytes, 4);
        int typeStart = checked((int)(header + this.Word(bytes, 8)));
        int typeLength = checked((int)this.Word(bytes, 12));
        int stringStart = checked((int)(header + this.Word(bytes, 16)));
        int stringLength = checked((int)this.Word(bytes, 20));

        // Both sections must lie inside the blob, after the header, and must not overlap each other.
        if (header < 24 || typeStart < header || stringStart < header || typeStart > bytes.Length - typeLength || stringStart > bytes.Length - stringLength ||
            (typeLength > 0 && stringLength > 0 && typeStart < stringStart + stringLength && stringStart < typeStart + typeLength))
        {
            throw new ArgumentException("Invalid BTF section extents.", nameof(data));
        }

        this.strings = bytes.AsSpan(stringStart, stringLength).ToArray();
        if (baseMetadata is null && (this.strings.Length == 0 || this.strings[0] != 0))
        {
            throw new ArgumentException("Base BTF string table must begin with an empty string.", nameof(data));
        }

        this.types = baseMetadata is null ? new Dictionary<uint, BtfType>() : new Dictionary<uint, BtfType>(baseMetadata.types);
        if (this.types.Count > maxTypes)
        {
            throw new ArgumentException("BTF base table exceeds the type budget.", nameof(maxTypes));
        }

        int position = typeStart;
        int end = checked(typeStart + typeLength);
        while (position < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.types.Count >= maxTypes || end - position < 12)
            {
                throw new ArgumentException("BTF type table is truncated or exceeds its budget.", nameof(data));
            }

            // Fixed part of every record: name offset, packed info word, and a size or referenced type ID.
            uint name = this.Word(bytes, position);
            uint info = this.Word(bytes, position + 4);
            uint size = this.Word(bytes, position + 8);
            var kind = (BtfKind)((info >> 24) & 31);
            int count = (int)(info & 65535);
            bool flag = (info & 0x80000000) != 0;

            // Bits 16-23 and 29-30 are reserved and must be zero; kinds without a repeated payload must have count zero.
            if ((info & 0x60ff0000) != 0 ||
                (count != 0 && kind is BtfKind.Int or BtfKind.Ptr or BtfKind.Array or BtfKind.Fwd or BtfKind.Typedef or BtfKind.Volatile or
                     BtfKind.Const or BtfKind.Restrict or BtfKind.Var or BtfKind.Float or BtfKind.DeclTag or BtfKind.TypeTag))
            {
                throw new ArgumentException("BTF type contains reserved info bits or an invalid record count.");
            }

            // Payload length in 32-bit words by kind: INT/VAR/DECL_TAG carry one word, ARRAY three, STRUCT/UNION/
            // DATASEC/ENUM64 three per entry, ENUM/FUNC_PROTO two per entry, and the rest nothing.
            int words = kind switch
            {
                BtfKind.Int or BtfKind.Var or BtfKind.DeclTag => 1,
                BtfKind.Array => 3,
                BtfKind.Struct or BtfKind.Union or BtfKind.Datasec or BtfKind.Enum64 => checked(count * 3),
                BtfKind.Enum or BtfKind.FuncProto => checked(count * 2),
                BtfKind.Ptr or BtfKind.Fwd or BtfKind.Typedef or BtfKind.Volatile or BtfKind.Const or BtfKind.Restrict or
                    BtfKind.Func or BtfKind.Float or BtfKind.TypeTag => 0,
                _ => throw new ArgumentException($"Unknown BTF kind {(int)kind}; cannot determine its record length."),
            };
            position += 12;
            if (words > (end - position) / 4)
            {
                throw new ArgumentException("Truncated BTF type payload.", nameof(data));
            }

            var payload = new uint[words];
            for (int i = 0; i < words; i++)
            {
                payload[i] = this.Word(bytes, position + (i * 4));
            }

            // IDs continue from the base table's last ID, which is how split BTF references stay valid.
            uint id = checked((uint)this.types.Count + 1);
            this.types.Add(id, new BtfType(id, this.String(name), kind, size, flag, payload, this));
            position += words * 4;
        }
    }

    /// <summary>Gets whether the metadata words are little-endian, as determined by the magic number.</summary>
    public bool IsLittleEndian { get; }

    /// <summary>Gets the number of types, including those inherited from a base table.</summary>
    public int TypeCount => this.types.Count;

    /// <summary>Finds the ID of a uniquely named type; a name shared by several types is an error rather than a first match.</summary>
    /// <remarks>Native metadata may contain distinct types with identical display names. Guessing which one the
    /// caller meant could produce believable values at the wrong offsets, so ambiguity throws and the caller must
    /// supply a numeric ID from the metadata producer instead.</remarks>
    /// <param name="name">Type name to look for.</param>
    /// <returns>The unique matching type ID.</returns>
    public uint FindType(string name)
    {
        uint? found = null;
        foreach (BtfType type in this.types.Values)
        {
            if (type.Name == name)
            {
                if (found.HasValue)
                {
                    throw new ArgumentException($"BTF name '{name}' is ambiguous.", nameof(name));
                }

                found = type.Id;
            }
        }

        return found ?? throw new KeyNotFoundException(name);
    }

    /// <summary>Compiles the type graph reachable from one root into a schema, keeping functions and forward declarations as address-only.</summary>
    /// <remarks>Parsing indexed the whole table; importing compiles only what one consumer needs, so several roots can
    /// reuse one parsed table. A pointer to an incomplete target can still be read as stored bits but cannot be
    /// followed by value.</remarks>
    /// <param name="rootTypeId">Numeric ID of the root type, which may be inherited from a base table.</param>
    /// <param name="options">The pointer width, validation mode and descriptor budget; <see cref="MetadataImportOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Checked at each step of the walk and while compiling the schema.</param>
    /// <returns>The compiled reachable schema, the root's ID, and diagnostics.</returns>
    /// <exception cref="ArgumentException">The reachable graph is invalid or exceeds the descriptor budget.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are out of range.</exception>
    public MetadataImportResult Import(uint rootTypeId, MetadataImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= MetadataImportOptions.Default;
        options.Validate();
        var importer = new BtfImporter(this, options);
        importer.Import(rootTypeId, cancellationToken);
        var schema = new MemorySchema(importer.Definitions.Values, this.IsLittleEndian, maxTypes: options.MaxTypes, pointerSize: options.PointerSize, cancellationToken: cancellationToken, bestEffort: options.BestEffort);
        importer.Diagnostics.AddRange(schema.Diagnostics);
        return new MetadataImportResult(schema, Id(rootTypeId), importer.Diagnostics.AsReadOnly());
    }

    /// <summary>Describes one type record's own shape - kind, size, and (for a struct or union) direct members - without importing or validating any type it refers to.</summary>
    /// <remarks>
    /// Unlike <see cref="Import"/>, this never recurses into a member's or an element's own type: each is reported
    /// by its declared ID only, so describing one type never requires its dependents to import cleanly. This is
    /// the tool for finding out what a specific ID actually is - for example while diagnosing why <see cref="Import"/>
    /// rejected some type deep in a large graph - not a replacement for <see cref="Import"/> when a caller actually
    /// wants to read values.
    /// <para>
    /// A modifier chain (<c>typedef</c>, <c>const</c>, <c>volatile</c>, <c>restrict</c>, a type tag) is followed
    /// transparently to the underlying storage kind, the same way <see cref="Import"/> treats it; the reported name
    /// still prefers the originally requested declaration's own name when it has one. An invalid or cyclic type
    /// reference fails the same way it would during <see cref="Import"/>, since even a shallow description needs to
    /// know what kind the ID resolves to.
    /// </para>
    /// </remarks>
    /// <param name="id">Type ID to describe; zero is <c>void</c>.</param>
    /// <param name="pointerSize">Target pointer width in bytes, used only to size a pointer or an array of pointers.</param>
    /// <returns>A shallow description of the type record at <paramref name="id"/>.</returns>
    public BtfTypeDescription Describe(uint id, int pointerSize = 8)
    {
        BtfType type = this.Resolve(id);
        string displayName = this.DisplayName(id, type);
        BtfKind kind = type.Kind;
        int size = this.Size(id, pointerSize);

        uint? targetTypeId = kind == BtfKind.Ptr && type.Size != 0 ? type.Size : null;
        uint? elementTypeId = null;
        int elementCount = 0;
        IReadOnlyList<BtfMemberDescription> members = Array.Empty<BtfMemberDescription>();

        if (kind == BtfKind.Array)
        {
            elementTypeId = type.Payload[0];
            elementCount = checked((int)type.Payload[2]);
        }
        else if (kind is BtfKind.Struct or BtfKind.Union)
        {
            var described = new BtfMemberDescription[type.Payload.Length / 3];
            for (int i = 0; i < type.Payload.Length; i += 3)
            {
                MemberPlacement placement = this.ResolveMemberPlacement(type, i);
                described[i / 3] = new BtfMemberDescription(placement.Name, placement.MemberId, placement.Offset, placement.BitOffset, placement.BitWidth);
            }

            members = described;
        }

        return new BtfTypeDescription(id, displayName, kind, size, targetTypeId, elementTypeId, elementCount, members);
    }

    /// <summary>Formats a numeric BTF ID as a schema ID, which stays unique even when display names repeat.</summary>
    /// <param name="id">Numeric BTF type ID.</param>
    /// <returns>The schema ID.</returns>
    internal static string Id(uint id) => "btf:" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The name to show for a type: the declared record's own name (a typedef, say) when it has one, else the resolved storage record's.</summary>
    /// <param name="id">The declared type ID.</param>
    /// <param name="resolved">The record <paramref name="id"/> resolves to.</param>
    /// <returns>The display name, possibly empty.</returns>
    internal string DisplayName(uint id, BtfType resolved)
        => this.types.TryGetValue(id, out BtfType? declared) && declared.Name.Length > 0 ? declared.Name : resolved.Name;

    /// <summary>Reads one 32-bit metadata word in the blob's byte order.</summary>
    /// <param name="bytes">The whole blob.</param>
    /// <param name="offset">Byte offset of the word.</param>
    private uint Word(byte[] bytes, int offset)
    {
        return this.IsLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
    }

    /// <summary>Reads a zero-terminated UTF-8 string by table offset, delegating offsets below this table's start to the base table.</summary>
    /// <param name="offset">Offset into the combined string space of the base chain and this table.</param>
    /// <returns>The decoded string.</returns>
    internal string String(uint offset)
    {
        if (offset < this.baseStringLength)
        {
            return this.baseMetadata!.String(offset);
        }

        long local = (long)offset - this.baseStringLength;
        if (local < 0 || local >= this.strings.Length)
        {
            throw new ArgumentException("BTF string offset is outside its table.");
        }

        int end = Array.IndexOf(this.strings, (byte)0, (int)local);
        if (end < 0)
        {
            throw new ArgumentException("Unterminated BTF string.");
        }

        return StrictUtf8.GetString(this.strings, (int)local, end - (int)local);
    }

    /// <summary>Follows modifier records (typedef, const, volatile, restrict, and tags) to the underlying type that has a storage meaning.</summary>
    /// <remarks>A member's declared type may be <c>const volatile my_typedef</c>, three records deep. Resolution
    /// finds the record that defines storage while the caller keeps the member's original ID for provenance. A
    /// modifier cycle has no storage definition and must fail rather than consume unbounded work. For modifier
    /// kinds the record's size word holds the referenced type ID.</remarks>
    /// <param name="id">Type ID to resolve; zero is <c>void</c>.</param>
    /// <returns>The first non-modifier record reached.</returns>
    /// <exception cref="ArgumentException">The chain names a missing type, or reaches no storage record within 128 records (as a modifier cycle does not).</exception>
    internal BtfType Resolve(uint id)
    {
        // A cycle never reaches a storage record, so it runs into the hop limit; no visited set is needed.
        for (int hops = 0; hops < 128; hops++)
        {
            if (id == 0)
            {
                return new BtfType(0, "void", BtfKind.Void, 0, false, [], this);
            }

            if (!this.types.TryGetValue(id, out BtfType? type))
            {
                break;
            }

            if (!type.IsModifier)
            {
                return type;
            }

            id = type.Size;
        }

        throw new ArgumentException("BTF contains an invalid or cyclic type reference.");
    }

    /// <summary>Computes a type's byte size from the metadata: pointers use the target width, arrays multiply, and incomplete kinds are zero.</summary>
    /// <param name="id">Type ID whose size is wanted.</param>
    /// <param name="pointerSize">Target pointer width in bytes.</param>
    /// <param name="depth">Nesting depth for arrays of arrays, bounded to protect the call stack.</param>
    /// <returns>The size in bytes.</returns>
    internal int Size(uint id, int pointerSize, int depth = 0)
    {
        if (depth > 128)
        {
            throw new ArgumentException("BTF size graph exceeds its nesting limit.");
        }

        // ARRAY payload: element type, index type, element count. INT/STRUCT/UNION/ENUM/FLOAT/ENUM64 carry a byte size.
        BtfType type = this.Resolve(id);
        return type.Kind switch
        {
            BtfKind.Ptr => pointerSize,
            BtfKind.Array => checked((int)type.Payload[2] * this.Size(type.Payload[0], pointerSize, depth + 1)),
            _ when type.IsIncomplete => 0,
            BtfKind.Int or BtfKind.Struct or BtfKind.Union or BtfKind.Enum or BtfKind.Float or BtfKind.Enum64 => checked((int)type.Size),
            _ => throw new ArgumentException($"BTF kind {(int)type.Kind} is not a value type."),
        };
    }

    /// <summary>Computes one struct or union member's placement - name, declared (unresolved) type ID, byte offset, and bit slice if it is a bitfield - shared by <see cref="Import"/> and <see cref="Describe"/> so the two can never disagree about where a member actually sits.</summary>
    /// <remarks>
    /// Struct members are three payload words each: name, type, and an offset word. When the containing type's flag
    /// bit is set the offset word packs a bitfield width in its top 8 bits and the bit offset in the low 24;
    /// otherwise the whole word is a bit offset. Older BTF instead encoded a bitfield inside the member's own INT
    /// record (offset in bits 16-23, width in bits 0-7 of that record's payload word); such legacy slices are
    /// normalized here too.
    /// </remarks>
    /// <param name="containingType">The resolved struct or union record the member belongs to.</param>
    /// <param name="payloadIndex">Index of the member's first payload word, a multiple of 3.</param>
    /// <returns>The member's placement.</returns>
    internal MemberPlacement ResolveMemberPlacement(BtfType containingType, int payloadIndex)
    {
        string name = containingType.Owner.String(containingType.Payload[payloadIndex]);
        uint memberId = containingType.Payload[payloadIndex + 1];
        uint encoded = containingType.Payload[payloadIndex + 2];
        int bit = checked((int)(containingType.Flag ? encoded & 0xffffff : encoded));
        int width = containingType.Flag ? (int)(encoded >> 24) : 0;
        BtfType member = this.Resolve(memberId);
        if (!containingType.Flag && member.Kind == BtfKind.Int)
        {
            // Legacy encoding: the INT record itself says which bits of its storage the member uses.
            int legacyOffset = (int)((member.Payload[0] >> 16) & 255);
            int legacyWidth = (int)(member.Payload[0] & 255);
            if (legacyOffset + legacyWidth > member.Size * 8)
            {
                throw new ArgumentException("Legacy BTF integer slice exceeds its storage type.");
            }

            if (legacyOffset != 0 || legacyWidth != member.Size * 8)
            {
                bit = checked(bit + legacyOffset);
                width = legacyWidth;
                if (width == 0)
                {
                    throw new ArgumentException("A legacy BTF bitfield must have nonzero width.");
                }
            }
        }

        bool promoted = name.Length == 0;
        name = promoted ? "__anonymous_" + (payloadIndex / 3) : name;

        if (width == 0)
        {
            if (bit % 8 != 0)
            {
                throw new ArgumentException("Non-bitfield BTF member is not byte aligned.");
            }

            return new MemberPlacement(name, memberId, bit / 8, null, null, promoted);
        }

        if (member.Kind is not (BtfKind.Int or BtfKind.Enum or BtfKind.Enum64) || width > 64)
        {
            throw new ArgumentException("BTF bitfield requires integer or enum storage of at most 64 bits.");
        }

        // The referenced type is the compiler's own storage unit for the slice (a bitfield's BTF member always
        // names its full declared type, e.g. "unsigned long" for a three-bit flag, never a type shrunk to fit the
        // slice), so its declared byte size - not a size guessed from this one member's width - is what several
        // bitfields sharing that unit actually share. Aligning the byte offset down to that unit's own size,
        // instead of truncating the absolute bit offset to whole bytes, keeps every sibling slice inside the same
        // storage word rather than implying a fresh word starts wherever this particular member happens to begin.
        int storageBits = checked((int)member.Size * 8);
        int unitBitOffset = (bit / storageBits) * storageBits;
        int bitInUnit = bit - unitBitOffset;

        // BTF counts bits from the record's first byte; the schema counts from the storage unit's low bit, which
        // for big-endian storage is at the far end of the unit.
        int lowBit = this.IsLittleEndian ? bitInUnit : storageBits - bitInUnit - width;
        return new MemberPlacement(name, memberId, unitBitOffset / 8, lowBit, width, promoted);
    }

    /// <summary>One struct or union member's resolved placement, shared by <see cref="Import"/> and <see cref="Describe"/>.</summary>
    /// <param name="Name">Member name, with an anonymous member already given its generated name.</param>
    /// <param name="MemberId">The member's declared (unresolved) BTF type ID.</param>
    /// <param name="Offset">Byte offset from the start of the containing type.</param>
    /// <param name="BitOffset">Bit position within the storage unit at <paramref name="Offset"/>, or null for a whole-value member.</param>
    /// <param name="BitWidth">Bitfield width in bits, or null for a whole-value member.</param>
    /// <param name="Promoted">Whether the member was anonymous in BTF and so is promoted, making its own members visible in the parent.</param>
    internal readonly record struct MemberPlacement(string Name, uint MemberId, int Offset, int? BitOffset, int? BitWidth, bool Promoted);
}
