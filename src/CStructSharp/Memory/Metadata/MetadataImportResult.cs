namespace CStructSharp.Memory.Metadata;

/// <summary>What an importer returns: the compiled schema of every type reachable from the chosen root, that root's ID, and notes about what was kept address-only.</summary>
/// <remarks>
/// An importer compiles a reachable graph starting at the type the caller named; it does not find an instance of
/// that type in a capture. Use <see cref="RootTypeId"/> directly in session calls instead of guessing the
/// importer's ID spelling (for example <c>btf:7</c> or <c>isf:user:task</c>). <see cref="Diagnostics"/> lists
/// supported but address-only situations, such as a pointer to a function or forward declaration, which do not
/// prevent importing the graph. An unsupported or inconsistent reachable value layout fails the import rather than
/// producing a guessed schema, unless <see cref="MetadataImportOptions.BestEffort"/> is set: then the type becomes a
/// same-sized <see cref="MemoryTypeKind.RawBytes"/> placeholder and <see cref="Diagnostics"/> names it.
/// </remarks>
/// <param name="Schema">Compiled schema of the reachable types with explicit placement.</param>
/// <param name="RootTypeId">ID of the requested root type within <paramref name="Schema"/>.</param>
/// <param name="Diagnostics">Importer notes: retained address-only types, and the placeholders a best-effort import made.</param>
public sealed record MetadataImportResult(MemorySchema Schema, string RootTypeId, IReadOnlyList<string> Diagnostics)
{
    /// <summary>Compiles an importer's definitions into a schema and appends the schema's own notes to the importer's.</summary>
    /// <remarks>This is the shared final step of every metadata import. Compilation validates the whole graph with
    /// the options' pointer width, descriptor budget, and best-effort mode; placeholders it makes in best-effort mode
    /// are listed after the importer's diagnostics.</remarks>
    /// <param name="definitions">The descriptors the importer built.</param>
    /// <param name="rootTypeId">ID of the requested root type among <paramref name="definitions"/>.</param>
    /// <param name="diagnostics">The importer's notes; the schema's diagnostics are appended to this list, which the result then exposes.</param>
    /// <param name="isLittleEndian">Byte order for scalars that do not specify their own.</param>
    /// <param name="options">Options already checked by <see cref="MetadataImportOptions.ValidateOrDefault"/>.</param>
    /// <param name="cancellationToken">Checked while compiling the schema.</param>
    /// <returns>The import result.</returns>
    /// <exception cref="CStructSharp.Diagnostics.CStructLayoutException">The definitions do not form a valid schema.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    internal static MetadataImportResult Compile(IEnumerable<MemoryTypeDefinition> definitions, string rootTypeId, List<string> diagnostics, bool isLittleEndian, MetadataImportOptions options, CancellationToken cancellationToken)
    {
        var schema = new MemorySchema(definitions, isLittleEndian, maxTypes: options.MaxTypes, pointerSize: options.PointerSize, cancellationToken: cancellationToken, bestEffort: options.BestEffort);
        diagnostics.AddRange(schema.Diagnostics);
        return new MetadataImportResult(schema, rootTypeId, diagnostics.AsReadOnly());
    }
}
