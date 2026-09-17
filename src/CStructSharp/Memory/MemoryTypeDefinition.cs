namespace CStructSharp.Memory;

/// <summary>An immutable description of one type: its identity, display name, kind, explicit byte size, and kind-specific details.</summary>
/// <remarks>
/// <para>
/// Definitions are the nodes of a <see cref="MemorySchema"/> graph, connected by IDs. <see cref="Id"/> is the key
/// that fields, arrays, and pointers refer to; <see cref="Name"/> is only for display. The two are separate
/// because native metadata routinely contains distinct types with the same name (two unrelated <c>struct node</c>
/// definitions in different translation units), and an importer must be able to keep both.
/// </para>
/// <para>
/// <see cref="Size"/> is always a byte count and includes any padding the compiler inserted, which is how a memory
/// schema differs from a Portable declaration: the size is stated, not computed. The remaining members depend on
/// the kind. Composites list their immediate <see cref="Fields"/>; arrays and pointers name their element or
/// target through <see cref="ElementTypeId"/>, with <see cref="Count"/> giving an array's element count; scalars
/// name a core codec through <see cref="ScalarType"/> so the memory APIs never duplicate numeric decoding.
/// </para>
/// <para>
/// Constructing a definition snapshots its fields but does not validate references, because a referenced type may
/// not exist yet while an importer is still building the graph. <see cref="MemorySchema"/> validates everything
/// once all definitions are available.
/// </para>
/// </remarks>
public sealed class MemoryTypeDefinition
{
    /// <summary>Creates a definition, snapshotting its fields; graph-wide validation happens in <see cref="MemorySchema"/>.</summary>
    /// <param name="id">Stable identity that other definitions refer to.</param>
    /// <param name="name">Human-readable name; it need not be unique within the schema.</param>
    /// <param name="kind">Storage category that decides which of the remaining parameters apply.</param>
    /// <param name="size">Encoded byte extent, including padding; zero only for incomplete types.</param>
    /// <param name="fields">Immediate members of a struct or union, in metadata order.</param>
    /// <param name="elementTypeId">ID of an array's element type or a pointer's target type; null for an opaque pointer.</param>
    /// <param name="count">Number of elements in an array; this is an element count, not a byte length.</param>
    /// <param name="scalarType">Core codec spelling for a scalar, such as <c>"uint32"</c> or an enum declared in <paramref name="declaration"/>.</param>
    /// <param name="declaration">Portable declarations the scalar codec depends on, such as an enum definition.</param>
    /// <param name="provenance">Free-text origin of this definition, kept for inspection and diagnostics.</param>
    /// <param name="isLittleEndian">Byte order for this scalar, or null to inherit the schema default.</param>
    public MemoryTypeDefinition(string id, string name, MemoryTypeKind kind, int size, IEnumerable<MemoryField>? fields = null, string? elementTypeId = null, int count = 0, string? scalarType = null, string? declaration = null, string? provenance = null, bool? isLittleEndian = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        this.Id = id;
        this.Name = name;
        this.Kind = kind;
        this.Size = size;
        this.Fields = Array.AsReadOnly(fields?.ToArray() ?? []);
        this.ElementTypeId = elementTypeId;
        this.Count = count;
        this.ScalarType = scalarType;
        this.Declaration = declaration;
        this.Provenance = provenance;
        this.IsLittleEndian = isLittleEndian;
    }

    /// <summary>Gets the stable identity that fields, arrays, and pointers refer to.</summary>
    public string Id { get; }

    /// <summary>Gets the display name, which may be shared by several definitions.</summary>
    public string Name { get; }

    /// <summary>Gets the storage category.</summary>
    public MemoryTypeKind Kind { get; }

    /// <summary>Gets the encoded extent in bytes, including padding.</summary>
    public int Size { get; }

    /// <summary>Gets the immediate members of a struct or union in metadata order; empty for other kinds.</summary>
    public IReadOnlyList<MemoryField> Fields { get; }

    /// <summary>Gets the array element or pointer target ID; null for non-composites and for opaque pointers.</summary>
    public string? ElementTypeId { get; }

    /// <summary>Gets the number of elements in an array; zero for other kinds.</summary>
    public int Count { get; }

    /// <summary>Gets the core codec spelling for a scalar; null for other kinds.</summary>
    public string? ScalarType { get; }

    /// <summary>Gets Portable declarations the scalar codec depends on, such as an enum definition; null when none are needed.</summary>
    public string? Declaration { get; }

    /// <summary>Gets the origin of this definition as recorded by its importer, when available.</summary>
    public string? Provenance { get; }

    /// <summary>Gets the scalar's byte order, or null to inherit the schema default.</summary>
    public bool? IsLittleEndian { get; }
}
