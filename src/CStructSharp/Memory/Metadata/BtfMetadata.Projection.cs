namespace CStructSharp.Memory.Metadata;

using System.Globalization;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>Imports field-directed BTF schemas while retaining native IDs and explicit projection availability.</summary>
public sealed partial class BtfMetadata
{
    /// <summary>Imports the union of selected paths, following embedded values and only explicitly requested pointer targets.</summary>
    /// <remarks>
    /// The root's recorded size and selected placements are unchanged. Terminal composites include all their by-value
    /// fields; pointer targets not otherwise needed remain identity-only definitions with IsUnselected set. Partial
    /// composites expose their omitted immediate names. This validates selected representations, direct names and
    /// promotion candidates, not the full reachable graph. The parsed BTF table still undergoes its normal binary
    /// validation. Use Import for general inspection. Results are immutable and share only bounded successful scalar
    /// compilation with other imports from this table.
    /// </remarks>
    /// <param name="rootTypeId">Declared BTF identity, including a typedef or qualifier identity.</param>
    /// <param name="selectedPaths">Relative session paths; an empty path includes the root by value.</param>
    /// <param name="options">Pointer width, validation mode, descriptor limit and planning-work budget.</param>
    /// <param name="cancellationToken">Observed during planning, import and compilation.</param>
    /// <returns>The projected schema and the original declared root identity.</returns>
    /// <exception cref="ArgumentException">Paths are empty or options are invalid.</exception>
    /// <exception cref="ArgumentNullException">The paths or a path entry is null.</exception>
    /// <exception cref="CStructPathException">A selected path is absent, ambiguous or not traversable.</exception>
    /// <exception cref="CStructLayoutException">Selected metadata is invalid or exceeds a budget.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public MetadataImportResult ImportProjection(uint rootTypeId, IEnumerable<string> selectedPaths, MetadataImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths);
        options = MetadataImportOptions.ValidateOrDefault(options);
        var plan = new MetadataProjectionPlan(Id(rootTypeId), selectedPaths, this.DescribeProjection, options.MaxTypes, options.MaxProjectionWork, cancellationToken);
        var importer = new BtfImporter(this, options, plan);
        importer.Import(rootTypeId, cancellationToken);
        CStructLayoutCache cache = LazyInitializer.EnsureInitialized(ref this.layoutCache, static () => new CStructLayoutCache(256, 1024 * 1024));
        return MetadataImportResult.Compile(importer.Definitions.Values, Id(rootTypeId), importer.Diagnostics, this.IsLittleEndian, options, cancellationToken, cache) with { SelectedPaths = plan.Paths, };
    }

    /// <summary>Reads direct names and native references without resolving omitted ordinary member types.</summary>
    /// <param name="id">Declared BTF identity in schema spelling.</param>
    /// <param name="charge">Checks cancellation and charges each native member examined.</param>
    /// <returns>The planner's shallow type shape.</returns>
    private MetadataProjectionPlan.Node DescribeProjection(string id, Action charge)
    {
        uint native = uint.Parse(id.AsSpan(4), CultureInfo.InvariantCulture);
        BtfType type = this.Resolve(native);
        if (type.Kind is BtfKind.Struct or BtfKind.Union)
        {
            var fields = new List<MetadataProjectionPlan.Member>();
            var references = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < type.Payload.Length; index += 3)
            {
                charge();
                string name = type.Owner.String(type.Payload[index]);
                bool anonymous = name.Length == 0;
                bool promoted = anonymous && this.ResolveMemberPlacement(type, index).BitWidth is null;
                name = anonymous ? "__anonymous_" + (index / 3) : name;
                if (!references.TryAdd(name, Id(type.Payload[index + 1])))
                {
                    throw new CStructLayoutException("Duplicate BTF projected member name.");
                }

                fields.Add(new(name, promoted));
            }

            // Member identities stay lazy so malformed unselected dependencies remain outside validation.
            return new(type.Kind == BtfKind.Struct ? MemoryTypeKind.Struct : MemoryTypeKind.Union, fields, name => references[name]);
        }

        return type.Kind switch
        {
            BtfKind.Ptr => new(MemoryTypeKind.Pointer, [], Element: type.Size == 0 ? null : Id(type.Size)),
            BtfKind.Array => new(MemoryTypeKind.Array, [], Element: Id(type.Payload[0]), Count: ToInt32(type.Payload[2])),
            BtfKind.Int or BtfKind.Float or BtfKind.Enum or BtfKind.Enum64 => new(MemoryTypeKind.Scalar, []),
            _ when type.IsIncomplete => new(MemoryTypeKind.Incomplete, []),
            _ => throw new CStructLayoutException($"BTF kind {(int)type.Kind} is not a selected value type."),
        };
    }
}
