namespace CStructSharp.Memory;

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using CStructSharp.Addressing;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>Applies a <see cref="MemorySchema"/> to regions of caller-owned address spaces: resolve a path, read or inspect a value, serialize a record, or plan an update.</summary>
/// <remarks>
/// <para>
/// A session joins two independent descriptions. The schema says how a type's bytes are arranged; a region says
/// where some bytes live. Neither knows about the other, so one session can read the same record type from a file,
/// from a mapped process image, and from a test fixture. The session itself keeps no bytes and no per-region state,
/// which is why it can be reused freely and why every operation takes its own <see cref="MemoryAccessContext"/>.
/// </para>
/// <para>
/// The four public operations build on one another. <see cref="Resolve"/> turns a path such as
/// <c>"items[2].next.value.pid"</c> into a <see cref="MemorySelection"/>: the region, type, and field the path
/// names. <see cref="Read"/> resolves and then decodes the selected bytes. <see cref="Inspect"/> additionally
/// flattens mapping layers to report where the bytes physically came from. <see cref="PlanUpdate"/> resolves,
/// reads the current bytes, encodes the new value over them, and returns a <see cref="MemoryPatch"/> that a caller
/// commits separately. <see cref="Serialize"/> stands apart: it needs no region because it creates new bytes.
/// </para>
/// <para>
/// Pointers are the one place where the session must read data to know where to go next. Reading a pointer field
/// yields a <see cref="StoredPointer"/> and stops. Only the explicit <c>.value</c> path step calls the resolver
/// supplied at construction, which may apply a relative base, strip a tag, or switch to another address space.
/// The default resolver treats a nonzero stored value as an absolute address in the same source. Primitive decoding and
/// encoding always go through the core compiled codecs; composite traversal applies the schema's explicit offsets.
/// </para>
/// <para>
/// Failures use the core <see cref="CStructException"/> hierarchy. A path that cannot be parsed or resolved against
/// the schema throws <see cref="CStructPathException"/>; a value that does not have the declared shape throws
/// <see cref="CStructWriteException"/>; memory that cannot be read throws <see cref="MemoryAccessException"/>, a
/// <see cref="CStructReadException"/>. When one of these crosses a session operation, the session records the
/// requested <c>typeId.path</c> as <see cref="CStructException.Path"/> and, for a memory access failure, the root
/// region as <see cref="MemoryAccessException.LogicalRegion"/>. Null arguments and cancellation keep their .NET
/// exception types.
/// </para>
/// </remarks>
public sealed class MemorySession
{
    /// <summary>The number of distinct paths whose steps <see cref="Steps"/> keeps; later paths are split on every use.</summary>
    private const int StepCacheCapacity = 256;

    /// <summary>
    ///     The steps of the paths resolved so far, by exact path text: paths are usually repeated literals, so a resolution
    ///     reuses their steps instead of splitting the path again. Bounded so generated paths cannot grow it without limit.
    /// </summary>
    private static readonly ConcurrentDictionary<string, PathStep[]> StepCache = new(StringComparer.Ordinal);

    private readonly Func<PointerRequest, MemoryRegion> resolver;

    /// <summary>Creates a session over a compiled schema, optionally with a custom pointer resolver.</summary>
    /// <param name="schema">Validated type graph that describes the records this session reads.</param>
    /// <param name="resolver">Turns stored pointer bits into a target region; null selects absolute addressing in the pointer's own source.</param>
    public MemorySession(MemorySchema schema, Func<PointerRequest, MemoryRegion>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        this.Schema = schema;
        this.resolver = resolver ?? ResolveAbsolute;
    }

    /// <summary>Gets the immutable compiled schema this session applies.</summary>
    public MemorySchema Schema { get; }

