namespace CStructSharp.Memory.Metadata;

using System.Globalization;
using System.Text;
using System.Text.Json;
using CStructSharp.Diagnostics;

/// <summary>A parsed Volatility ISF 6.2.0 document, from which the value types reachable from one user type can be imported into a <see cref="MemorySchema"/>.</summary>
/// <remarks>
/// <para>
/// ISF (Intermediate Symbol Format) is the JSON metadata the Volatility 3 memory-forensics framework uses to
/// describe an operating system build. A document has four tables: <c>base_types</c> (integers, floats, booleans,
/// with size, signedness, and byte order), <c>user_types</c> (structs, classes, and unions with a size and a
/// <c>fields</c> object mapping member names to an offset and a type descriptor), <c>enums</c> (constants over a
/// base type), and <c>symbols</c> (named addresses). Type descriptors nest: a field's type may be a base type, a
/// user type, an enum, a pointer or array with a <c>subtype</c>, a bitfield with a position and length, or a
/// function.
/// </para>
/// <para>
/// <see cref="Import"/> consumes the first three tables for one root user type and everything it references, so
/// several roots can reuse one parsed document. It keeps the explicit byte order of base types, takes the target
/// pointer width as an import option (a 32-bit profile analyzed on a 64-bit host must say so), and reserves each
/// composite's identity before following its fields so recursive pointers terminate.
/// </para>
/// <para>
/// A reference to a type name that the document does not define imports as an address-only
/// <see cref="MemoryTypeKind.Incomplete"/> type with a diagnostic. Kernel profiles contain many such names: C lets a
/// struct hold a pointer to a type that is only forward-declared (<c>struct files_struct *files;</c>), and the profile
/// generator leaves the pointed-to type out of <c>user_types</c>. A pointer to it reads as an address. Embedding it by
/// value has no size to lay out, so the containing type fails validation, or becomes a raw-bytes placeholder in a
/// best-effort import.
/// </para>
/// <para>
/// A bitfield's recorded <c>offset</c> and <c>bit_position</c> name an integer of its storage type at that byte offset
/// and a slice of that integer counted from its low bit. Profile generators differ on which byte they record: the start
/// of the compiler's storage unit, or the byte that holds the slice's first bit, so that a two-byte slice in the last
/// byte of a struct appears to overrun it. The importer moves each slice to the storage unit, aligned to its own size
/// from the start of the struct, that holds the same physical bits, which is how a C compiler allocates bitfields and
/// how <see cref="BtfMetadata"/> places them. A slice that crosses such a unit, as in a packed struct, keeps its
/// recorded placement.
/// </para>
/// <para>
/// <see cref="UserTypeNames"/>, <see cref="Symbols"/> and <see cref="TryGetSymbol"/> read the <c>user_types</c> and
/// <c>symbols</c> tables directly. A symbol's address is reported as recorded: relocations such as kernel address space
/// layout randomization (KASLR) are not applied, and no file is opened.
/// </para>
/// <para>
/// A document that is not valid JSON, is not ISF 6.2.0, or lacks or mistypes a property the import needs throws
/// <see cref="CStructLayoutException"/>, as an invalid layout declaration does. Parameter errors keep their .NET
/// exception types.
/// </para>
/// </remarks>
public sealed class IsfMetadata
{
    private readonly JsonElement root;
    private readonly Lazy<Dictionary<string, JsonElement>> userTypes;
    private readonly Lazy<Dictionary<string, JsonElement>> baseTypes;
    private readonly Lazy<Dictionary<string, JsonElement>> enums;
    private readonly Lazy<IReadOnlyList<string>> userTypeNames;
    private readonly Lazy<IReadOnlyDictionary<string, ulong>> symbols;

