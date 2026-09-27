namespace CStructSharp.Memory.Metadata;

using System.Globalization;
using System.Text;

/// <summary>Converts the BTF types reachable from one root into memory type definitions, preserving recorded offsets.</summary>
/// <remarks>
/// <para>
/// Each kind maps to a definition: INT and FLOAT to core codecs, PTR and ARRAY to their target or element, STRUCT and
/// UNION to fields, ENUM and ENUM64 to a generated enum declaration, and the incomplete kinds to
/// <see cref="MemoryTypeKind.Incomplete"/> with a diagnostic.
/// </para>
/// <para>
/// A bitfield's absolute bit offset becomes a (byte offset, bit in storage unit) pair by aligning down to the
/// referenced type's own size (see <see cref="BtfMetadata.ResolveMemberPlacement"/>), so bitfields sharing one storage
/// word stay at the same byte offset. Each bitfield gets its own storage definition keyed after its struct.
/// </para>
/// <para>
/// The walk runs on <see cref="MetadataGraphWalk"/>. A type in <c>building</c> has started but not finished, so a
/// struct whose member points back to it (a list node) is imported once and the walk ends; every ID is visited at
/// most once.
/// </para>
/// </remarks>
internal sealed class BtfImporter
{
    private readonly BtfMetadata table;
    private readonly int pointerSize;
    private readonly int maxTypes;
    private readonly HashSet<uint> building = [];

    /// <summary>Starts an import over a parsed table.</summary>
    /// <param name="table">The parsed BTF table.</param>
    /// <param name="options">Validated import options.</param>
    public BtfImporter(BtfMetadata table, MetadataImportOptions options)
    {
        this.table = table;
        this.pointerSize = options.PointerSize;
        this.maxTypes = options.MaxTypes;
    }

    /// <summary>What a step of the walk does.</summary>
    private enum StepKind
    {
        /// <summary>Import a type ID unless it is already imported or in progress.</summary>
        Visit,

        /// <summary>Place the next member of a struct or union, or finish it.</summary>
        ResumeComposite,

        /// <summary>A pointer's target or an array's element is done, so the pointer or array is no longer in progress.</summary>
        FinishReference,
    }

