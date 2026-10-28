namespace CStructSharp.Memory;

using CStructSharp.Diagnostics;

/// <summary>An immutable schema-only selection plan, reusable across records and independent operation contexts.</summary>
/// <remarks>No source, bytes, pointer value, session, or context is retained. Offset is unknown after a pointer value step. Holding a plan retains its schema; applications choose their own bounded reader lifetime.</remarks>
public sealed class MemoryPath
{
    private readonly Step[] steps;

    /// <summary>Stores validated schema decisions for an explicit path.</summary>
    /// <param name="schema">Owning immutable schema.</param>
    /// <param name="root">Root type.</param>
    /// <param name="path">Relative path text.</param>
    /// <param name="steps">Prepared traversal decisions.</param>
    /// <param name="type">Selected type.</param>
    /// <param name="field">Selected member, if any.</param>
    /// <param name="parentTypeId">Selected member's parent identity.</param>
    /// <param name="offset">Constant root byte offset, or null after pointer traversal.</param>
    private MemoryPath(MemorySchema schema, MemoryTypeDefinition root, string path, Step[] steps, MemoryTypeDefinition type, MemoryField? field, string? parentTypeId, long? offset)
    {
        this.Schema = schema;
        this.RootType = root;
        this.Path = path;
        this.steps = steps;
        this.Type = type;
        this.Field = field;
        this.ParentTypeId = parentTypeId;
        this.Offset = offset;
    }

    /// <summary>Gets the schema instance owning this plan.</summary>
    public MemorySchema Schema { get; }

    /// <summary>Gets the root record's type.</summary>
    public MemoryTypeDefinition RootType { get; }

    /// <summary>Gets the original relative path.</summary>
    public string Path { get; }

    /// <summary>Gets the selected type without accessing memory.</summary>
    public MemoryTypeDefinition Type { get; }

    /// <summary>Gets the selected field, including bit slice, or null at roots and array elements.</summary>
    public MemoryField? Field { get; }

    /// <summary>Gets the selected field's immediate containing type ID, or null.</summary>
    public string? ParentTypeId { get; }

    /// <summary>Gets the byte offset from the root, or null when the path follows a stored pointer.</summary>
    public long? Offset { get; }

    /// <summary>Validates a fixed-offset scalar selection without allocating intermediate regions or selections.</summary>
    /// <param name="session">Session owning the identical schema.</param>
    /// <param name="region">Finite root storage.</param>
    /// <param name="context">Operation cancellation and depth limits.</param>
    /// <param name="failure">Root extent failure, or null.</param>
    /// <returns>Whether the root can hold the selected path.</returns>
    internal bool TryValidateFixed(MemorySession session, MemoryRegion region, MemoryAccessContext context, out MemoryReadFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(region);
        if (!ReferenceEquals(session.Schema, this.Schema))
        {
            throw new ArgumentException("A prepared path requires the identical schema instance.", nameof(session));
        }

        context.CheckNestingDepth(0);
        failure = null;
        if (region.Length < this.RootType.Size)
        {
            failure = this.WithContext(new(MemoryReadFailureKind.InsufficientExtent, "Root region is shorter than its type.", SourceId: region.Source.Id, Address: region.Address, Length: this.RootType.Size), region);
            return false;
        }

        ulong address = region.Address;
        for (int index = 0; index < this.steps.Length; index++)
        {
            Step step = this.steps[index];
            context.CheckNestingDepth(index + 1 + step.SearchDepth);
            _ = checked(address + (ulong)step.ValidationOffset);
            if (step.Fields is { } fields)
            {
                foreach (MemoryField field in fields)
                {
                    address = checked(address + (ulong)field.Offset);
                }
            }
            else
            {
                address = checked(address + (ulong)step.Offset);
            }
        }

        return true;
    }

