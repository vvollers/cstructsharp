namespace CStructSharp.Memory.Metadata;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>Imports the value-type subset of Volatility ISF 6.2.0 JSON into a <see cref="MemorySchema"/>.</summary>
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
/// This importer consumes the first three tables for one root user type and everything it references. It keeps
/// the explicit byte order of base types, takes the target pointer width as a caller setting (a 32-bit profile
/// analyzed on a 64-bit host must say so), and reserves each composite's identity before following its fields so
/// recursive pointers terminate. Symbols are not evaluated, relocations are not applied, no file is opened, and
/// the byte length, type count, and nesting depth of the input are all bounded.
/// </para>
/// </remarks>
public static class IsfMetadata
{
    /// <summary>Imports one named user type and its reachable dependencies from a UTF-8 ISF 6.2.0 document.</summary>
    /// <remarks>The result owns compiled descriptors, not the parsed JSON. Use its <see cref="MetadataImportResult.RootTypeId"/>
    /// in session operations and inspect its diagnostics for retained address-only types. Choosing a profile that
    /// matches a capture and locating a record are the caller's responsibilities; a valid document establishes
    /// neither.</remarks>
    /// <param name="json">UTF-8 ISF document supplied by the caller.</param>
    /// <param name="rootName">Name in <c>user_types</c> to import together with its reachable dependencies.</param>
    /// <param name="pointerSize">Target pointer width in bytes, describing the analyzed image rather than the host.</param>
    /// <param name="isLittleEndian">Default byte order for the schema; base types with an explicit order override it.</param>
    /// <param name="maxBytes">Maximum accepted document length in bytes.</param>
    /// <param name="maxTypes">Maximum number of descriptors the import may create.</param>
    /// <param name="cancellationToken">Checked while descending the type graph and while compiling the schema.</param>
    /// <returns>The compiled reachable schema, the root's ID, and diagnostics.</returns>
    public static MetadataImportResult Import(ReadOnlyMemory<byte> json, string rootName, int pointerSize = 8, bool isLittleEndian = true, int maxBytes = 16 * 1024 * 1024, int maxTypes = 100_000, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        _ = new StoredPointer(0, pointerSize);
        if (maxBytes <= 0 || maxTypes <= 0 || json.Length > maxBytes)
        {
            throw new ArgumentException("ISF metadata exceeds its byte/type budget.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128, });
        JsonElement root = document.RootElement;
        if (root.GetProperty("metadata").GetProperty("format").GetString() != "6.2.0")
        {
            throw new ArgumentException("Only ISF format 6.2.0 is supported.");
        }

        var importer = new Importer(root, pointerSize, maxTypes, cancellationToken);
        string id = importer.Named(rootName, false, 0);
        return new MetadataImportResult(new MemorySchema(importer.Types.Values, isLittleEndian, pointerSize: pointerSize, cancellationToken: cancellationToken), id, importer.Diagnostics.AsReadOnly());
    }

    /// <summary>The mutable state of one import: the descriptors built so far, generated-ID counter, and diagnostics. Discarded after import.</summary>
    private sealed class Importer
    {
        private readonly JsonElement root;
        private readonly int pointerSize;
        private readonly CancellationToken cancellationToken;
        private readonly int maxTypes;
        private int nextId;

        /// <summary>Creates an importer over one parsed document.</summary>
        /// <param name="root">Root element of the ISF document.</param>
        /// <param name="pointerSize">Target pointer width in bytes.</param>
        /// <param name="maxTypes">Maximum number of descriptors to create.</param>
        /// <param name="cancellationToken">Checked at each descent.</param>
        internal Importer(JsonElement root, int pointerSize, int maxTypes, CancellationToken cancellationToken)
        {
            this.root = root;
            this.pointerSize = pointerSize;
            this.cancellationToken = cancellationToken;
            this.maxTypes = maxTypes;
        }

        /// <summary>Gets the descriptors created so far, keyed by generated ID.</summary>
        internal Dictionary<string, MemoryTypeDefinition> Types { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets notes about address-only types retained during import.</summary>
        internal List<string> Diagnostics { get; } = new();

        /// <summary>Imports a base type or user type by name, returning its ID; composites reserve their ID before their fields are followed.</summary>
        /// <remarks>Base types map directly to core codec spellings such as <c>uint32</c> or <c>float64</c>. A user
        /// type is first added as an empty struct or union, so a field that points back to it (a linked-list node)
        /// finds the ID and stops; the descriptor is replaced with the full field list afterwards. Only by-value
        /// recursion is invalid, and the completed <see cref="MemorySchema"/> checks for that.</remarks>
        /// <param name="name">Type name in <c>base_types</c> or <c>user_types</c>.</param>
        /// <param name="isBase">Whether to look in <c>base_types</c> rather than <c>user_types</c>.</param>
        /// <param name="depth">Descent depth, bounded to protect the call stack.</param>
        /// <returns>The generated descriptor ID.</returns>
        internal string Named(string name, bool isBase, int depth)
        {
            this.Check(depth);
            string id = (isBase ? "isf:base:" : "isf:user:") + name;
            if (this.Types.ContainsKey(id))
            {
                return id;
            }

            JsonElement type = this.root.GetProperty(isBase ? "base_types" : "user_types").GetProperty(name);
            int size = type.GetProperty("size").GetInt32();
            string kind = type.GetProperty("kind").GetString()!;
            if (isBase)
            {
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

                // Map the ISF description to the core codec of the same size and signedness.
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

            MemoryTypeKind compositeKind = kind switch
            {
                "struct" or "class" => MemoryTypeKind.Struct,
                "union" => MemoryTypeKind.Union,
                _ => throw new ArgumentException($"Unsupported ISF user type kind '{kind}'."),
            };

            // Reserve the identity before descending so recursive pointers can refer to it.
            this.Add(id, new(id, name, compositeKind, size, provenance: id));
            var fields = new List<MemoryField>();
            foreach (JsonProperty property in type.GetProperty("fields").EnumerateObject())
            {
                this.Check(depth);
                JsonElement field = property.Value;
                JsonElement descriptor = field.GetProperty("type");
                int offset = field.GetProperty("offset").GetInt32();
                bool promoted = field.TryGetProperty("anonymous", out JsonElement anonymous) && anonymous.GetBoolean();
                if (descriptor.GetProperty("kind").GetString() == "bitfield")
                {
                    // A bitfield descriptor wraps its storage type and adds a bit position and length within it.
                    string storageId = this.Descriptor(descriptor.GetProperty("type"), depth + 1);
                    MemoryTypeDefinition storage = this.Types[storageId];
                    int bit = descriptor.GetProperty("bit_position").GetInt32();
                    int width = descriptor.GetProperty("bit_length").GetInt32();
                    bool signed = this.IsSigned(descriptor.GetProperty("type"));
                    fields.Add(new(property.Name, storageId, offset, bit, width, signed));
                }
                else
                {
                    fields.Add(new(property.Name, this.Descriptor(descriptor, depth + 1), offset, promoted: promoted));
                }
            }

            this.Types[id] = new(id, name, compositeKind, size, fields, provenance: id);
            return id;
        }

        /// <summary>Imports the type named by a field's descriptor: a reference by name, or a generated pointer, array, or function type.</summary>
        /// <param name="descriptor">The descriptor object with a <c>kind</c> property.</param>
        /// <param name="depth">Descent depth, bounded to protect the call stack.</param>
        /// <returns>The ID of the imported type.</returns>
        private string Descriptor(JsonElement descriptor, int depth)
        {
            this.Check(depth);
            string kind = descriptor.GetProperty("kind").GetString()!;
            if (kind == "base")
            {
                return this.Named(descriptor.GetProperty("name").GetString()!, true, depth + 1);
            }

            if (kind is "struct" or "class" or "union")
            {
                return this.Named(descriptor.GetProperty("name").GetString()!, false, depth + 1);
            }

            if (kind == "enum")
            {
                return this.Enum(descriptor.GetProperty("name").GetString()!, depth + 1);
            }

            // Pointers, arrays, and functions have no name of their own, so they get generated IDs.
            string id = "isf:generated:" + this.nextId++;
            if (kind is "pointer" or "array")
            {
                string element = this.Descriptor(descriptor.GetProperty("subtype"), depth + 1);
                int count = kind == "array" ? descriptor.GetProperty("count").GetInt32() : 0;
                int size = kind == "array" ? checked(count * this.Types[element].Size) : this.pointerSize;

                // A pointer may name its own storage base type; it must agree with the supplied pointer width.
                if (descriptor.TryGetProperty("base", out JsonElement pointerBase))
                {
                    string baseId = this.Named(pointerBase.GetString()!, true, depth + 1);
                    if (this.Types[baseId].Size != this.pointerSize)
                    {
                        throw new ArgumentException("ISF pointer base differs from the supplied pointer width.");
                    }
                }

                this.Add(id, new(id, string.Empty, kind == "array" ? MemoryTypeKind.Array : MemoryTypeKind.Pointer, size, elementTypeId: element, count: count, provenance: "ISF 6.2.0 " + kind));
                return id;
            }

            if (kind == "function")
            {
                this.Add(id, new(id, "function", MemoryTypeKind.Incomplete, 0));
                this.Diagnostics.Add($"{id}: function type is address-only.");
                return id;
            }

            throw new ArgumentException($"Unsupported ISF descriptor '{kind}'.");
        }

        /// <summary>Imports an enum as a scalar whose codec is a generated Portable enum declaration over its base type.</summary>
        /// <param name="name">Enum name in the <c>enums</c> table.</param>
        /// <param name="depth">Descent depth, bounded to protect the call stack.</param>
        /// <returns>The enum descriptor's ID.</returns>
        private string Enum(string name, int depth)
        {
            string id = "isf:enum:" + name;
            if (this.Types.ContainsKey(id))
            {
                return id;
            }

            JsonElement data = this.root.GetProperty("enums").GetProperty(name);
            string baseId = this.Named(data.GetProperty("base").GetString()!, true, depth + 1);
            MemoryTypeDefinition storage = this.Types[baseId];
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
            return id;
        }

        /// <summary>Checks cancellation and the nesting limit before descending further.</summary>
        /// <param name="depth">Current descent depth.</param>
        private void Check(int depth)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            if (depth > 128)
            {
                throw new ArgumentException("ISF type/depth budget exceeded.");
            }
        }

        /// <summary>Adds a descriptor, enforcing the type-count limit on generated and named types alike.</summary>
        /// <param name="id">Descriptor ID.</param>
        /// <param name="type">Descriptor to add.</param>
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
    }
}
