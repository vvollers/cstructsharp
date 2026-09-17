namespace CStructSharp.Memory.Metadata;

/// <summary>What an importer returns: the compiled schema of every type reachable from the chosen root, that root's ID, and notes about what was kept address-only.</summary>
/// <remarks>
/// An importer compiles a reachable graph starting at the type the caller named; it does not find an instance of
/// that type in a capture. Use <see cref="RootTypeId"/> directly in session calls instead of guessing the
/// importer's ID spelling (for example <c>btf:7</c> or <c>isf:user:task</c>). <see cref="Diagnostics"/> lists
/// supported but address-only situations, such as a pointer to a function or forward declaration, which do not
/// prevent importing the graph. Unsupported reachable value layouts fail the import rather than producing a
/// guessed schema.
/// </remarks>
/// <param name="Schema">Compiled schema of the reachable types with explicit placement.</param>
/// <param name="RootTypeId">ID of the requested root type within <paramref name="Schema"/>.</param>
/// <param name="Diagnostics">Importer notes about retained address-only types.</param>
public sealed record MetadataImportResult(MemorySchema Schema, string RootTypeId, IReadOnlyList<string> Diagnostics);