    /// <summary>Parses a UTF-8 ISF 6.2.0 document.</summary>
    /// <param name="json">The UTF-8 document.</param>
    /// <param name="isLittleEndian">Byte order of the imported schemas; base types with an explicit order override it.</param>
    /// <param name="maxBytes">Maximum accepted document length in bytes. JSON nesting is limited to 256 levels, the default layout nesting limit.</param>
    /// <param name="cancellationToken">Checked before parsing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is not positive.</exception>
    /// <exception cref="CStructLayoutException">The document exceeds its budget, is not valid JSON, or is not ISF 6.2.0.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public IsfMetadata(ReadOnlyMemory<byte> json, bool isLittleEndian = true, int maxBytes = 16 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (json.Length > maxBytes)
        {
            throw new CStructLayoutException("ISF metadata exceeds its byte budget.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MemorySchema.MaxDefinitionNestingDepth, });

            // A cloned element owns its data, so the document can be released now.
            this.root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new CStructLayoutException("ISF metadata is not valid JSON.", exception);
        }

        if (this.root.ValueKind != JsonValueKind.Object || !this.root.TryGetProperty("metadata", out JsonElement metadata) ||
            metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty("format", out JsonElement format) ||
            format.ValueKind != JsonValueKind.String || !format.ValueEquals("6.2.0"))
        {
            throw new CStructLayoutException("Only ISF format 6.2.0 is supported.");
        }

        this.IsLittleEndian = isLittleEndian;

        // The tables are indexed on first use, so a caller that only reads symbols never indexes types and vice versa.
        // A JSON object lookup is a linear scan, and a kernel profile has tens of thousands of entries per table.
        this.userTypes = new(() => this.Index("user_types"));
        this.baseTypes = new(() => this.Index("base_types"));
        this.enums = new(() => this.Index("enums"));
        this.userTypeNames = new(() => Array.AsReadOnly(this.userTypes.Value.Keys.Order(StringComparer.Ordinal).ToArray()));
        this.symbols = new(this.ReadSymbols);
    }

    /// <summary>Gets the byte order of the imported schemas; base types with an explicit order override it.</summary>
    public bool IsLittleEndian { get; }

    /// <summary>Gets the names in the <c>user_types</c> table (the structs, classes and unions <see cref="Import"/> accepts as a root), in ordinal order.</summary>
    /// <remarks>The list is built on first access. A document without a <c>user_types</c> table has no names.</remarks>
    /// <exception cref="CStructLayoutException">The <c>user_types</c> table is not a JSON object.</exception>
    public IReadOnlyList<string> UserTypeNames => this.userTypeNames.Value;

    /// <summary>Gets the <c>symbols</c> table: each symbol's name and its recorded address.</summary>
    /// <remarks>
    /// <para>
    /// An address is a virtual address in the analyzed image, as the profile generator recorded it; relocations such as
    /// kernel address space layout randomization (KASLR) are not applied. Some generators write an address in the top
    /// half of a 64-bit address space, such as <c>0xffffffff82614940</c>, as the negative signed number with the same
    /// bits; such a number is returned as that unsigned bit pattern.
    /// </para>
    /// <para>
    /// The table is read and checked on first access. A document without a <c>symbols</c> table has no symbols. Other
    /// properties of a symbol, such as its <c>type</c>, are not read.
    /// </para>
    /// </remarks>
    /// <exception cref="CStructLayoutException">The <c>symbols</c> table is not a JSON object, or a symbol has no integer <c>address</c> in the signed or unsigned 64-bit range.</exception>
    public IReadOnlyDictionary<string, ulong> Symbols => this.symbols.Value;

    /// <summary>Looks up a symbol's recorded address by its exact (case-sensitive) name.</summary>
    /// <remarks>See <see cref="Symbols"/> for how addresses are read; the first lookup reads the whole table.</remarks>
    /// <param name="name">The symbol name, such as <c>init_task</c>.</param>
    /// <param name="address">The recorded address when the symbol exists; otherwise zero.</param>
    /// <returns>True when the <c>symbols</c> table has an entry named <paramref name="name"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="CStructLayoutException">The <c>symbols</c> table is malformed.</exception>
    public bool TryGetSymbol(string name, out ulong address)
    {
        ArgumentNullException.ThrowIfNull(name);
        return this.Symbols.TryGetValue(name, out address);
    }

