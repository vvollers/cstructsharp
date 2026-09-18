namespace CStructSharp.Memory;

using CStructSharp.Introspection;

/// <summary>Converts a compiled fixed-size Portable layout into a <see cref="MemorySchema"/>, using the offsets the compiler already computed.</summary>
/// <remarks>
/// <para>
/// When your own <c>CStruct</c> declaration is the authority for a record's layout, there is no metadata to import:
/// the compiled layout already knows every size, offset, pointer width, and codec. This adapter reads those facts
/// through the layout's public introspection and emits equivalent descriptors, so the memory APIs (unsigned
/// addresses, mappings, explicit pointer resolution) become available for records that were declared in the
/// Portable language. The declaration is not parsed again, and no host ABI is consulted.
/// </para>
/// <para>
/// Only fixed layouts can be projected. A memory schema must know every size up front, so conditional and
/// runtime-sized members are rejected with an explanation; such layouts are read through a finite region's stream
/// view with the ordinary core API instead. Compilation options that affect codecs (custom codecs, <c>long</c>
/// width, enum storage, bitfield allocation) are carried across so the projected scalars decode identically.
/// </para>
/// <para>
/// Recursive types such as <c>struct Node { Node *next; }</c> are handled by reserving a type's identity before its
/// members are imported, so a pointer back to the type finds an existing ID rather than recursing forever.
/// </para>
/// </remarks>
public static class PortableMemorySchema
{
    /// <summary>Projects a fixed exported root type and every type it references into a new schema.</summary>
    /// <remarks>The returned schema can be reused across sources and sessions. Sizes and offsets come from the
    /// layout's introspection only; the adapter never uses <c>Marshal.SizeOf</c> or the host pointer width to fill
    /// a gap, and a member without a fixed extent is a reason to reject the projection rather than to substitute
    /// zero. Runtime counts and data-sized arrays need a caller-selected finite region and the core stream API.</remarks>
    /// <param name="layout">Compiled Portable layout to project.</param>
    /// <param name="rootType">Name of the fixed-size exported type to start from.</param>
    /// <returns>A validated schema whose type IDs are the layout's own type names.</returns>
    public static MemorySchema Create(CStruct layout, string rootType)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootType);
        var builder = new Builder(layout);
        builder.Add(rootType, null, 0);
        builder.CompleteAliases();
        var options = new CStructCompilationOptions
        {
            Codecs = layout.CompilationOptions.Codecs,
            CLongWidth = layout.CompilationOptions.CLongWidth,
            DefaultEnumStorage = layout.CompilationOptions.DefaultEnumStorage,
            BitfieldAllocation = layout.CompilationOptions.BitfieldAllocation,
            BitfieldPacking = layout.CompilationOptions.BitfieldPacking,
            MaxDefinitionLength = layout.CompilationOptions.MaxDefinitionLength,
        };
        return new MemorySchema(builder.Types.Values, layout.IsLittleEndian, options, pointerSize: layout.PointerSize);
    }

    /// <summary>Accumulates descriptors for the reachable types of one layout, generating IDs for anonymous pointer, array, and inline types.</summary>
    private sealed class Builder
    {
        private readonly CStruct layout;
        private readonly Dictionary<string, LayoutDeclarationInfo> declarations;
        private readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        private int generated;

        /// <summary>Indexes the layout's exported declarations by name once.</summary>
        /// <param name="layout">Compiled layout being projected.</param>
        internal Builder(CStruct layout)
        {
            this.layout = layout;

            // Names are unique in compiled Portable layouts.
            this.declarations = layout.Layout.Declarations.ToDictionary(declaration => declaration.Name, StringComparer.Ordinal);
        }

        /// <summary>Gets the descriptors produced so far, keyed by ID.</summary>
        internal Dictionary<string, MemoryTypeDefinition> Types { get; } = new(StringComparer.Ordinal);

        /// <summary>Re-copies every typedef from its final target once all recursive members have been filled in.</summary>
        /// <remarks>While importing recursively, a typedef may have copied a composite that was still a reserved
        /// placeholder with no fields. Refreshing after construction ensures the alias reflects the completed
        /// member list instead of permanently retaining the provisional empty one.</remarks>
        internal void CompleteAliases()
        {
            foreach (string id in this.aliases.Keys)
            {
                this.CompleteAlias(id, 0);
            }
        }

        /// <summary>Refreshes one alias after refreshing whatever it points to, so chains of typedefs resolve in the right order.</summary>
        /// <param name="id">Typedef ID to refresh.</param>
        /// <param name="depth">Chain depth, bounded to reject cyclic alias graphs.</param>
        private void CompleteAlias(string id, int depth)
        {
            if (!this.aliases.TryGetValue(id, out string? targetId))
            {
                return;
            }

            if (depth > 128)
            {
                throw new ArgumentException("Alias graph exceeds its depth limit.");
            }

            this.CompleteAlias(targetId, depth + 1);
            MemoryTypeDefinition target = this.Types[targetId];
            this.Types[id] = new(id, id, target.Kind, target.Size, target.Fields, target.ElementTypeId, target.Count, target.ScalarType, target.Declaration, "Portable typedef", target.IsLittleEndian);
        }

        /// <summary>Imports a named type: a declared struct, union, or typedef, or else a scalar whose size is known or probed.</summary>
        /// <remarks>Composites reserve their ID with an empty descriptor before their fields are imported, so a
        /// field that points back to the composite finds the ID and stops. A scalar's size is taken from the caller
        /// when the layout already reported it (an array element, say); otherwise a one-member probe struct is
        /// compiled so the core reports the codec's size instead of the adapter guessing a host width.</remarks>
        /// <param name="id">Type name in the layout, which becomes the descriptor ID.</param>
        /// <param name="knownSize">Size already reported by introspection, or null to probe.</param>
        /// <param name="depth">Import depth, bounded to protect the call stack.</param>
        internal void Add(string id, int? knownSize, int depth)
        {
            if (this.Types.ContainsKey(id))
            {
                return;
            }

            if (depth > 128)
            {
                throw new ArgumentException("Portable metadata exceeds the depth limit.");
            }

            if (this.declarations.TryGetValue(id, out LayoutDeclarationInfo? declaration))
            {
                if (declaration.Size is not int size)
                {
                    throw new ArgumentException($"Type '{id}' is runtime-sized; use a finite region with the core stream API.");
                }

                if (declaration.Kind is LayoutDeclarationKind.Struct or LayoutDeclarationKind.Union)
                {
                    // Reserve the identity first so recursive pointer members can refer to it, then fill in fields.
                    MemoryTypeKind kind = declaration.Kind == LayoutDeclarationKind.Struct ? MemoryTypeKind.Struct : MemoryTypeKind.Union;
                    this.Types.Add(id, new(id, id, kind, size));
                    this.Types[id] = new(id, id, kind, size, this.Fields(declaration.Fields, depth + 1), provenance: "Portable compiled layout");
                    return;
                }

                if (declaration.Kind == LayoutDeclarationKind.Typedef)
                {
                    // Reserve the final extent before resolving recursive pointer aliases.
                    this.Types.Add(id, new(id, id, MemoryTypeKind.Struct, size));
                    string underlying = declaration.UnderlyingType!;
                    if (declaration.PointerDepth > 0)
                    {
                        string pointerId = id + ":pointer";
                        this.AddPointer(pointerId, underlying, declaration.PointerDepth, depth + 1);
                        underlying = pointerId;
                    }
                    else
                    {
                        this.Add(underlying, null, depth + 1);
                    }

                    // Wrap the target in one array descriptor per dimension, innermost dimension first.
                    MemoryTypeDefinition target = this.Types[underlying];
                    for (int index = declaration.ArrayShape.Count - 1; index >= 0; index--)
                    {
                        string arrayId = "__alias_array_" + this.generated++;
                        int count = declaration.ArrayShape[index];
                        var array = new MemoryTypeDefinition(arrayId, arrayId, MemoryTypeKind.Array, checked(target.Size * count), elementTypeId: target.Id, count: count);
                        this.Types.Add(arrayId, array);
                        target = array;
                    }

                    this.Types[id] = new(id, id, target.Kind, target.Size, target.Fields, target.ElementTypeId, target.Count, target.ScalarType, target.Declaration, "Portable typedef", target.IsLittleEndian);
                    this.aliases.Add(id, target.Id);
                    return;
                }

                knownSize = size;
            }

            if (id == "void")
            {
                this.Types.Add(id, new(id, id, MemoryTypeKind.Incomplete, 0));
                return;
            }

            if (!knownSize.HasValue)
            {
                var probe = new CStruct(this.layout.ToDefinition() + $"\nstruct __probe {{ {id} value; }};", pointerSize: this.layout.PointerSize, isLittleEndian: this.layout.IsLittleEndian, compilationOptions: new CStructCompilationOptions { Codecs = this.layout.CompilationOptions.Codecs, CLongWidth = this.layout.CompilationOptions.CLongWidth, });
                knownSize = probe.GetStructSizeInBytes("__probe");
            }

            this.Types.Add(id, new(id, id, MemoryTypeKind.Scalar, knownSize.Value, scalarType: id, declaration: this.layout.ToDefinition(), provenance: "Portable compiled layout"));
        }

        /// <summary>Creates one pointer descriptor per indirection level; the innermost level targets the named type, or nothing for <c>void</c>.</summary>
        /// <param name="id">ID for the outermost pointer descriptor.</param>
        /// <param name="target">Name of the ultimately pointed-to type.</param>
        /// <param name="levels">Number of indirections, as in <c>int **</c> being two.</param>
        /// <param name="depth">Import depth, bounded to protect the call stack.</param>
        private void AddPointer(string id, string target, int levels, int depth)
        {
            string child = levels > 1 ? id + ":next" : target;
            this.Types.Add(id, new(id, id, MemoryTypeKind.Pointer, this.layout.PointerSize, elementTypeId: target == "void" && levels == 1 ? null : child));
            if (levels > 1)
            {
                this.AddPointer(child, target, levels - 1, depth + 1);
            }
            else if (target != "void")
            {
                this.Add(target, null, depth + 1);
            }
        }

        /// <summary>Converts a composite's introspected fields into descriptors; any field without a fixed offset and size is an error, never offset zero.</summary>
        /// <remarks>Anonymous inline composites become promoted union descriptors with generated IDs, preserving
        /// the exact extent the layout reported because promoted storage may contain overlapping views. Pointer
        /// fields get generated pointer descriptors; array dimensions wrap the element type from the innermost
        /// dimension outward. A high-bit-first bitfield allocation is converted to the schema's low-bit convention
        /// by mirroring the bit offset within the storage unit.</remarks>
        /// <param name="fields">Introspected fields of one struct or union.</param>
        /// <param name="depth">Import depth, bounded to protect the call stack.</param>
        /// <returns>Field descriptors in declaration order.</returns>
        private IReadOnlyList<MemoryField> Fields(IReadOnlyList<LayoutFieldInfo> fields, int depth)
        {
            var result = new List<MemoryField>();
            foreach (LayoutFieldInfo field in fields)
            {
                if (field.IsConditional || field.Offset is not int offset || field.Size is not int size)
                {
                    throw new ArgumentException("Conditional or runtime-sized Portable fields require an explicit bounded core read.");
                }

                if (field.Name.Length == 0 && !field.IsAnonymous)
                {
                    continue;
                }

                string name = field.Name.Length == 0 ? "__promoted_" + this.generated++ : field.Name;
                string id = field.TypeName;
                if (field.IsAnonymous)
                {
                    id = "__inline_" + this.generated++;

                    // Promoted storage can contain overlapping union views; preserve the exact reported extent.
                    this.Types.Add(id, new(id, name, MemoryTypeKind.Union, size, this.Fields(field.PromotedFields, depth + 1)));
                }
                else if (field.PointerDepth > 0)
                {
                    id = "__pointer_" + this.generated++;
                    this.AddPointer(id, field.TypeName, field.PointerDepth, depth + 1);
                }
                else
                {
                    // The field size covers the whole array; divide by each dimension to reach the element size.
                    int? elementSize = size;
                    foreach (int? dimension in field.Dimensions)
                    {
                        if (dimension is not >= 0)
                        {
                            throw new ArgumentException("A fixed memory projection requires known nonnegative array dimensions.");
                        }

                        // An empty array still has a known element type; probe it rather than dividing by zero.
                        elementSize = dimension == 0 ? null : elementSize / dimension.Value;
                    }

                    this.Add(id, elementSize, depth + 1);
                }

                for (int i = field.Dimensions.Count - 1; i >= 0; i--)
                {
                    int count = field.Dimensions[i] ?? throw new ArgumentException("Unknown array count.");
                    string array = "__array_" + this.generated++;
                    this.Types.Add(array, new(array, array, MemoryTypeKind.Array, checked(this.Types[id].Size * count), elementTypeId: id, count: count));
                    id = array;
                }

                // The schema counts bit offsets from the low bit; a high-bit-first layout counts from the high bit.
                int? bitOffset = field.BitOffset;
                if (bitOffset.HasValue && this.layout.CompilationOptions.BitfieldAllocation == BitfieldAllocation.HighBitFirst)
                {
                    bitOffset = (size * 8) - bitOffset.Value - field.BitWidth!.Value;
                }

                result.Add(new(name, id, offset, bitOffset, field.BitWidth, promoted: field.IsAnonymous));
            }

            return result;
        }
    }
}
