namespace CStructSharp.Memory.Metadata;

using System.Text.Json;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>Imports immutable field-directed ISF schemas without walking unrequested pointer graphs.</summary>
public sealed partial class IsfMetadata
{
    /// <summary>Imports the union of selected member/index/pointer paths, preserving the root size and recorded placement.</summary>
    /// <remarks>
    /// A terminal composite or array includes its by-value fields; pointers retain their stored address and target
    /// identity, but their target representation is imported only when required by a selected path or embedded value.
    /// Unselected target identities have IsUnselected set. Composite Fields contains only imported members and
    /// UnselectedMembers lists omitted immediate names. Only selected descriptors and their placement are validated;
    /// direct names and promotion markers are inspected to preserve ambiguity checks. Full Import remains appropriate
    /// for general inspection. Schemas never expand when another root is imported. Cancellation and budgets apply
    /// to both shallow planning and ordinary schema compilation; successful scalar compilation uses the document cache.
    /// </remarks>
    /// <param name="rootName">Exact user_types name.</param>
    /// <param name="selectedPaths">Relative session paths; an empty path selects the root's complete by-value shape.</param>
    /// <param name="options">Pointer width, validation mode, descriptor limit and planning-work budget.</param>
    /// <param name="cancellationToken">Observed during planning, import and compilation.</param>
    /// <returns>A narrower-validation schema with explicit projection availability and the requested paths.</returns>
    /// <exception cref="ArgumentException">The root name is empty, paths are empty, or options are invalid.</exception>
    /// <exception cref="ArgumentNullException">The paths or a path entry is null.</exception>
    /// <exception cref="CStructPathException">A selected path is absent, ambiguous or cannot traverse its type.</exception>
    /// <exception cref="CStructLayoutException">Selected metadata or a promotion shape is malformed or exceeds a budget.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public MetadataImportResult ImportProjection(string rootName, IEnumerable<string> selectedPaths, MetadataImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        ArgumentNullException.ThrowIfNull(selectedPaths);
        options = MetadataImportOptions.ValidateOrDefault(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!this.userTypes.Value.ContainsKey(rootName))
        {
            throw new CStructLayoutException($"ISF user type '{rootName}' is not in user_types.");
        }

