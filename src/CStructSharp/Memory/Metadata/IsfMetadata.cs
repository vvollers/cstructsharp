namespace CStructSharp.Memory.Metadata;

using System.Globalization;
using System.Text;
using System.Text.Json;

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
/// composite's identity before following its fields so recursive pointers terminate. Symbols are not evaluated,
/// relocations are not applied, and no file is opened.
/// </para>
/// </remarks>
public sealed class IsfMetadata
{
    private readonly JsonElement root;

    /// <summary>Parses a UTF-8 ISF 6.2.0 document.</summary>
    /// <param name="json">The UTF-8 document.</param>
    /// <param name="isLittleEndian">Byte order of the imported schemas; base types with an explicit order override it.</param>
    /// <param name="maxBytes">Maximum accepted document length in bytes. JSON nesting is limited to 128 levels.</param>
    /// <param name="cancellationToken">Checked before parsing.</param>
    /// <exception cref="ArgumentException">The document exceeds its budget or is not ISF 6.2.0.</exception>
    /// <exception cref="JsonException">The document is not valid JSON.</exception>
    public IsfMetadata(ReadOnlyMemory<byte> json, bool isLittleEndian = true, int maxBytes = 16 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0 || json.Length > maxBytes)
        {
            throw new ArgumentException("ISF metadata exceeds its byte budget.", nameof(json));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128, });

        // A cloned element owns its data, so the document can be released now.
        this.root = document.RootElement.Clone();
        if (!this.root.TryGetProperty("metadata", out JsonElement metadata) || !metadata.TryGetProperty("format", out JsonElement format) ||
            format.GetString() != "6.2.0")
        {
            throw new ArgumentException("Only ISF format 6.2.0 is supported.", nameof(json));
        }

        this.IsLittleEndian = isLittleEndian;
    }

    /// <summary>Gets the byte order of the imported schemas; base types with an explicit order override it.</summary>
    public bool IsLittleEndian { get; }

    /// <summary>Imports one named user type and its reachable dependencies.</summary>
    /// <remarks>The result owns compiled descriptors, not the parsed JSON. Use its <see cref="MetadataImportResult.RootTypeId"/>
    /// in session operations and inspect its diagnostics for retained address-only types. Choosing a profile that
    /// matches a capture and locating a record are the caller's responsibilities; a valid document establishes
    /// neither.</remarks>
    /// <param name="rootName">Name in <c>user_types</c> to import together with its reachable dependencies.</param>
    /// <param name="options">The pointer width, validation mode and descriptor budget; <see cref="MetadataImportOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Checked at each step of the walk and while compiling the schema.</param>
    /// <returns>The compiled reachable schema, the root's ID, and diagnostics.</returns>
    /// <exception cref="ArgumentException">A reachable type is invalid or unsupported, or the descriptor budget is exceeded.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options are out of range.</exception>
    public MetadataImportResult Import(string rootName, MetadataImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        options ??= MetadataImportOptions.Default;
        options.Validate();
        var importer = new Importer(this.root, options);
        string id = importer.Import(rootName, cancellationToken);
        var schema = new MemorySchema(importer.Types.Values, this.IsLittleEndian, maxTypes: options.MaxTypes, pointerSize: options.PointerSize, cancellationToken: cancellationToken, bestEffort: options.BestEffort);
        importer.Diagnostics.AddRange(schema.Diagnostics);
        return new MetadataImportResult(schema, id, importer.Diagnostics.AsReadOnly());
    }

    /// <summary>The state of one import: the descriptors built so far, the generated-ID counter, and diagnostics.</summary>
    /// <remarks>
    /// The walk runs on <see cref="MetadataGraphWalk"/>, depth first, and numbers generated types (pointers, arrays,
    /// functions, enum codecs) in the order it meets them. A type reached by name gets its ID from the name, so it is
    /// imported once; a user type is reserved as an empty composite before its fields are followed, so a field that
    /// points back to it finds the ID and stops. Only by-value recursion is invalid, and <see cref="MemorySchema"/>
    /// checks for that.
    /// </remarks>
    private sealed class Importer
    {
        private readonly JsonElement root;
        private readonly int pointerSize;
        private readonly int maxTypes;
        private int nextId;

        /// <summary>Starts an import over a parsed document.</summary>
        /// <param name="root">Root element of the document.</param>
        /// <param name="options">Validated import options.</param>
        internal Importer(JsonElement root, MetadataImportOptions options)
        {
            this.root = root;
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
        internal string Import(string rootName, CancellationToken cancellationToken)
        {
            string id = UserId(rootName);
            MetadataGraphWalk.Run(new Step(StepKind.UserType, id, default, null, rootName), this.Run, cancellationToken);
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
                throw new ArgumentException($"Unsupported ISF descriptor '{kind}'.");
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

            JsonElement type = this.root.GetProperty("base_types").GetProperty(name);
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
                throw new ArgumentException("Invalid ISF byte order.");
            }

            bool signed = type.GetProperty("signed").GetBoolean();
            string scalar = kind switch
            {
                "int" or "char" => (signed ? "int" : "uint") + (size * 8),
                "float" => "float" + (size * 8),
                "bool" when size == 1 => "bool",
                _ => throw new ArgumentException($"Unsupported ISF base type '{kind}' of size {size}."),
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

            JsonElement type = this.root.GetProperty("user_types").GetProperty(name);
            int size = type.GetProperty("size").GetInt32();
            MemoryTypeKind kind = type.GetProperty("kind").GetString() switch
            {
                "struct" or "class" => MemoryTypeKind.Struct,
                "union" => MemoryTypeKind.Union,
                var other => throw new ArgumentException($"Unsupported ISF user type kind '{other}'."),
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
                composite.Fields.Add(new(property.Name, storageId, offset, bit, width, this.IsSigned(storage)));
                work.Push(new Step(StepKind.Descriptor, storageId, storage, null, null));
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
                throw new ArgumentException("ISF pointer base differs from the supplied pointer width.");
            }

            this.Add(id, new(id, string.Empty, isArray ? MemoryTypeKind.Array : MemoryTypeKind.Pointer, size, elementTypeId: element, count: count, provenance: "ISF 6.2.0 " + (isArray ? "array" : "pointer")));
        }

        /// <summary>Imports an enum as a scalar whose codec is a generated Portable enum declaration over its base type.</summary>
        /// <param name="id">The enum's ID.</param>
        /// <param name="name">Enum name in the <c>enums</c> table.</param>
        private void ImportEnum(string id, string name)
        {
            JsonElement data = this.root.GetProperty("enums").GetProperty(name);
            MemoryTypeDefinition storage = this.Types[this.ImportBase(data.GetProperty("base").GetString()!)];
            string enumName = "__isf_enum_" + this.nextId++;
            var source = new StringBuilder($"enum {enumName} : {storage.ScalarType} {{");
            foreach (JsonProperty constant in data.GetProperty("constants").EnumerateObject())
            {
                if (constant.Name.Length == 0 || constant.Name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                {
                    throw new ArgumentException("ISF enum member cannot be represented as a Portable identifier.");
                }

                // Parsing the raw token as an integer rejects fractional or exponential constants instead of rounding.
                System.Numerics.BigInteger value = System.Numerics.BigInteger.Parse(constant.Value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                source.Append(constant.Name).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
            }

            source.Append("};");
            this.Add(id, new(id, name, MemoryTypeKind.Scalar, data.GetProperty("size").GetInt32(), scalarType: enumName, declaration: source.ToString(), provenance: id, isLittleEndian: storage.IsLittleEndian));
        }

        /// <summary>Adds a descriptor within the descriptor budget.</summary>
        /// <param name="id">Descriptor ID.</param>
        /// <param name="type">Descriptor to add.</param>
        /// <exception cref="ArgumentException">The budget is exceeded.</exception>
        private void Add(string id, MemoryTypeDefinition type)
        {
            if (this.Types.Count >= this.maxTypes)
            {
                throw new ArgumentException("ISF type budget exceeded.");
            }

            this.Types.Add(id, type);
        }

        /// <summary>Reads the signedness of a bitfield's storage type, looking through an enum to its base type.</summary>
        /// <param name="descriptor">The storage type descriptor of a bitfield.</param>
        /// <returns>True when the storage base type is signed.</returns>
        private bool IsSigned(JsonElement descriptor)
        {
            string kind = descriptor.GetProperty("kind").GetString()!;
            string name = descriptor.GetProperty("name").GetString()!;
            if (kind == "enum")
            {
                name = this.root.GetProperty("enums").GetProperty(name).GetProperty("base").GetString()!;
            }

            return this.root.GetProperty("base_types").GetProperty(name).GetProperty("signed").GetBoolean();
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