    /// <summary>Gets the definitions imported so far, keyed by schema ID.</summary>
    public Dictionary<string, MemoryTypeDefinition> Definitions { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets notes about address-only types.</summary>
    public List<string> Diagnostics { get; } = [];

    /// <summary>Imports a root and everything it reaches.</summary>
    /// <param name="rootId">The root type ID.</param>
    /// <param name="cancellationToken">Checked before each step.</param>
    /// <exception cref="ArgumentException">A reachable type is invalid or unsupported, or the descriptor budget is exceeded.</exception>
    public void Import(uint rootId, CancellationToken cancellationToken)
        => MetadataGraphWalk.Run(new Step(StepKind.Visit, rootId, null), this.Run, cancellationToken);

    /// <summary>The generated Portable name of an enum's codec.</summary>
    /// <param name="id">The enum's type ID.</param>
    /// <returns>The enum name.</returns>
    private static string EnumName(uint id) => "__btf_enum_" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     A Portable enum declaration for an ENUM or ENUM64 record: ENUM entries are a name and a 32-bit value, ENUM64
    ///     entries add a high word, and the flag bit means signed.
    /// </summary>
    /// <param name="id">The enum's type ID.</param>
    /// <param name="type">The enum record.</param>
    /// <param name="size">The enum's size in bytes.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentException">A member name is not a Portable identifier.</exception>
    private static string EnumDeclaration(uint id, BtfType type, int size)
    {
        var declaration = new StringBuilder($"enum {EnumName(id)} : {(type.Flag ? "int" : "uint")}{size * 8} {{");
        int stride = type.Kind == BtfKind.Enum64 ? 3 : 2;
        for (int i = 0; i < type.Payload.Length; i += stride)
        {
            string name = type.Owner.String(type.Payload[i]);
            if (name.Length == 0 || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            {
                throw new ArgumentException("BTF enum member cannot be represented as a Portable identifier.");
            }

            ulong bits = type.Payload[i + 1] | (stride == 3 ? (ulong)type.Payload[i + 2] << 32 : 0);
            string value = type.Flag ? (stride == 3 ? unchecked((long)bits) : unchecked((int)bits)).ToString(CultureInfo.InvariantCulture) : bits.ToString(CultureInfo.InvariantCulture);
            declaration.Append(name).Append('=').Append(value).Append(',');
        }

        return declaration.Append("};").ToString();
    }

    /// <summary>Runs one step.</summary>
    /// <param name="step">The step.</param>
    /// <param name="work">The walk's stack.</param>
    private void Run(Step step, Stack<Step> work)
    {
        switch (step.Kind)
        {
        case StepKind.Visit:
            this.Visit(step.Id, work);
            break;
        case StepKind.ResumeComposite:
            this.ResumeComposite(step.Composite!, work);
            break;
        default:
            this.building.Remove(step.Id);
            break;
        }
    }

    /// <summary>Imports one type ID, scheduling what it references.</summary>
    /// <param name="id">The declared type ID.</param>
    /// <param name="work">The walk's stack.</param>
    private void Visit(uint id, Stack<Step> work)
    {
        string key = BtfMetadata.Id(id);
        if (this.Definitions.ContainsKey(key) || this.building.Contains(id))
        {
            return;
        }

        BtfType type = this.table.Resolve(id);
        this.building.Add(id);
        string displayName = this.table.DisplayName(id, type);
        int size = this.table.Size(id, this.pointerSize);
        string provenance = $"BTF v1 type {id}, terminal {type.Id}, kind {(int)type.Kind}";
        switch (type.Kind)
        {
        case BtfKind.Int:
        case BtfKind.Float:
            {
                // INT payload word: encoding flags in bits 24-27 (bit 24 = signed), legacy offset in 16-23, bit width in 0-7.
                string scalar = (type.Kind == BtfKind.Float ? "float" : type.IsSignedInt ? "int" : "uint") + (size * 8);
                if (type.Kind == BtfKind.Int && (((type.Payload[0] >> 16) & 255) != 0 || (type.Payload[0] & 255) != size * 8))
                {
                    throw new ArgumentException("Legacy BTF integer bit slices must be normalized as members before import.");
                }

                this.Add(key, new(key, displayName, MemoryTypeKind.Scalar, size, scalarType: scalar, provenance: provenance));
                this.building.Remove(id);
                break;
            }

        case BtfKind.Ptr:
            // The size word is the target type ID; zero means void, which imports as an opaque pointer.
            this.Add(key, new(key, displayName, MemoryTypeKind.Pointer, size, elementTypeId: type.Size == 0 ? null : BtfMetadata.Id(type.Size), provenance: provenance));
            if (type.Size != 0)
            {
                work.Push(new Step(StepKind.FinishReference, id, null));
                work.Push(new Step(StepKind.Visit, type.Size, null));
            }
            else
            {
                this.building.Remove(id);
            }

            break;
        case BtfKind.Array:
            // Payload: element type, index type, element count.
            if (this.table.Resolve(type.Payload[1]).Kind != BtfKind.Int)
            {
                throw new ArgumentException("BTF array index type must be an integer.");
            }

            this.Add(key, new(key, displayName, MemoryTypeKind.Array, size, elementTypeId: BtfMetadata.Id(type.Payload[0]), count: checked((int)type.Payload[2]), provenance: provenance));
            work.Push(new Step(StepKind.FinishReference, id, null));
            work.Push(new Step(StepKind.Visit, type.Payload[0], null));
            break;
        case BtfKind.Struct:
        case BtfKind.Union:
            work.Push(new Step(StepKind.ResumeComposite, id, new CompositeImport(id, type, key, displayName, size, provenance)));
            break;
        case BtfKind.Enum:
        case BtfKind.Enum64:
            this.Add(key, new(key, displayName, MemoryTypeKind.Scalar, size, scalarType: EnumName(id), declaration: EnumDeclaration(id, type, size), provenance: provenance));
            this.building.Remove(id);
            break;
        case var _ when type.IsIncomplete:
            // void, forward declarations, functions, and prototypes can be pointed at but never read by value.
            this.Add(key, new(key, displayName, MemoryTypeKind.Incomplete, 0, provenance: provenance));
            this.Diagnostics.Add($"{key}: {type.Name} is incomplete or callable; address-only use is supported.");
            this.building.Remove(id);
            break;
        default:
            throw new ArgumentException($"BTF kind {(int)type.Kind} is not supported as a value type.");
        }
    }

    /// <summary>Places a struct's or union's next member, or finishes the composite once every member is placed.</summary>
    /// <remarks>A member's own type is scheduled above the composite rather than imported here, so control returns to
    /// the composite, at its next member, only once that member and everything it reaches are done.</remarks>
    /// <param name="composite">The composite in progress.</param>
    /// <param name="work">The walk's stack.</param>
    private void ResumeComposite(CompositeImport composite, Stack<Step> work)
    {
        if (composite.PayloadIndex >= composite.Type.Payload.Length)
        {
            MemoryTypeKind kind = composite.Type.Kind == BtfKind.Struct ? MemoryTypeKind.Struct : MemoryTypeKind.Union;
            this.Add(composite.Key, new(composite.Key, composite.DisplayName, kind, composite.Size, composite.Fields, provenance: composite.Provenance));
            this.building.Remove(composite.Id);
            return;
        }

        int index = composite.PayloadIndex;
        composite.PayloadIndex = index + 3;
        BtfMetadata.MemberPlacement placement = this.table.ResolveMemberPlacement(composite.Type, index);
        work.Push(new Step(StepKind.ResumeComposite, composite.Id, composite));
        if (placement.BitWidth is int width)
        {
            // A bitfield reads its bits from its own unsigned storage definition; the field records the signedness.
            BtfType member = this.table.Resolve(placement.MemberId);
            bool signed = member.Kind == BtfKind.Int ? member.IsSignedInt : member.Flag;
            int storage = checked((int)member.Size);
            string memberKey = composite.Key + ":bits:" + index;
            this.Add(memberKey, new(memberKey, placement.Name, MemoryTypeKind.Scalar, storage, scalarType: "uint" + (storage * 8), provenance: composite.Provenance));
            composite.Fields.Add(new(placement.Name, memberKey, placement.Offset, placement.BitOffset, width, signed));
        }
        else
        {
            composite.Fields.Add(new(placement.Name, BtfMetadata.Id(placement.MemberId), placement.Offset, promoted: placement.Promoted));
            work.Push(new Step(StepKind.Visit, placement.MemberId, null));
        }
    }

    /// <summary>Adds a definition within the descriptor budget.</summary>
    /// <param name="key">The schema ID.</param>
    /// <param name="definition">The definition.</param>
    /// <exception cref="ArgumentException">The budget is exceeded.</exception>
    private void Add(string key, MemoryTypeDefinition definition)
    {
        if (this.Definitions.Count >= this.maxTypes)
        {
            throw new ArgumentException("BTF import exceeds its descriptor budget.");
        }

        this.Definitions.Add(key, definition);
    }

    /// <summary>One step of the walk.</summary>
    /// <param name="Kind">What the step does.</param>
    /// <param name="Id">The type ID the step concerns.</param>
    /// <param name="Composite">The composite in progress, for <see cref="StepKind.ResumeComposite"/>.</param>
    private readonly record struct Step(StepKind Kind, uint Id, CompositeImport? Composite);

    /// <summary>A struct or union import in progress: the fields placed so far, and the next member.</summary>
    /// <param name="id">The composite's type ID.</param>
    /// <param name="type">The resolved struct or union record.</param>
    /// <param name="key">The schema ID of the finished definition.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="size">The declared size in bytes.</param>
    /// <param name="provenance">The provenance note for the definition and its bitfield storage.</param>
    private sealed class CompositeImport(uint id, BtfType type, string key, string displayName, int size, string provenance)
    {
        /// <summary>Gets the composite's type ID.</summary>
        public uint Id { get; } = id;

        /// <summary>Gets the resolved struct or union record.</summary>
        public BtfType Type { get; } = type;

        /// <summary>Gets the schema ID of the finished definition.</summary>
        public string Key { get; } = key;

        /// <summary>Gets the display name.</summary>
        public string DisplayName { get; } = displayName;

        /// <summary>Gets the declared size in bytes.</summary>
        public int Size { get; } = size;

        /// <summary>Gets the provenance note.</summary>
        public string Provenance { get; } = provenance;

        /// <summary>Gets the fields placed so far, in declaration order.</summary>
        public List<MemoryField> Fields { get; } = [];

        /// <summary>Gets or sets the payload index (a multiple of 3) of the next member.</summary>
        public int PayloadIndex { get; set; }
    }
}