    /// <summary>Applies this plan to a record, reading only intermediate pointer storage.</summary>
    /// <param name="session">Session with the identical schema instance and the desired pointer resolver.</param>
    /// <param name="region">Finite root record extent.</param>
    /// <param name="context">Operation-local budget and cancellation.</param>
    /// <returns>The final storage and containing-record metadata.</returns>
    /// <exception cref="ArgumentException">The session owns a different schema.</exception>
    /// <exception cref="CStructException">Selection or memory access failed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public MemorySelection Resolve(MemorySession session, MemoryRegion region, MemoryAccessContext? context = null)
        => this.TryResolve(session, region, out MemorySelection? selection, out MemoryReadFailure? failure, context) ? selection! : throw failure!.ToException();

    /// <summary>Applies prepared decisions without throwing for null pointers, incomplete storage, or unavailable memory.</summary>
    /// <param name="session">Session owning the identical schema.</param>
    /// <param name="region">Caller-owned root extent.</param>
    /// <param name="selection">Selected storage on success; null on failure.</param>
    /// <param name="failure">Structured failure on false; null on success.</param>
    /// <param name="context">Shared operation context; null creates one.</param>
    /// <returns>Whether the complete path resolved.</returns>
    /// <exception cref="ArgumentException">The schema instances differ.</exception>
    /// <exception cref="MemoryAccessException">A budget or depth limit was exhausted.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public bool TryResolve(MemorySession session, MemoryRegion region, out MemorySelection? selection, out MemoryReadFailure? failure, MemoryAccessContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(region);
        if (!ReferenceEquals(session.Schema, this.Schema))
        {
            throw new ArgumentException("A prepared path requires the identical schema instance.", nameof(session));
        }

        context ??= new MemoryAccessContext();
        context.CheckNestingDepth(0);
        selection = null;
        failure = null;
        if (region.Length < this.RootType.Size)
        {
            failure = this.WithContext(new(MemoryReadFailureKind.InsufficientExtent, "Root region is shorter than its type.", SourceId: region.Source.Id, Address: region.Address, Length: this.RootType.Size), region);
            return false;
        }

        var current = new MemorySelection(region.Slice(0, this.RootType.Size), this.RootType, null, null);
        MemoryRegion container = region;
        int nesting = 0;
        int pointers = 0;
        Span<byte> bits = stackalloc byte[8];
        try
        {
            for (int index = 0; index < this.steps.Length; index++)
            {
                Step step = this.steps[index];
                context.CheckNestingDepth(++nesting + step.SearchDepth);
                _ = checked(current.Region.Address + (ulong)step.ValidationOffset);
                if (step.Follow)
                {
                    context.CheckPointerDepth(++pointers);
                    context.Charge(current.Region.Source.Id, current.Region.Address, 0);
                    if (!current.Region.TryReadExactly(bits[..current.Type.Size], context, out failure))
                    {
                        failure = this.WithContext(failure!, region);
                        return false;
                    }

                    MemoryScalarCodec codec = this.Schema.GetCodec(current.Type);
                    ulong address = codec.DecodePointer(bits[..current.Type.Size]);
                    if (address == 0)
                    {
                        failure = this.WithContext(new(MemoryReadFailureKind.NullPointer, "Cannot follow a null pointer.", SourceId: current.Region.Source.Id, Address: current.Region.Address, Length: current.Type.Size), region);
                        return false;
                    }

                    context.Charge(current.Region.Source.Id, current.Region.Address, 0);
                    MemoryRegion resolved = session.ResolvePointer(new PointerRequest(new StoredPointer(address, current.Type.Size), current.Region, current.Container ?? container, step.Type.Id, step.Type.Size, this.Path, index + 1), context);
                    if (resolved.Length < step.Type.Size)
                    {
                        failure = this.WithContext(new(MemoryReadFailureKind.InsufficientExtent, "Resolved region is shorter than the target type.", SourceId: resolved.Source.Id, Address: resolved.Address, Length: step.Type.Size), region);
                        return false;
                    }

                    current = new MemorySelection(resolved.Slice(0, step.Type.Size), step.Type, null, null);
                    container = current.Region;
                    nesting = 0;
                }
                else if (step.Fields is { } fields)
                {
                    foreach (MemoryField field in fields)
                    {
                        container = current.Region;
                        MemoryTypeDefinition member = this.Schema.GetType(field.TypeId);
                        current = new MemorySelection(container.Slice(field.Offset, member.Size), member, field, current.Type.Id, container);
                    }
                }
                else if (!step.Address)
                {
                    current = new MemorySelection(current.Region.Slice(step.Offset, step.Type.Size), step.Type, null, null);
                    container = current.Region;
                }
            }
        }
        catch (MemoryAccessException exception)
        {
            failure = this.WithContext(MemoryReadFailure.FromAccess(exception), region);
            if (exception.Failure == MemoryFailure.BudgetExceeded)
            {
                _ = failure.ToException();
                throw;
            }

            return false;
        }