    /// <summary>Imports one named user type and its reachable dependencies.</summary>
    /// <remarks>The result owns compiled descriptors, not the parsed JSON. Use its <see cref="MetadataImportResult.RootTypeId"/>
    /// in session operations and inspect its diagnostics for retained address-only types. Choosing a profile that
    /// matches a capture and locating a record are the caller's responsibilities; a valid document establishes
    /// neither.</remarks>
    /// <param name="rootName">Name in <c>user_types</c> to import together with its reachable dependencies.</param>
    /// <param name="options">The pointer width, validation mode and descriptor budget; <see cref="MetadataImportOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Checked at each step of the walk and while compiling the schema.</param>
    /// <returns>The compiled reachable schema, the root's ID, and diagnostics.</returns>
    /// <exception cref="ArgumentException"><paramref name="rootName"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rootName"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are out of range.</exception>
    /// <exception cref="CStructLayoutException">
    ///     <paramref name="rootName"/> is not in <c>user_types</c>, or a reachable type is malformed, unsupported,
    ///     embeds an undefined type by value (in a strict import), or exceeds the descriptor budget.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public MetadataImportResult Import(string rootName, MetadataImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        options = MetadataImportOptions.ValidateOrDefault(options);
        var importer = new Importer(this, options);
        string id = importer.Import(rootName, cancellationToken);
        return MetadataImportResult.Compile(importer.Types.Values, id, importer.Diagnostics, this.IsLittleEndian, options, cancellationToken);
    }

    /// <summary>Indexes one top-level table by entry name.</summary>
    /// <remarks>JSON allows a repeated property name; the last entry wins, as it does for <see cref="JsonElement.GetProperty(string)"/>.</remarks>
    /// <param name="table">The table's property name, such as <c>user_types</c>.</param>
    /// <returns>The entries by name; empty when the document has no such table.</returns>
    /// <exception cref="CStructLayoutException">The table is not a JSON object.</exception>
    private Dictionary<string, JsonElement> Index(string table)
    {
        var entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!this.root.TryGetProperty(table, out JsonElement element))
        {
            return entries;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CStructLayoutException($"ISF table '{table}' is not a JSON object.");
        }

        foreach (JsonProperty entry in element.EnumerateObject())
        {
            entries[entry.Name] = entry.Value;
        }

        return entries;
    }

    /// <summary>Reads every symbol's address from the <c>symbols</c> table.</summary>
    /// <returns>The read-only addresses by symbol name.</returns>
    /// <exception cref="CStructLayoutException">The table is not an object, or a symbol has no integer address.</exception>
    private IReadOnlyDictionary<string, ulong> ReadSymbols()
    {
        var addresses = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach ((string name, JsonElement symbol) in this.Index("symbols"))
        {
            if (symbol.ValueKind != JsonValueKind.Object || !symbol.TryGetProperty("address", out JsonElement address) || address.ValueKind != JsonValueKind.Number)
            {
                throw new CStructLayoutException($"ISF symbol '{name}' has no integer address.");
            }

            // Both readers reject a fraction or an exponent, so only an exact integer is accepted.
            if (address.TryGetUInt64(out ulong unsigned))
            {
                addresses.Add(name, unsigned);
            }
            else if (address.TryGetInt64(out long signed))
            {
                // A negative address is a top-half address written as a signed number; keep its 64-bit pattern.
                addresses.Add(name, unchecked((ulong)signed));
            }
            else
            {
                throw new CStructLayoutException($"ISF symbol '{name}' has no integer address in the 64-bit range.");
            }
        }

        return addresses.AsReadOnly();
    }

    /// <summary>The state of one import: the descriptors built so far, the generated-ID counter, and diagnostics.</summary>
    /// <remarks>
    /// The walk runs on <see cref="MetadataGraphWalk"/>, depth first, and numbers generated types (pointers, arrays,
    /// functions, enum codecs) in the order it meets them. A type reached by name gets its ID from the name, so it is
    /// imported once; a user type is reserved as an empty composite before its fields are followed, so a field that
    /// points back to it finds the ID and stops. Only by-value recursion is invalid, and <see cref="MemorySchema"/>
    /// checks for that. A name missing from its table is recorded once as an incomplete type under the same ID.
    /// </remarks>
    private sealed class Importer
    {
        private readonly IsfMetadata document;
        private readonly int pointerSize;
        private readonly int maxTypes;
        private int nextId;

        /// <summary>Starts an import over a parsed document.</summary>
        /// <param name="document">The parsed document, whose table indexes the import reads.</param>
        /// <param name="options">Validated import options.</param>
        internal Importer(IsfMetadata document, MetadataImportOptions options)
        {
            this.document = document;
            this.pointerSize = options.PointerSize;
            this.maxTypes = options.MaxTypes;
        }

        /// <summary>What a step of the walk does.</summary>
        private enum StepKind
        {
            /// <summary>Import a user type by name: the root.</summary>
            UserType,

            /// <summary>Import the type a descriptor names, under the ID already chosen for it.</summary>
            Descriptor,

            /// <summary>Follow the next field of a user type, or finish it.</summary>
            ResumeComposite,

            /// <summary>A pointer's or array's subtype is imported, so its own descriptor can be added.</summary>
            FinishReference,
        }

        /// <summary>Gets the descriptors created so far, keyed by ID.</summary>
        internal Dictionary<string, MemoryTypeDefinition> Types { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets notes about address-only types retained during import.</summary>
        internal List<string> Diagnostics { get; } = [];

        /// <summary>Imports a user type and everything it reaches.</summary>
        /// <param name="rootName">The user type's name.</param>
        /// <param name="cancellationToken">Checked before each step.</param>
        /// <returns>The root's ID.</returns>
        /// <exception cref="CStructLayoutException">The root or a reachable type is missing, malformed, unsupported, or over budget.</exception>
        internal string Import(string rootName, CancellationToken cancellationToken)
        {
            // Any other name may be a forward declaration, but the root must have a layout to import.
            if (!this.document.userTypes.Value.ContainsKey(rootName))
            {
                throw new CStructLayoutException($"ISF user type '{rootName}' is not in user_types.");
            }

            string id = UserId(rootName);
            try
            {
                MetadataGraphWalk.Run(new Step(StepKind.UserType, id, default, null, rootName), this.Run, cancellationToken);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
            {
                // The JSON accessors report a missing property as KeyNotFoundException, a wrongly typed one as
                // InvalidOperationException or FormatException, and an out-of-range number as OverflowException; a
                // descriptor constructor rejects a negative size or offset with ArgumentException. In an import each
                // of these means the document is malformed, which is a layout error like any other invalid metadata.
                throw new CStructLayoutException("Invalid ISF metadata: " + exception.Message, exception);
            }

            return id;
        }

        /// <summary>The ID of a user type.</summary>
        /// <param name="name">The name in <c>user_types</c>.</param>
        /// <returns>The ID.</returns>
        private static string UserId(string name) => "isf:user:" + name;

        /// <summary>The ID of a base type.</summary>
        /// <param name="name">The name in <c>base_types</c>.</param>
        /// <returns>The ID.</returns>
        private static string BaseId(string name) => "isf:base:" + name;

        /// <summary>
        ///     Moves a bitfield to the storage unit that holds its bits: an integer of the storage type's size, at a
        ///     multiple of that size from the start of the containing type, as a C compiler allocates it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ISF reads a bitfield as an integer of its storage type at <paramref name="offset"/>, in that type's byte
        /// order, and selects <paramref name="width"/> bits starting at bit <paramref name="bitPosition"/>, counted from
        /// the integer's low bit. Some profile generators record the byte that holds the slice's first bit as the offset
        /// instead of the start of the storage unit, so the integer they describe can run past the end of its struct.
        /// Any integer that contains the same physical bits selects the same value, so moving the slice changes nothing a
        /// read or write sees, and it makes every slice of one unit share that unit's offset.
        /// </para>
        /// <para>
        /// The slice's position is first expressed as a bit index counted from the struct's first byte in the unit's own
        /// bit order: low bit first for little-endian storage, and high bit first for big-endian storage, whose low bits
        /// are in its last byte. Dividing that index by the unit's width in bits gives the unit. For example, a
        /// little-endian two-byte unit with offset 7 and bit position 0 starts at struct bit 56, which is bit 8 of the
        /// unit at byte 6.
        /// </para>
        /// <para>
        /// The choices, in order: the aligned unit of the declared size; the recorded placement, when it fits the
        /// containing type (a slice that crosses an aligned unit, as a packed struct can have); and the widest narrower
        /// aligned unit that holds the slice and fits, for a packed struct whose last bits cannot hold a whole declared
        /// unit, such as a four-bit <c>unsigned int</c> slice in the last two bytes of a six-byte struct. An invalid slice,
        /// or one that no unit holds, keeps its recorded placement for schema validation to accept or reject.
        /// </para>
        /// </remarks>
        /// <param name="offset">The recorded byte offset.</param>
        /// <param name="bitPosition">The recorded bit position, counted from the low bit of the integer at <paramref name="offset"/>.</param>
        /// <param name="width">The slice's width in bits.</param>
        /// <param name="storageSize">The storage type's size in bytes.</param>
        /// <param name="littleEndian">Whether the storage type is little-endian.</param>
        /// <param name="containerSize">The containing type's size in bytes.</param>
        /// <returns>The unit's byte offset, the slice's position from the unit's low bit, and the unit's size in bytes.</returns>
        private static (int Offset, int BitPosition, int UnitSize) StorageUnitPlacement(int offset, int bitPosition, int width, int storageSize, bool littleEndian, int containerSize)
        {
            if (storageSize is not (1 or 2 or 4 or 8) || offset < 0 || bitPosition < 0 || width <= 0 || bitPosition + (long)width > storageSize * 8L)
            {
                return (offset, bitPosition, storageSize);
            }

            long first = (offset * 8L) + (littleEndian ? bitPosition : (storageSize * 8L) - bitPosition - width);
            if (TryUnit(storageSize, out (int, int, int) placement))
            {
                return placement;
            }

            if ((long)offset + storageSize <= containerSize)
            {
                return (offset, bitPosition, storageSize);
            }

            for (int size = storageSize / 2; size >= 1; size /= 2)
            {
                if (TryUnit(size, out placement))
                {
                    return placement;
                }
            }

            return (offset, bitPosition, storageSize);

            // Places the slice in the aligned unit of the given size that holds its first bit, if the whole slice fits
            // that unit and the unit fits the containing type.
            bool TryUnit(int size, out (int Offset, int BitPosition, int UnitSize) unit)
            {
                long unitBits = size * 8L;
                long unitOffset = first / unitBits * size;
                long bitInUnit = first - (unitOffset * 8);
                bool fits = bitInUnit + width <= unitBits && unitOffset + size <= containerSize;
                unit = fits ? ((int)unitOffset, (int)(littleEndian ? bitInUnit : unitBits - bitInUnit - width), size) : default;
                return fits;
            }
        }

        /// <summary>Runs one step.</summary>
        /// <param name="step">The step.</param>
        /// <param name="work">The walk's stack.</param>
        private void Run(Step step, Stack<Step> work)
        {
            switch (step.Kind)
            {
            case StepKind.UserType:
                this.StartUserType(step.Id, step.Text!, work);
                break;
            case StepKind.Descriptor:
                this.ImportDescriptor(step.Id, step.Descriptor, work);
                break;
            case StepKind.ResumeComposite:
                this.ResumeComposite(step.Composite!, work);
                break;
            default:
                this.FinishReference(step.Id, step.Descriptor, step.Text!);
                break;
            }
        }

        /// <summary>
        ///     Chooses the ID of the type a descriptor names: a named type's ID comes from its name; a pointer, array or
        ///     function gets the next generated ID, in the order the walk meets it.
        /// </summary>
        /// <param name="descriptor">The descriptor object with a <c>kind</c> property.</param>
        /// <returns>The ID.</returns>
        private string DescriptorId(JsonElement descriptor)
        {
            return descriptor.GetProperty("kind").GetString() switch
            {
                "base" => BaseId(descriptor.GetProperty("name").GetString()!),
                "struct" or "class" or "union" => UserId(descriptor.GetProperty("name").GetString()!),
                "enum" => "isf:enum:" + descriptor.GetProperty("name").GetString()!,
                _ => "isf:generated:" + this.nextId++,
            };
        }

        /// <summary>Imports the type a descriptor names, unless a named type is already imported or reserved.</summary>
        /// <param name="id">The ID chosen by <see cref="DescriptorId"/>.</param>
        /// <param name="descriptor">The descriptor.</param>
        /// <param name="work">The walk's stack.</param>
        private void ImportDescriptor(string id, JsonElement descriptor, Stack<Step> work)
        {
            if (this.Types.ContainsKey(id))
            {
                return;
            }

            string kind = descriptor.GetProperty("kind").GetString()!;
            switch (kind)
            {
            case "base":
                this.ImportBase(descriptor.GetProperty("name").GetString()!);
                break;
            case "struct" or "class" or "union":
                this.StartUserType(id, descriptor.GetProperty("name").GetString()!, work);
                break;
            case "enum":
                this.ImportEnum(id, descriptor.GetProperty("name").GetString()!);
                break;
            case "pointer" or "array":
                // Added once its subtype is: an array's size is its element's size times its count.
                JsonElement subtype = descriptor.GetProperty("subtype");
                string element = this.DescriptorId(subtype);
                work.Push(new Step(StepKind.FinishReference, id, descriptor, null, element));
                work.Push(new Step(StepKind.Descriptor, element, subtype, null, null));
                break;
            case "function":
                this.Add(id, new(id, "function", MemoryTypeKind.Incomplete, 0));
                this.Diagnostics.Add($"{id}: function type is address-only.");
                break;
            default:
                throw new CStructLayoutException($"Unsupported ISF descriptor '{kind}'.");
            }
        }

        /// <summary>Imports a base type, mapped to the core codec of the same size and signedness.</summary>
        /// <param name="name">The name in <c>base_types</c>.</param>
        /// <returns>The base type's ID.</returns>
        private string ImportBase(string name)
        {
            string id = BaseId(name);
            if (this.Types.ContainsKey(id))
            {
                return id;
            }

            if (!this.document.baseTypes.Value.TryGetValue(name, out JsonElement type))
            {
                this.AddUndefined(id, name, "base type");
                return id;
            }

            int size = type.GetProperty("size").GetInt32();
            string kind = type.GetProperty("kind").GetString()!;
            if (kind == "void")
            {
                this.Add(id, new(id, name, MemoryTypeKind.Incomplete, 0, provenance: id));
                return id;
            }

            string endian = type.GetProperty("endian").GetString()!;
            if (endian is not ("little" or "big"))
            {
                throw new CStructLayoutException("Invalid ISF byte order.");
            }

            bool signed = type.GetProperty("signed").GetBoolean();
            string scalar = kind switch
            {
                "int" or "char" => (signed ? "int" : "uint") + (size * 8),
                "float" => "float" + (size * 8),
                "bool" when size == 1 => "bool",
                _ => throw new CStructLayoutException($"Unsupported ISF base type '{kind}' of size {size}."),
            };
            this.Add(id, new(id, name, MemoryTypeKind.Scalar, size, scalarType: scalar, provenance: id, isLittleEndian: endian == "little"));
            return id;
        }

        /// <summary>Reserves a user type as an empty composite, so recursive pointers find it, and schedules its fields.</summary>
        /// <param name="id">The user type's ID.</param>
        /// <param name="name">The name in <c>user_types</c>.</param>
        /// <param name="work">The walk's stack.</param>
        private void StartUserType(string id, string name, Stack<Step> work)
        {
            if (this.Types.ContainsKey(id))
            {
                return;
            }

            if (!this.document.userTypes.Value.TryGetValue(name, out JsonElement type))
            {
                this.AddUndefined(id, name, "user type");
                return;
            }

            int size = type.GetProperty("size").GetInt32();
            MemoryTypeKind kind = type.GetProperty("kind").GetString() switch
            {
                "struct" or "class" => MemoryTypeKind.Struct,
                "union" => MemoryTypeKind.Union,
                var other => throw new CStructLayoutException($"Unsupported ISF user type kind '{other}'."),
            };

            this.Add(id, new(id, name, kind, size, provenance: id));
            work.Push(new Step(StepKind.ResumeComposite, id, default, new CompositeImport(id, name, kind, size, type.GetProperty("fields").EnumerateObject().ToArray()), null));
        }

        /// <summary>Follows a user type's next field, or replaces its reservation with the full field list once every field is done.</summary>
        /// <remarks>The field's type is scheduled above the composite, so control returns here, at the next field, only
        /// once that type and everything it reaches are imported.</remarks>
        /// <param name="composite">The user type in progress.</param>
        /// <param name="work">The walk's stack.</param>
        private void ResumeComposite(CompositeImport composite, Stack<Step> work)
        {
            if (composite.Next >= composite.Members.Length)
            {
                this.Types[composite.Id] = new(composite.Id, composite.Name, composite.Kind, composite.Size, composite.Fields, provenance: composite.Id);
                return;
            }

            JsonProperty property = composite.Members[composite.Next++];
            work.Push(new Step(StepKind.ResumeComposite, composite.Id, default, composite, null));
            JsonElement field = property.Value;
            JsonElement descriptor = field.GetProperty("type");
            int offset = field.GetProperty("offset").GetInt32();
            if (descriptor.GetProperty("kind").GetString() == "bitfield")
            {
                // A bitfield descriptor wraps its storage type and adds a bit position and length within it.
                JsonElement storage = descriptor.GetProperty("type");
                string storageId = this.DescriptorId(storage);
                int bit = descriptor.GetProperty("bit_position").GetInt32();
                int width = descriptor.GetProperty("bit_length").GetInt32();
                bool signed = false;
                bool importStorage = true;
                if (this.TryGetStorage(storage, out int storageSize, out bool littleEndian, out signed, out bool isBool))
                {
                    (offset, bit, int unitSize) = StorageUnitPlacement(offset, bit, width, storageSize, littleEndian, composite.Size);

                    // A narrower unit than the declared type, or a C _Bool (whose codec is not an integer a slice can be
                    // cut from), reads its bits through a plain unsigned integer of the unit's size and byte order.
                    if (unitSize != storageSize || isBool)
                    {
                        storageId = this.UnsignedStorage(unitSize, littleEndian);
                        importStorage = false;
                    }
                }

                composite.Fields.Add(new(property.Name, storageId, offset, bit, width, signed));
                if (importStorage)
                {
                    work.Push(new Step(StepKind.Descriptor, storageId, storage, null, null));
                }
            }
            else
            {
                bool promoted = field.TryGetProperty("anonymous", out JsonElement anonymous) && anonymous.GetBoolean();
                string typeId = this.DescriptorId(descriptor);
                composite.Fields.Add(new(property.Name, typeId, offset, promoted: promoted));
                work.Push(new Step(StepKind.Descriptor, typeId, descriptor, null, null));
            }
        }

        /// <summary>Adds a pointer or array once its subtype is imported.</summary>
        /// <param name="id">The pointer's or array's generated ID.</param>
        /// <param name="descriptor">The pointer or array descriptor.</param>
        /// <param name="element">The subtype's ID.</param>
        private void FinishReference(string id, JsonElement descriptor, string element)
        {
            bool isArray = descriptor.GetProperty("kind").GetString() == "array";
            int count = isArray ? descriptor.GetProperty("count").GetInt32() : 0;
            int size = isArray ? checked(count * this.Types[element].Size) : this.pointerSize;

            // A pointer may name its own storage base type; it must agree with the supplied pointer width.
            if (descriptor.TryGetProperty("base", out JsonElement pointerBase) && this.Types[this.ImportBase(pointerBase.GetString()!)].Size != this.pointerSize)
            {
                throw new CStructLayoutException("ISF pointer base differs from the supplied pointer width.");
            }

            this.Add(id, new(id, string.Empty, isArray ? MemoryTypeKind.Array : MemoryTypeKind.Pointer, size, elementTypeId: element, count: count, provenance: "ISF 6.2.0 " + (isArray ? "array" : "pointer")));
        }

        /// <summary>Imports an enum as a scalar whose codec is a generated Portable enum declaration over an integer of the enum's size.</summary>
        /// <remarks>
        /// The enum's own <c>size</c> is what it occupies in memory, and profile generators do not always record a base
        /// type of that size: a packed C enum (<c>enum rw_hint { ... } __packed;</c>) is one byte wide but may name
        /// <c>unsigned int</c> as its base. Its constants also decide its signedness, because a C compiler gives an enum
        /// with a negative constant a signed type, while some generators still record an unsigned base. So an integer
        /// base contributes only its byte order and its signedness, a negative constant makes the storage signed, and the
        /// width comes from <c>size</c>.
        /// </remarks>
        /// <param name="id">The enum's ID.</param>
        /// <param name="name">Enum name in the <c>enums</c> table.</param>
        private void ImportEnum(string id, string name)
        {
            if (!this.document.enums.Value.TryGetValue(name, out JsonElement data))
            {
                this.AddUndefined(id, name, "enum");
                return;
            }

            // An enum's base type is part of its own definition, not a forward reference, so it must exist.
            string baseName = data.GetProperty("base").GetString()!;
            MemoryTypeDefinition storage = this.Types[this.ImportBase(baseName)];
            if (storage.Kind != MemoryTypeKind.Scalar)
            {
                throw new CStructLayoutException($"ISF enum '{name}' has base type '{baseName}', which is not a defined scalar.");
            }

            var constants = new List<(string Name, System.Numerics.BigInteger Value)>();
            foreach (JsonProperty constant in data.GetProperty("constants").EnumerateObject())
            {
                if (constant.Name.Length == 0 || constant.Name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                {
                    throw new CStructLayoutException("ISF enum member cannot be represented as a Portable identifier.");
                }

                // Parsing the raw token as an integer rejects fractional or exponential constants instead of rounding.
                constants.Add((constant.Name, System.Numerics.BigInteger.Parse(constant.Value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)));
            }

            int size = data.GetProperty("size").GetInt32();
            string codec = storage.ScalarType!;
            if (size is 1 or 2 or 4 or 8 && (codec.StartsWith("int", StringComparison.Ordinal) || codec.StartsWith("uint", StringComparison.Ordinal)))
            {
                bool signed = codec.StartsWith("int", StringComparison.Ordinal) || constants.Any(constant => constant.Value.Sign < 0);
                codec = (signed ? "int" : "uint") + (size * 8).ToString(CultureInfo.InvariantCulture);
            }

            string enumName = "__isf_enum_" + this.nextId++;
            var source = new StringBuilder($"enum {enumName} : {codec} {{");
            foreach ((string constantName, System.Numerics.BigInteger value) in constants)
            {
                source.Append(constantName).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
            }

            source.Append("};");
            this.Add(id, new(id, name, MemoryTypeKind.Scalar, size, scalarType: enumName, declaration: source.ToString(), provenance: id, isLittleEndian: storage.IsLittleEndian));
        }

        /// <summary>Adds a descriptor within the descriptor budget.</summary>
        /// <param name="id">Descriptor ID.</param>
        /// <param name="type">Descriptor to add.</param>
        /// <exception cref="CStructLayoutException">The budget is exceeded.</exception>
        private void Add(string id, MemoryTypeDefinition type)
        {
            if (this.Types.Count >= this.maxTypes)
            {
                throw new CStructLayoutException("ISF type budget exceeded.");
            }

            this.Types.Add(id, type);
        }

        /// <summary>Records a name the document references but does not define as an address-only type.</summary>
        /// <param name="id">The ID the reference chose for the name.</param>
        /// <param name="name">The missing name.</param>
        /// <param name="table">What the name was expected to be, for the diagnostic: <c>user type</c>, <c>enum</c> or <c>base type</c>.</param>
        private void AddUndefined(string id, string name, string table)
        {
            this.Add(id, new(id, name, MemoryTypeKind.Incomplete, 0, provenance: id));
            this.Diagnostics.Add($"{id}: {table} '{name}' is referenced but not defined (a forward declaration); address-only use is supported.");
        }

        /// <summary>Reads the size, byte order and signedness of a bitfield's storage type, looking through an enum to its base type.</summary>
        /// <param name="descriptor">The storage type descriptor of a bitfield.</param>
        /// <param name="size">The storage size in bytes.</param>
        /// <param name="littleEndian">Whether the storage is little-endian.</param>
        /// <param name="signed">Whether the storage base type is signed.</param>
        /// <param name="isBool">Whether the storage is a base type of kind <c>bool</c>, rather than an integer or an enum.</param>
        /// <returns>
        ///     False when the storage is not a defined base type or an enum over one; the storage then imports as
        ///     whatever its descriptor names, and schema validation reports it.
        /// </returns>
        private bool TryGetStorage(JsonElement descriptor, out int size, out bool littleEndian, out bool signed, out bool isBool)
        {
            (size, littleEndian, signed, isBool) = (0, false, false, false);
            string kind = descriptor.GetProperty("kind").GetString()!;
            string name = descriptor.GetProperty("name").GetString()!;
            if (kind == "enum")
            {
                if (!this.document.enums.Value.TryGetValue(name, out JsonElement data))
                {
                    return false;
                }

                name = data.GetProperty("base").GetString()!;
            }

            if (kind is not ("enum" or "base") || !this.document.baseTypes.Value.TryGetValue(name, out JsonElement type) ||
                !type.TryGetProperty("endian", out JsonElement endian))
            {
                return false;
            }

            size = type.GetProperty("size").GetInt32();
            littleEndian = endian.ValueEquals("little");
            signed = type.GetProperty("signed").GetBoolean();
            isBool = kind == "base" && type.GetProperty("kind").ValueEquals("bool");
            return true;
        }

        /// <summary>Gets the ID of an unsigned integer storage type for bitfields, adding it on first use.</summary>
        /// <param name="size">The integer's size in bytes: 1, 2, 4 or 8.</param>
        /// <param name="littleEndian">The integer's byte order.</param>
        /// <returns>The storage type's ID.</returns>
        private string UnsignedStorage(int size, bool littleEndian)
        {
            string scalar = "uint" + (size * 8).ToString(CultureInfo.InvariantCulture);
            string id = "isf:bitfield-storage:" + scalar + (littleEndian ? ":little" : ":big");
            if (!this.Types.ContainsKey(id))
            {
                this.Add(id, new(id, scalar, MemoryTypeKind.Scalar, size, scalarType: scalar, provenance: "ISF 6.2.0 bitfield storage", isLittleEndian: littleEndian));
            }

            return id;
        }

        /// <summary>One step of the walk.</summary>
        /// <param name="Kind">What the step does.</param>
        /// <param name="Id">The ID of the type the step concerns.</param>
        /// <param name="Descriptor">The descriptor, for <see cref="StepKind.Descriptor"/> and <see cref="StepKind.FinishReference"/>.</param>
        /// <param name="Composite">The user type in progress, for <see cref="StepKind.ResumeComposite"/>.</param>
        /// <param name="Text">The root's name for <see cref="StepKind.UserType"/>; the subtype's ID for <see cref="StepKind.FinishReference"/>.</param>
        private readonly record struct Step(StepKind Kind, string Id, JsonElement Descriptor, CompositeImport? Composite, string? Text);

        /// <summary>A user type import in progress: its fields so far, and the next one to follow.</summary>
        /// <param name="id">The user type's ID.</param>
        /// <param name="name">The name in <c>user_types</c>.</param>
        /// <param name="kind">Struct or union.</param>
        /// <param name="size">The declared size in bytes.</param>
        /// <param name="members">The <c>fields</c> entries in document order.</param>
        private sealed class CompositeImport(string id, string name, MemoryTypeKind kind, int size, JsonProperty[] members)
        {
            /// <summary>Gets the user type's ID.</summary>
            public string Id { get; } = id;

            /// <summary>Gets the name in <c>user_types</c>.</summary>
            public string Name { get; } = name;

            /// <summary>Gets whether it is a struct or a union.</summary>
            public MemoryTypeKind Kind { get; } = kind;

            /// <summary>Gets the declared size in bytes.</summary>
            public int Size { get; } = size;

            /// <summary>Gets the <c>fields</c> entries in document order.</summary>
            public JsonProperty[] Members { get; } = members;

            /// <summary>Gets the fields followed so far.</summary>
            public List<MemoryField> Fields { get; } = [];

            /// <summary>Gets or sets the index of the next entry of <see cref="Members"/>.</summary>
            public int Next { get; set; }
        }
    }
}