        try
        {
            var source = new ProjectionSource(this);
            var plan = new MetadataProjectionPlan("isf:user:" + rootName, selectedPaths, source.Describe, options.MaxTypes, options.MaxProjectionWork, cancellationToken);
            var importer = new Importer(this, options, plan, source);
            string id = importer.Import(rootName, cancellationToken);
            CStructLayoutCache cache = LazyInitializer.EnsureInitialized(ref this.layoutCache, static () => new CStructLayoutCache(256, 1024 * 1024));
            return MetadataImportResult.Compile(importer.Types.Values, id, importer.Diagnostics, this.IsLittleEndian, options, cancellationToken, cache) with { SelectedPaths = plan.Paths, };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new CStructLayoutException("Invalid ISF projection metadata: " + exception.Message, exception);
        }
    }

    /// <summary>Rejects null names instead of conflating them with anonymous empty-name metadata identities.</summary>
    /// <param name="descriptor">Named type reference.</param>
    /// <returns>The recorded name.</returns>
    private static string ProjectionName(JsonElement descriptor)
        => descriptor.GetProperty("name").GetString() ?? throw new CStructLayoutException("ISF named descriptor has a null name.");

    /// <summary>Supplies lazy native shapes without examining the descriptors of omitted ordinary members.</summary>
    /// <param name="document">Parsed metadata retained for this operation only.</param>
    private sealed class ProjectionSource(IsfMetadata document)
    {
        private readonly Dictionary<string, JsonElement> descriptors = new(StringComparer.Ordinal);
        private readonly Dictionary<JsonElement, string> identities = [];

        /// <summary>Checks a selected array's own count before path traversal can misclassify malformed metadata as absence.</summary>
        /// <param name="descriptor">Selected array descriptor.</param>
        /// <returns>The nonnegative declared count.</returns>
        private static int ArrayCount(JsonElement descriptor)
        {
            int count = descriptor.GetProperty("count").GetInt32();
            return count >= 0 ? count : throw new CStructLayoutException("ISF projected array has a negative count.");
        }

        /// <summary>Registers an anonymous descriptor or returns a named identity for lazy lookup.</summary>
        /// <param name="descriptor">Native descriptor.</param>
        /// <returns>Identity used by the shared planner.</returns>
        internal string Register(JsonElement descriptor)
        {
            string? kind = descriptor.GetProperty("kind").GetString();
            string? named = kind switch
            {
                "struct" or "class" or "union" => "isf:user:" + ProjectionName(descriptor),
                "base" => "isf:base:" + ProjectionName(descriptor),
                "enum" => "isf:enum:" + ProjectionName(descriptor),
                _ => null,
            };
            if (named is not null)
            {
                return named;
            }

            // JsonElement identity retains its document and position, rather than merging equal anonymous shapes.
            if (!this.identities.TryGetValue(descriptor, out string? id))
            {
                id = "isf:projection-descriptor:" + this.identities.Count;
                this.identities.Add(descriptor, id);
                this.descriptors.Add(id, descriptor);
            }

            return id;
        }

        /// <summary>Reads a type's direct names, array count or pointer identity, leaving ordinary member descriptors lazy.</summary>
        /// <param name="id">Native planner identity.</param>
        /// <param name="charge">Checks cancellation and charges each native member examined.</param>
        /// <returns>The shallow shape.</returns>
        internal MetadataProjectionPlan.Node Describe(string id, Action charge)
        {
            if (id.StartsWith("isf:user:", StringComparison.Ordinal))
            {
                if (!document.userTypes.Value.TryGetValue(id[9..], out JsonElement type))
                {
                    return new(MemoryTypeKind.Incomplete, []);
                }

                MemoryTypeKind kind = type.GetProperty("kind").GetString() switch
                {
                    "struct" or "class" => MemoryTypeKind.Struct,
                    "union" => MemoryTypeKind.Union,
                    _ => throw new CStructLayoutException("Unsupported ISF projected user type kind."),
                };
                JsonElement fields = type.GetProperty("fields");
                var members = new List<MetadataProjectionPlan.Member>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty field in fields.EnumerateObject())
                {
                    charge();
                    if (!names.Add(field.Name))
                    {
                        throw new CStructLayoutException("Duplicate ISF projected member name.");
                    }

                    bool promoted = field.Value.ValueKind == JsonValueKind.Object && field.Value.TryGetProperty("anonymous", out JsonElement anonymous) && anonymous.GetBoolean();
                    if (promoted && field.Value.GetProperty("type").GetProperty("kind").GetString() == "bitfield")
                    {
                        promoted = false;
                    }

                    members.Add(new(field.Name, promoted));
                }

                // Only selected or anonymously promoted members need their type descriptor examined.
                return new(kind, members, name => this.Register(fields.GetProperty(name).GetProperty("type")));
            }

            if (id.StartsWith("isf:base:", StringComparison.Ordinal))
            {
                return new(!document.baseTypes.Value.TryGetValue(id[9..], out JsonElement type) || type.GetProperty("kind").GetString() == "void" ? MemoryTypeKind.Incomplete : MemoryTypeKind.Scalar, []);
            }

            if (id.StartsWith("isf:enum:", StringComparison.Ordinal))
            {
                if (!document.enums.Value.TryGetValue(id[9..], out JsonElement enumeration))
                {
                    return new(MemoryTypeKind.Incomplete, []);
                }

                return new(MemoryTypeKind.Scalar, [], Dependencies: ["isf:base:" + enumeration.GetProperty("base").GetString()]);
            }

            JsonElement descriptor = this.descriptors[id];
            string[]? pointerBase = descriptor.TryGetProperty("base", out JsonElement storage) ? ["isf:base:" + storage.GetString()] : null;
            return descriptor.GetProperty("kind").GetString() switch
            {
                "pointer" => new(MemoryTypeKind.Pointer, [], Element: this.Register(descriptor.GetProperty("subtype")), Dependencies: pointerBase),
                "array" => new(MemoryTypeKind.Array, [], Element: this.Register(descriptor.GetProperty("subtype")), Count: ArrayCount(descriptor)),
                "bitfield" => new(MemoryTypeKind.Scalar, [], Dependencies: [this.Register(descriptor.GetProperty("type"))]),
                "function" => new(MemoryTypeKind.Incomplete, []),
                _ => throw new CStructLayoutException("Unsupported selected ISF descriptor kind."),
            };
        }
    }
}