        selection = current;
        return true;
    }

    /// <summary>Compiles member, promotion, index and pointer decisions without reading any source.</summary>
    /// <param name="schema">Owning schema.</param>
    /// <param name="typeId">Requested root identity.</param>
    /// <param name="path">Relative path.</param>
    /// <param name="selection">Prepared plan on success.</param>
    /// <param name="failure">Failure describing absence, ambiguity or invalid traversal.</param>
    /// <returns>Whether the schema can select the path.</returns>
    internal static bool TryCreate(MemorySchema schema, string typeId, string path, out MemoryPath? selection, out MemoryReadFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        ArgumentNullException.ThrowIfNull(path);
        selection = null;
        failure = null;
        string fullPath = path.Length == 0 ? typeId : typeId + "." + path;
        if (!schema.Types.TryGetValue(typeId, out MemoryTypeDefinition? root))
        {
            failure = new(MemoryReadFailureKind.UnknownType, $"Unknown memory type '{typeId}'.", fullPath);
            return false;
        }

        if (root.IsUnselected)
        {
            failure = new(MemoryReadFailureKind.UnselectedType, "Type representation was not selected by this projection.", fullPath);
            return false;
        }

        MemorySession.PathStep[] tokens;
        try
        {
            tokens = MemorySession.Steps(path);
        }
        catch (CStructPathException exception)
        {
            failure = new(MemoryReadFailureKind.InvalidPath, exception.Message, fullPath);
            return false;
        }

        var steps = new List<Step>();
        MemoryTypeDefinition type = root;
        MemoryField? field = null;
        string? parent = null;
        long? offset = 0;
        for (int index = 0; index < tokens.Length; index++)
        {
            MemorySession.PathStep token = tokens[index];
            if (type.Kind == MemoryTypeKind.Pointer && token.Member == "address" && index == tokens.Length - 1)
            {
                steps.Add(new(type, 0, null, false, true, 0));
                continue;
            }

            if (type.Kind == MemoryTypeKind.Pointer && token.Member == "value" && type.ElementTypeId is { } targetId)
            {
                type = schema.GetType(targetId);
                if (type.Kind == MemoryTypeKind.Incomplete)
                {
                    failure = new(type.IsUnselected ? MemoryReadFailureKind.UnselectedType : MemoryReadFailureKind.IncompleteTarget, type.IsUnselected ? "Pointer target was not selected by this projection." : "Pointer target is incomplete.", fullPath);
                    return false;
                }

                steps.Add(new(type, 0, null, true, false, 0));
                offset = null;
                field = null;
                parent = null;
                continue;
            }

            if (type.Kind == MemoryTypeKind.Array && token.Member is null && token.Index < type.Count)
            {
                type = schema.GetType(type.ElementTypeId!);
                long elementOffset = checked((long)token.Index * type.Size);
                steps.Add(new(type, elementOffset, null, false, false, 0));
                offset += elementOffset;
                field = null;
                parent = null;
                continue;
            }

            if (type.Kind is MemoryTypeKind.Struct or MemoryTypeKind.Union && token.Member is { } name)
            {
                var matches = new List<MemoryField[]>();
                long validationOffset = 0;
                int searchDepth = FindMembers(schema, type, name, [], matches, 0, 0, ref validationOffset);
                if (matches.Count != 1)
                {
                    failure = new(matches.Count == 0 ? (type.IsProjected ? MemoryReadFailureKind.UnselectedMember : MemoryReadFailureKind.AbsentMember) : MemoryReadFailureKind.AmbiguousMember, matches.Count == 0 ? $"Member '{type.Id}.{name}' is absent{(type.IsProjected ? " from this projection" : string.Empty)}." : $"Ambiguous promoted member '{name}'.", fullPath);
                    return false;
                }

                MemoryField[] chain = matches[0];
                foreach (MemoryField member in chain)
                {
                    parent = type.Id;
                    type = schema.GetType(member.TypeId);
                    field = member;
                    offset += member.Offset;
                }

                steps.Add(new(type, 0, chain, false, false, searchDepth, validationOffset));
                continue;
            }

            failure = new(MemoryReadFailureKind.InvalidPath, $"Cannot traverse '{token}' through '{type.Id}'.", fullPath);
            return false;
        }

        selection = new(schema, root, path, [.. steps], type, field, parent, offset);
        return true;
    }

    /// <summary>Collects every direct or promoted candidate, preserving ambiguity and maximum search depth.</summary>
    /// <param name="schema">Owning schema.</param>
    /// <param name="type">Composite searched.</param>
    /// <param name="name">Requested member.</param>
    /// <param name="prefix">Promotion chain above this composite.</param>
    /// <param name="matches">Receives full candidate chains.</param>
    /// <param name="depth">Current promotion depth.</param>
    /// <param name="offset">Current composite's byte offset from the selected parent.</param>
    /// <param name="validationOffset">Greatest member-start offset checked by the ordinary promotion search, including empty unselected members.</param>
    /// <returns>Maximum promotion search depth, including unsuccessful branches.</returns>
    private static int FindMembers(MemorySchema schema, MemoryTypeDefinition type, string name, MemoryField[] prefix, List<MemoryField[]> matches, int depth, long offset, ref long validationOffset)
    {
        int maximum = depth;
        foreach (MemoryField member in type.Fields)
        {
            validationOffset = Math.Max(validationOffset, checked(offset + member.Offset));
            if (member.Name == name)
            {
                matches.Add([.. prefix, member]);
            }
            else if (member.Promoted)
            {
                maximum = Math.Max(maximum, FindMembers(schema, schema.GetType(member.TypeId), name, [.. prefix, member], matches, depth + 1, offset + member.Offset, ref validationOffset));
            }
        }

        return maximum;
    }

    /// <summary>Adds logical path and root information while retaining transport coordinates.</summary>
    /// <param name="failure">Original structured failure.</param>
    /// <param name="region">Caller root.</param>
    /// <returns>The contextual failure.</returns>
    internal MemoryReadFailure WithContext(MemoryReadFailure failure, MemoryRegion region)
        => failure with { Path = failure.Path ?? (this.Path.Length == 0 ? this.RootType.Id : this.RootType.Id + "." + this.Path), LogicalRegion = failure.LogicalRegion ?? region, };

    /// <summary>One explicit path token and its already resolved schema decisions.</summary>
    /// <param name="Type">Type after this token.</param>
    /// <param name="Offset">Array element byte offset.</param>
    /// <param name="Fields">Direct or promoted field chain.</param>
    /// <param name="Follow">Whether to resolve a stored pointer.</param>
    /// <param name="Address">Whether this is the terminal pointer address accessor.</param>
    /// <param name="SearchDepth">Maximum promotion depth searched for this token.</param>
    /// <param name="ValidationOffset">Greatest member-start offset examined by ordinary resolution.</param>
    private sealed record Step(MemoryTypeDefinition Type, long Offset, MemoryField[]? Fields, bool Follow, bool Address, int SearchDepth, long ValidationOffset = 0);
}
