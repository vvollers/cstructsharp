namespace CStructSharp.Memory.Metadata;

/// <summary>How <see cref="BtfMetadata.Import"/> and <see cref="IsfMetadata.Import"/> turn a type graph into a <see cref="MemorySchema"/>.</summary>
/// <remarks>
///     Parsing limits (the input's byte length, the size of a BTF type table) belong to the metadata object's
///     constructor; these options apply to one import from one root.
/// </remarks>
public sealed record MetadataImportOptions
{
    /// <summary>Creates the default options; set the properties to change them.</summary>
    public MetadataImportOptions()
    {
    }

    /// <summary>Gets the default options: 8-byte pointers, strict validation, at most 100,000 descriptors.</summary>
    public static MetadataImportOptions Default { get; } = new();

    /// <summary>
    ///     Gets the target pointer width in bytes. It describes the analyzed image, not the metadata's own word size or
    ///     the .NET process running the import, so a 32-bit capture analyzed on a 64-bit host must say 4.
    /// </summary>
    public int PointerSize { get; init; } = 8;

    /// <summary>
    ///     Gets whether a type whose recorded placement does not hold up (a member that does not fit its container, an
    ///     inconsistent bitfield) is demoted to a same-sized <see cref="MemoryTypeKind.RawBytes"/> placeholder and noted in
    ///     the result's diagnostics instead of failing the whole import. A torn or partial forensic capture is the case
    ///     it exists for; complete metadata rarely needs it.
    /// </summary>
    public bool BestEffort { get; init; }

    /// <summary>Gets the maximum number of type descriptors one import may create, bitfield storage and generated types included.</summary>
    public int MaxTypes { get; init; } = 100_000;

    /// <summary>Checks the options before an import starts.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The pointer width is not a supported width, or the descriptor budget is not positive.</exception>
    internal void Validate()
    {
        _ = new StoredPointer(0, this.PointerSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.MaxTypes, nameof(this.MaxTypes));
    }
}