    /// <summary>Reads one selection and reports both its logical location and the physical backing ranges that supplied its bytes.</summary>
    /// <remarks>
    /// Use this for an inspector that must show where a value came from. Flattening mapping tables costs extra
    /// requests beyond the value read, so prefer <see cref="Read"/> when provenance is unnecessary. Backing ranges
    /// follow every <see cref="MappedMemorySource"/> layer; a custom source is reported as-is because the library
    /// cannot see inside it. The result describes this read; it does not lock the bytes against later change.
    /// </remarks>
    /// <param name="region">Finite caller-owned region holding the root record.</param>
    /// <param name="typeId">ID of the root record's type in the schema.</param>
    /// <param name="path">Member/index path from the root; pointer targets need an explicit <c>.value</c> step.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>The value, its selection metadata, and the ordered backing ranges.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> or <paramref name="path"/> is null.</exception>
    /// <exception cref="CStructPathException"><paramref name="typeId"/> is unknown, or the path is malformed or names no member, element, or pointer that can be followed.</exception>
    /// <exception cref="MemoryAccessException">The bytes needed to resolve or read the value are unavailable, a followed pointer is null, or the budget is exhausted.</exception>
    /// <exception cref="OperationCanceledException">The context's token was cancelled.</exception>
    public MemoryInspection Inspect(MemoryRegion region, string typeId, string path = "", MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            MemorySelection selection = this.Resolve(region, typeId, path, context);
            List<MemoryRegion> backing = selection.Region.FlattenMappings(context);
            return new MemoryInspection(this.ReadCore(selection, context, 0), selection, backing.AsReadOnly());
        }
        catch (CStructException exception)
        {
            AttachContext(exception, typeId, path, region);
            throw;
        }
    }

    /// <summary>Resolves a path and decodes the selected value. Pointers stay <see cref="StoredPointer"/> unless the path consumes <c>.value</c>.</summary>
    /// <remarks>An empty path reads the root. Values have the shapes the core reader returns: a struct is a
    /// <see cref="StructValue"/>, a union a <see cref="UnionValue"/> with its raw storage and every member's
    /// interpretation of it, an array of a numeric or <see cref="bool"/> scalar a <see cref="PrimitiveArray{T}"/>
    /// and any other array a <see cref="List{T}"/> of <see cref="object"/>, a scalar its codec's managed type, and a
    /// signed bit slice a <see cref="long"/>. A selected path decodes only the storage it needs, so an unavailable
    /// unrelated member does not prevent reading a known field. Reading a whole union does not decide which member
    /// the application's tag selects.</remarks>
    /// <param name="region">Finite caller-owned region holding the root record.</param>
    /// <param name="typeId">ID of the root record's type in the schema.</param>
    /// <param name="path">Member/index path from the root; pointer targets need an explicit <c>.value</c> step.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>The decoded scalar, stored pointer, structure, or fixed array.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> or <paramref name="path"/> is null.</exception>
    /// <exception cref="CStructPathException"><paramref name="typeId"/> is unknown, or the path is malformed or names no member, element, or pointer that can be followed.</exception>
    /// <exception cref="MemoryAccessException">The bytes needed to resolve or read the value are unavailable, a followed pointer is null, or the budget is exhausted.</exception>
    /// <exception cref="OperationCanceledException">The context's token was cancelled.</exception>
    public object? Read(MemoryRegion region, string typeId, string path = "", MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            MemorySelection selected = this.Resolve(region, typeId, path, context);
            return this.ReadCore(selected, context, 0);
        }
        catch (CStructException exception)
        {
            AttachContext(exception, typeId, path, region);
            throw;
        }
    }

    /// <summary>Walks a path step by step to the region, type, and field it names, without decoding the final value.</summary>
    /// <remarks>
    /// <para>
    /// The walk starts with the root type over the root region and applies one token at a time. A member name
    /// slices the current region by the field's offset; an index slices an array by element size; both are pure
    /// arithmetic on schema data. A <c>.value</c> step on a pointer is different: the session must read the pointer
    /// bytes, refuse null and incomplete targets, and ask the resolver for the target region, which may lie outside
    /// the root region or in another source. A <c>.address</c> step keeps the pointer's stored-bit semantics and
    /// must end the path, so an update through it can never encode a resolved target by mistake.
    /// </para>
    /// <para>
    /// Member and index steps count against the context's <see cref="MemoryAccessContext.MaxNestingDepth"/>, which
    /// restarts at each pointer target; every <c>.value</c> step counts against
    /// <see cref="MemoryAccessContext.MaxPointerDepth"/>. Together they bound pathological paths and pointer chains.
    /// </para>
    /// </remarks>
    /// <param name="region">Finite caller-owned region holding the root record.</param>
    /// <param name="typeId">ID of the root record's type in the schema.</param>
    /// <param name="path">Member/index path from the root; pointer targets need an explicit <c>.value</c> step.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>The selected storage location and its metadata; the value is not decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> or <paramref name="path"/> is null.</exception>
    /// <exception cref="CStructPathException"><paramref name="typeId"/> is unknown, or the path is malformed or names no member, element, or pointer that can be followed.</exception>
    /// <exception cref="MemoryAccessException">The bytes needed to resolve or read the value are unavailable, a followed pointer is null, or the budget is exhausted.</exception>
    /// <exception cref="OperationCanceledException">The context's token was cancelled.</exception>
    public MemorySelection Resolve(MemoryRegion region, string typeId, string path = "", MemoryAccessContext? context = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(region);
            ArgumentNullException.ThrowIfNull(path);
            context ??= new MemoryAccessContext();
            context.CheckNestingDepth(0);
            var selected = new MemorySelection(region.Slice(0, this.Schema.GetType(typeId).Size), this.Schema.GetType(typeId), null, null);
            MemoryRegion container = region;

            // depth counts every path step and is reported to the resolver; nesting counts the value levels
            // entered since the last pointer target, and pointerSteps counts followed pointers. Nesting restarts
            // at each target because a pointer target is a new root value, not a member of the value that held it.
            int depth = 0;
            int nesting = 0;
            int pointerSteps = 0;
            bool addressSelected = false;
            foreach (PathStep step in Steps(path))
            {
                if (addressSelected)
                {
                    throw new CStructPathException("The address accessor must terminate a path.");
                }

                depth++;
                context.CheckNestingDepth(++nesting);
                MemoryTypeDefinition type = selected.Type;
                if (type.Kind == MemoryTypeKind.Pointer)
                {
                    if (step.Member == "address")
                    {
                        // Address selection retains StoredPointer semantics so writes cannot accidentally encode a resolved target.
                        addressSelected = true;
                        continue;
                    }

                    if (step.Member != "value" || type.ElementTypeId is null)
                    {
                        throw new CStructPathException("A typed pointer path must use '.value'; opaque pointers cannot be followed.");
                    }

                    // Following a pointer is the one path step that reads data: the target address is in the bytes.
                    context.CheckPointerDepth(++pointerSteps);
                    StoredPointer pointer = (StoredPointer)this.ReadCore(selected, context, nesting)!;
                    if (pointer.IsNull)
                    {
                        throw new MemoryAccessException(MemoryFailure.InvalidValue, selected.Region.Source.Id, selected.Region.Address, type.Size, "Cannot follow a null pointer.");
                    }

                    MemoryTypeDefinition target = this.Schema.GetType(type.ElementTypeId);
                    if (target.Kind == MemoryTypeKind.Incomplete)
                    {
                        throw new CStructPathException("Pointer target is incomplete.");
                    }

                    // The resolver decides what the bits mean; the session only trusts it for TargetSize bytes.
                    context.Charge(selected.Region.Source.Id, selected.Region.Address, 0);
                    MemoryRegion resolved = this.resolver(new PointerRequest(pointer, selected.Region, selected.Container ?? container, target.Id, target.Size, path, depth));
                    selected = new MemorySelection(resolved.Slice(0, target.Size), target, null, null);
                    container = selected.Region;
                    nesting = 0;
                }
                else if (step.Member is null && type.Kind == MemoryTypeKind.Array)
                {
                    int index = step.Index;
                    if (index >= type.Count)
                    {
                        // As in the core path resolver, an index past the declared count is a path error.
                        throw new CStructPathException(string.Create(CultureInfo.InvariantCulture, $"Array index {index} is out of range for '{type.Id}' with length {type.Count}."));
                    }

                    MemoryTypeDefinition element = this.Schema.GetType(type.ElementTypeId!);
                    selected = new MemorySelection(selected.Region.Slice(checked(index * element.Size), element.Size), element, null, null);
                    container = selected.Region;
                }
                else if (step.Member is not null && type.Kind is MemoryTypeKind.Struct or MemoryTypeKind.Union)
                {
                    container = selected.Region;
                    selected = this.FindMember(selected, step.Member, context, nesting);
                }
                else
                {
                    throw new CStructPathException($"Cannot traverse '{step}' through '{type.Id}'.");
                }
            }

            return selected;
        }
        catch (CStructException exception)
        {
            AttachContext(exception, typeId, path, region);
            throw;
        }
    }

    /// <summary>Creates the bytes of one new record from zero-filled storage. Gaps and unselected union bytes stay zero.</summary>
    /// <remarks>
    /// Struct input is a dictionary of member values, an array is an <see cref="IList"/> of the declared count, a
    /// pointer is a <see cref="StoredPointer"/> of the declared width, and a union is a <see cref="UnionValue"/>, as
    /// for the core writer: <see cref="UnionValue.FromRaw"/> for its exact bytes or <see cref="UnionValue.FromMember"/>
    /// for one member. The method creates storage for this one value only; it does not allocate
    /// pointer targets or choose addresses. Write the result into a source yourself or pass it to
    /// <see cref="MemoryPatch.Create"/>. Unlike <see cref="PlanUpdate"/>, this starts from zeroes, so padding
    /// recorded by the metadata is initialized to zero rather than preserved.
    /// </remarks>
    /// <param name="typeId">ID of the type to encode.</param>
    /// <param name="value">Value in the declared shape for that type.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>A new owned byte array of the type's size containing the encoded value.</returns>
    /// <exception cref="CStructPathException"><paramref name="typeId"/> is unknown or names an incomplete type.</exception>
    /// <exception cref="CStructWriteException"><paramref name="value"/> does not have the declared shape, or a scalar codec rejects it.</exception>
    /// <exception cref="MemoryAccessException">The output or the nesting exceeds the budget; the failure has no source coordinates.</exception>
    /// <exception cref="OperationCanceledException">The context's token was cancelled.</exception>
    public byte[] Serialize(string typeId, object? value, MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            context.CheckNestingDepth(0);
            MemoryTypeDefinition type = this.Schema.GetType(typeId);
            if (type.Size > context.MaxTotalBytes)
            {
                // New output is not read from any source, so the failure has no source coordinates.
                throw new MemoryAccessException(MemoryFailure.BudgetExceeded, null, null, type.Size, "Output exceeds the byte budget.");
            }

            var bytes = new byte[type.Size];
            this.Encode(type, value, bytes, context, 0);
            return bytes;
        }
        catch (CStructException exception)
        {
            exception.AttachContext(typeId);
            throw;
        }
    }

    /// <summary>Stages a replacement of the selected storage, preserving every byte and bit the new value does not cover. Nothing is written.</summary>
    /// <remarks>
    /// The selected bytes are read first and the new value is encoded over a copy of them, so padding and
    /// neighboring bit slices survive. Replacing a whole union with a <see cref="UnionValue"/> that selects a
    /// member is the exception: the union is cleared before the chosen member is encoded. The returned patch has already flattened
    /// mapping layers and captured expected bytes, which means planning reads the storage more than once and needs
    /// a budget larger than the replacement length. Call <see cref="MemoryPatch.Commit"/> to perform the writes.
    /// </remarks>
    /// <param name="region">Finite caller-owned region holding the root record.</param>
    /// <param name="typeId">ID of the root record's type in the schema.</param>
    /// <param name="path">Member/index path from the root; pointer targets need an explicit <c>.value</c> step.</param>
    /// <param name="value">New value in the declared shape of the selected type.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>An immutable preview of the physical writes; no backing bytes are changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> or <paramref name="path"/> is null.</exception>
    /// <exception cref="CStructPathException"><paramref name="typeId"/> is unknown, or the path is malformed or names nothing writable.</exception>
    /// <exception cref="CStructWriteException"><paramref name="value"/> does not have the declared shape, or a bit-slice value is out of range.</exception>
    /// <exception cref="MemoryAccessException">The current bytes are unavailable, the selection exceeds the byte budget, or planning found a stale source.</exception>
    /// <exception cref="OperationCanceledException">The context's token was cancelled.</exception>
    public MemoryPatch PlanUpdate(MemoryRegion region, string typeId, string path, object? value, MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            MemorySelection selected = this.Resolve(region, typeId, path, context);
            if (selected.Type.Size > context.MaxTotalBytes)
            {
                throw new MemoryAccessException(MemoryFailure.BudgetExceeded, selected.Region.Source.Id, selected.Region.Address, selected.Type.Size, "Patch exceeds the byte budget.");
            }

            // Start from the current bytes so everything outside the new value is preserved.
            byte[] expected = selected.Region.ReadAll(context);
            byte[] bytes = (byte[])expected.Clone();
            if (selected.Field?.BitWidth is not null)
            {
                // A bit slice is patched in place with the core's masked update, never re-encoded as a whole integer.
                this.Schema.GetCodec(selected.Type, selected.Field, selected.ParentTypeId).Layout.Update(bytes, "__bits.value", EncodeBits(selected.Field, value));
            }
            else
            {
                this.Encode(selected.Type, value, bytes, context, 0);
            }

            return MemoryPatch.Create(selected.Region, bytes, context, expected);
        }
        catch (CStructException exception)
        {
            AttachContext(exception, typeId, path, region);
            throw;
        }
    }

    /// <summary>
    ///     Records what the caller asked for on a failure that crosses a session operation: the requested
    ///     <c>typeId.path</c> as the exception's path and, for a memory access failure, the root region. Context a
    ///     lower layer already recorded is kept.
    /// </summary>
    /// <param name="exception">The failure leaving the operation.</param>
    /// <param name="typeId">The requested root type ID.</param>
    /// <param name="path">The requested member path; empty for the root.</param>
    /// <param name="region">The caller's root region.</param>
    private static void AttachContext(CStructException exception, string typeId, string? path, MemoryRegion region)
    {
        exception.AttachContext(string.IsNullOrEmpty(path) ? typeId : typeId + "." + path);
        if (exception is MemoryAccessException access)
        {
            access.LogicalRegion ??= region;
        }
    }

    /// <summary>The default resolver: the stored bits are an absolute address in the source the pointer was read from.</summary>
    /// <param name="request">The pointer being followed and where it was found.</param>
    private static MemoryRegion ResolveAbsolute(PointerRequest request) => new(request.Storage.Source, request.Pointer.Address, request.TargetSize);

    /// <summary>
    ///     Splits a path into the steps the resolution takes: each member name, then each of its indexes. The path uses the
    ///     layout path grammar relative to the selected type (<see cref="CStructPathResolver.ParseRelative"/>): an empty
    ///     path is the value itself, and a path may start with an index of an array value.
    /// </summary>
    /// <remarks>Parsing is independent of types and bytes; <see cref="Resolve"/> later decides whether a member step is a
    /// member or a pointer accessor, and whether an index fits. Rejecting malformed paths here means a typo can never fall
    /// through to a neighboring but wrong selection.</remarks>
    /// <param name="path">Path text; empty means the root value.</param>
    /// <returns>The steps in order; the shared array must not be modified.</returns>
    /// <exception cref="CStructPathException">The path is malformed.</exception>
    private static PathStep[] Steps(string path)
    {
        if (StepCache.TryGetValue(path, out PathStep[]? cached))
        {
            return cached;
        }

        IReadOnlyList<PathSegment> segments = CStructPathResolver.ParseRelative(path);
        int count = 0;
        for (int index = 0; index < segments.Count; index++)
        {
            count += (segments[index].Name.Length > 0 ? 1 : 0) + segments[index].Indexes.Count;
        }

        PathStep[] steps = count == 0 ? [] : new PathStep[count];
        int next = 0;
        for (int index = 0; index < segments.Count; index++)
        {
            // A relative path's leading indexes form a first segment with no name.
            PathSegment segment = segments[index];
            if (segment.Name.Length > 0)
            {
                steps[next++] = new PathStep(segment.Name, 0);
            }

            for (int dimension = 0; dimension < segment.Indexes.Count; dimension++)
            {
                steps[next++] = new PathStep(null, segment.Indexes[dimension]);
            }
        }

        if (StepCache.Count < StepCacheCapacity)
        {
            StepCache.TryAdd(path, steps);
        }

        return steps;
    }

    /// <summary>Range-checks a bit-slice input and converts it to the unsigned bit pattern the core updater expects.</summary>
    /// <remarks>A width-<c>w</c> slice holds <c>[0, 2^w)</c> unsigned or <c>[-2^(w-1), 2^(w-1))</c> signed.
    /// <see cref="BigInteger"/> keeps the bounds exact even for a 64-bit slice. A negative value becomes its
    /// two's-complement pattern by adding <c>2^w</c>, but only after the range check; masking first would silently
    /// turn an out-of-range input into a different valid value.</remarks>
    /// <param name="field">Field whose width and signedness define the accepted range.</param>
    /// <param name="value">Integer to encode, in any form <see cref="Convert.ToString(object, IFormatProvider)"/> renders as digits.</param>
    /// <returns>The unsigned bit pattern to store.</returns>
    /// <exception cref="CStructWriteException">The value is not an integer or does not fit the slice.</exception>
    private static ulong EncodeBits(MemoryField field, object? value)
    {
        if (!BigInteger.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger integer))
        {
            throw new CStructWriteException($"Bit slice '{field.Name}' requires an integer value.");
        }

        int width = field.BitWidth!.Value;
        BigInteger modulus = BigInteger.One << width;
        BigInteger minimum = field.Signed ? -(modulus >> 1) : BigInteger.Zero;
        BigInteger maximum = field.Signed ? (modulus >> 1) - 1 : modulus - 1;
        if (integer < minimum || integer > maximum)
        {
            throw new CStructWriteException($"Value does not fit the bit slice '{field.Name}'.");
        }

        return (ulong)(integer < 0 ? integer + modulus : integer);
    }

    /// <summary>Finds a direct member, or a member of a promoted anonymous composite, without decoding any bytes.</summary>
    /// <remarks>Promotion makes a nested anonymous struct or union's members visible at the parent level, as C
    /// does for anonymous members. All candidates are examined rather than returning the first match: two promoted
    /// members with the same name are ambiguous, and picking either silently would hand the caller the wrong
    /// address.</remarks>
    /// <param name="parent">Selection of the containing struct or union.</param>
    /// <param name="name">Member name to find.</param>
    /// <param name="context">Shared context, used for depth checks while descending into promoted members.</param>
    /// <param name="depth">Current nesting depth within the current pointer target, checked against <see cref="MemoryAccessContext.MaxNestingDepth"/>.</param>
    /// <returns>The selection of the member.</returns>
    /// <exception cref="CStructPathException">No member has that name, or two promoted members do.</exception>
    private MemorySelection FindMember(MemorySelection parent, string name, MemoryAccessContext context, int depth)
        => this.TryFindMember(parent, name, context, depth) ?? throw new CStructPathException($"Member '{parent.Type.Id}.{name}' is absent.");

    /// <summary>Like <see cref="FindMember"/> but returns null for an absent member, so promotion search can continue; ambiguity still throws.</summary>
    /// <param name="parent">Selection of the struct or union being searched.</param>
    /// <param name="name">Member name to find.</param>
    /// <param name="context">Shared context for depth checks.</param>
    /// <param name="depth">Current nesting depth within the current pointer target, checked against <see cref="MemoryAccessContext.MaxNestingDepth"/>.</param>
    /// <returns>The selection of the member, or null when neither the composite nor its promoted members declare it.</returns>
    /// <exception cref="CStructPathException">Two promoted members have that name.</exception>
    private MemorySelection? TryFindMember(MemorySelection parent, string name, MemoryAccessContext context, int depth)
    {
        context.CheckNestingDepth(depth);
        MemorySelection? found = null;
        foreach (MemoryField field in parent.Type.Fields)
        {
            if (field.Name != name && !field.Promoted)
            {
                // Schema validation proves every extent fits the bounded parent selection. Its one remaining
                // address failure is an empty member just past ulong.MaxValue; retain that check in field order.
                _ = checked(parent.Region.Address + (ulong)field.Offset);
                continue;
            }

            MemoryTypeDefinition type = this.Schema.GetType(field.TypeId);
            var candidate = new MemorySelection(parent.Region.Slice(field.Offset, type.Size), type, field, parent.Type.Id, parent.Region);
            if (field.Name == name)
            {
                if (found is not null)
                {
                    throw new CStructPathException($"Ambiguous promoted member '{name}'.");
                }

                found = candidate;
            }
            else if (field.Promoted)
            {
                MemorySelection? nested = this.TryFindMember(candidate, name, context, depth + 1);
                if (nested is not null)
                {
                    if (found is not null)
                    {
                        throw new CStructPathException($"Ambiguous promoted member '{name}'.");
                    }

                    found = nested;
                }
            }
        }

        return found;
    }

    /// <summary>Decodes a selection: scalars and pointers through the core codecs, composites by recursing over their explicit members.</summary>
    /// <remarks>Recursion follows by-value storage only. A pointer leaf returns a <see cref="StoredPointer"/>, so a
    /// cyclic pointer graph never triggers implicit recursive reads. A signed slice is sign-extended after the core
    /// has extracted its unsigned bits. Union members are decoded as separate interpretations of the same bytes.
    /// Each composite level charges one zero-byte request, so a huge array cannot be decoded for free.</remarks>
    /// <param name="selected">Region, type, and optional field to decode.</param>
    /// <param name="context">Shared budget charged by every source read and composite level.</param>
    /// <param name="depth">Current nesting depth, checked against <see cref="MemoryAccessContext.MaxNestingDepth"/>.</param>
    private object? ReadCore(MemorySelection selected, MemoryAccessContext context, int depth)
    {
        context.CheckNestingDepth(depth);
        context.Charge(selected.Region.Source.Id, selected.Region.Address, 0);
        MemoryTypeDefinition type = selected.Type;
        if (type.Kind is MemoryTypeKind.Scalar or MemoryTypeKind.Pointer)
        {
            // The scalar's bytes go to the core codec as a span; a bit slice uses its own slice codec.
            Span<byte> bytes = type.Size <= 64 ? stackalloc byte[type.Size] : new byte[type.Size];
            selected.Region.ReadExactly(bytes, context);
            object value = this.Schema.GetCodec(type, selected.Field, selected.ParentTypeId).Decode(bytes);
            if (type.Kind == MemoryTypeKind.Pointer)
            {
                return new StoredPointer(Convert.ToUInt64(value, CultureInfo.InvariantCulture), type.Size);
            }

            if (selected.Field is { Signed: true, BitWidth: int width, })
            {
                // Sign-extend: if the top bit of the slice is set, the value is bits - 2^width.
                ulong bits = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                return (bits & (1UL << (width - 1))) == 0 ? (long)bits : (long)(new BigInteger(bits) - (BigInteger.One << width));
            }

            return value;
        }

        if (type.Kind == MemoryTypeKind.Array)
        {
            // Refuse up front rather than fail part-way through a huge array whose elements each cost a request.
            if (type.Count > context.MaxRequests - context.Requests)
            {
                throw new MemoryAccessException(MemoryFailure.BudgetExceeded, selected.Region.Source.Id, selected.Region.Address, type.Size, "Array exceeds remaining work budget.");
            }

            MemoryTypeDefinition element = this.Schema.GetType(type.ElementTypeId!);
            var values = new object?[type.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = this.ReadCore(new MemorySelection(selected.Region.Slice(checked(i * element.Size), element.Size), element, null, null), context, depth + 1);
            }

            return this.ToArrayValue(element, values);
        }

        if (type.Kind is MemoryTypeKind.Struct or MemoryTypeKind.Union)
        {
            var members = new List<KeyValuePair<string, object?>>(type.Fields.Count);
            foreach (MemoryField field in type.Fields)
            {
                MemoryTypeDefinition member = this.Schema.GetType(field.TypeId);
                object? value = this.ReadCore(new MemorySelection(selected.Region.Slice(field.Offset, member.Size), member, field, type.Id), context, depth + 1);
                if (field.Promoted && value is IReadOnlyDictionary<string, object?> promoted)
                {
                    // Promoted members appear directly in the parent, as they do in C source.
                    members.AddRange(promoted);
                }
                else
                {
                    members.Add(new KeyValuePair<string, object?>(field.Name, value));
                }
            }

            if (type.Kind == MemoryTypeKind.Union)
            {
                // As the core reader does, a union keeps its complete storage next to the member views, so writing
                // the value back without selecting a member reproduces the bytes exactly.
                return UnionValue.FromParsed(type.Name, selected.Region.ReadAll(context), members);
            }

            var structValue = new StructValue();
            foreach (KeyValuePair<string, object?> pair in members)
            {
                structValue.Add(pair.Key, pair.Value);
            }

            return structValue;
        }

        if (type.Kind == MemoryTypeKind.RawBytes)
        {
            // No field layout survived validation for this type, so its bytes are handed back verbatim rather
            // than decoded; the caller still gets exactly the declared size, just not decomposed into members.
            return selected.Region.ReadAll(context);
        }

        throw new CStructPathException($"Cannot read incomplete type '{type.Id}' by value.");
    }

    /// <summary>
    ///     Gives decoded array elements the shape the core reader uses: a <see cref="PrimitiveArray{T}"/> for a plain
    ///     numeric or <see cref="bool"/> scalar element, and a <see cref="List{T}"/> of <see cref="object"/> otherwise.
    /// </summary>
    /// <param name="element">The array's element type.</param>
    /// <param name="values">The decoded elements, in order.</param>
    /// <returns>The array value.</returns>
    private IList<object?> ToArrayValue(MemoryTypeDefinition element, object?[] values)
    {
        // An element with its own declaration is an enum or other declared codec, which the core also returns as a
        // list. For an empty array the element's managed type comes from decoding zero bytes, without a source read.
        if (element.Kind != MemoryTypeKind.Scalar || element.Declaration is not null)
        {
            return new List<object?>(values);
        }

        // The core's typed-array rule (PrimitiveArrayReader), keyed by the decoded element's managed type.
        object? sample = values.Length > 0 ? values[0] : this.Schema.GetCodec(element).Decode(new byte[element.Size]);
        return (sample is null ? null : PrimitiveArrayReader.FromBoxed(sample.GetType(), values)) ?? new List<object?>(values);
    }

    /// <summary>Encodes a value into a destination span, using core serialization for scalars and explicit offsets for composites.</summary>
    /// <remarks>The destination's initial contents decide what happens to bytes the value does not cover:
    /// <see cref="Serialize"/> passes zeroes, <see cref="PlanUpdate"/> passes a copy of the existing bytes. Struct
    /// encoding touches only member ranges. A whole-union encoding with a <see cref="UnionValue"/> that selects a
    /// member deliberately clears the union first so stale bytes of another interpretation cannot masquerade as part of
    /// the new value. Pointers must arrive as <see cref="StoredPointer"/> of the declared width; their stored
    /// value is written unchanged.</remarks>
    /// <param name="type">Type being encoded.</param>
    /// <param name="value">Value in the declared shape for that type.</param>
    /// <param name="destination">Span of exactly the type's size to encode into.</param>
    /// <param name="context">Shared budget charged for staged bytes and each composite level.</param>
    /// <param name="depth">Current nesting depth, checked against <see cref="MemoryAccessContext.MaxNestingDepth"/>.</param>
    /// <exception cref="CStructWriteException">The value does not have the declared shape, or a scalar codec rejects it.</exception>
    /// <exception cref="CStructPathException">The type is incomplete and has no storage to write.</exception>
    private void Encode(MemoryTypeDefinition type, object? value, Span<byte> destination, MemoryAccessContext context, int depth)
    {
        context.CheckNestingDepth(depth);
        context.Charge(null, null, 0);
        if (type.Kind is MemoryTypeKind.Scalar or MemoryTypeKind.Pointer)
        {
            object? scalar = value;
            if (type.Kind == MemoryTypeKind.Pointer)
            {
                if (value is not StoredPointer pointer || pointer.Width != type.Size)
                {
                    throw new CStructWriteException($"Pointer '{type.Id}' is written from a StoredPointer {type.Size} bytes wide.");
                }

                scalar = pointer.Address;
            }

            if (scalar is null)
            {
                throw new CStructWriteException($"Null is not a valid value for scalar '{type.Id}'.");
            }

            // The schema checked that the codec's size is the metadata extent, so it writes exactly the destination.
            int written = this.Schema.GetCodec(type).Layout.Serialize(destination, MemorySchema.CodecRoot(type), scalar);
            if (written != destination.Length)
            {
                throw new CStructWriteException($"Codec output for '{type.Id}' differs from the metadata extent.");
            }

            context.Charge(null, null, written);
        }
        else if (type.Kind == MemoryTypeKind.Array)
        {
            if (value is not IList values || values.Count != type.Count)
            {
                throw new CStructWriteException(string.Create(CultureInfo.InvariantCulture, $"Array '{type.Id}' is written from an IList of exactly {type.Count} elements."));
            }

            MemoryTypeDefinition element = this.Schema.GetType(type.ElementTypeId!);
            for (int i = 0; i < type.Count; i++)
            {
                this.Encode(element, values[i], destination.Slice(checked(i * element.Size), element.Size), context, depth + 1);
            }
        }
        else if (type.Kind is MemoryTypeKind.Struct or MemoryTypeKind.Union)
        {
            if (type.Kind == MemoryTypeKind.Union)
            {
                // As in the core writer: a selected member is encoded over zeroed storage, so stale bytes of another
                // interpretation cannot masquerade as part of the new value; without a selection the raw storage
                // is reproduced exactly (a union value that was read writes back unchanged).
                if (value is not UnionValue union || !string.Equals(union.UnionName, type.Name, StringComparison.Ordinal))
                {
                    throw new CStructWriteException($"Union '{type.Name}' is written from a UnionValue with that name (UnionValue.FromRaw or UnionValue.FromMember).");
                }

                if (union.HasSelection)
                {
                    destination.Clear();
                    MemoryField selected = this.Schema.FindField(type.Id, union.SelectedMember)
                        ?? throw new CStructWriteException(WriteFailures.UnknownUnionMember(type.Name, union.SelectedMember));
                    this.EncodeField(type, selected, union.SelectedValue, destination, context, depth);
                    return;
                }

                byte[] raw = union.GetRawStorageArray();
                if (raw.Length != type.Size)
                {
                    throw new CStructWriteException(WriteFailures.RawStorageLengthMismatch(type.Name, type.Size, raw.Length));
                }

                context.Charge(null, null, raw.Length);
                raw.CopyTo(destination);
                return;
            }

            if (value is not IReadOnlyDictionary<string, object?> members)
            {
                throw new CStructWriteException($"Struct '{type.Id}' is written from a dictionary of named member values.");
            }

            foreach (MemoryField field in type.Fields)
            {
                // A promoted member may be supplied flattened in the parent dictionary, so pass the parent through.
                if (!members.TryGetValue(field.Name, out object? memberValue))
                {
                    memberValue = field.Promoted ? value : throw new CStructWriteException(WriteFailures.NoValueSupplied(field.Name));
                }

                this.EncodeField(type, field, memberValue, destination, context, depth);
            }
        }
        else if (type.Kind == MemoryTypeKind.RawBytes)
        {
            // The mirror of the raw-bytes read path: no field layout survived validation, so the caller must supply
            // exactly the declared number of raw bytes rather than a decomposed member value.
            if (value is not byte[] raw || raw.Length != type.Size)
            {
                throw new CStructWriteException(string.Create(CultureInfo.InvariantCulture, $"Raw-bytes type '{type.Id}' is written from exactly {type.Size} raw bytes."));
            }

            context.Charge(null, null, raw.Length);
            raw.CopyTo(destination);
        }
        else
        {
            throw new CStructPathException($"Cannot write incomplete type '{type.Id}'.");
        }
    }

    /// <summary>Encodes one member into its slice of the destination; a bit slice changes only its own bits of the storage unit.</summary>
    /// <remarks>A bit slice cannot be serialized as a fresh integer, because that would overwrite the other slices
    /// sharing the storage unit. Instead the core masked
    /// <see cref="CStruct.Update(Span{byte}, string, object, System.Collections.Generic.IReadOnlyDictionary{string, int}?, UpdateOptions?)"/> changes only the selected bits of the unit in place.</remarks>
    /// <param name="parent">Containing struct or union, which keys the slice codec.</param>
    /// <param name="field">Member to encode.</param>
    /// <param name="value">Value for that member.</param>
    /// <param name="destination">Span of the whole containing record.</param>
    /// <param name="context">Shared budget charged for staged bytes.</param>
    /// <param name="depth">Current nesting depth, checked against <see cref="MemoryAccessContext.MaxNestingDepth"/>.</param>
    /// <exception cref="CStructWriteException">The value does not fit the member.</exception>
    private void EncodeField(MemoryTypeDefinition parent, MemoryField field, object? value, Span<byte> destination, MemoryAccessContext context, int depth)
    {
        MemoryTypeDefinition member = this.Schema.GetType(field.TypeId);
        Span<byte> target = destination.Slice(field.Offset, member.Size);
        if (field.BitWidth is not null)
        {
            this.Schema.GetCodec(member, field, parent.Id).Layout.Update(target, "__bits.value", EncodeBits(field, value));
            context.Charge(null, null, target.Length);
        }
        else
        {
            this.Encode(member, value, target, context, depth + 1);
        }
    }

    /// <summary>One step of a resolution: a member (or pointer accessor) name, or an array index.</summary>
    /// <param name="Member">The member or accessor name, or <see langword="null"/> for an index.</param>
    /// <param name="Index">The array index, when <paramref name="Member"/> is <see langword="null"/>.</param>
    private readonly record struct PathStep(string? Member, int Index)
    {
        /// <summary>Returns the step as a path spells it: the member name, or <c>[n]</c>.</summary>
        /// <returns>The step's text.</returns>
        public override string ToString() => this.Member ?? string.Create(CultureInfo.InvariantCulture, $"[{this.Index}]");
    }
}
