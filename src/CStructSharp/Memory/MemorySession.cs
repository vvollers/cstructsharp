namespace CStructSharp.Memory;

using System.Collections;
using System.Globalization;
using System.Numerics;
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
/// The default resolver treats nonzero bits as an absolute address in the same source. Primitive decoding and
/// encoding always go through the core compiled codecs; composite traversal applies the schema's explicit offsets.
/// </para>
/// </remarks>
public sealed class MemorySession
{
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
    public MemoryInspection Inspect(MemoryRegion region, string typeId, string path = "", MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            MemorySelection selection = this.Resolve(region, typeId, path, context);
            var backing = new List<MemoryRegion>();
            MemoryPatch.Flatten(selection.Region, backing, context, 0);
            return new MemoryInspection(this.ReadCore(selection, context, 0), selection, backing.AsReadOnly());
        }
        catch (MemoryAccessException exception)
        {
            exception.Path ??= typeId + (path.Length == 0 ? string.Empty : "." + path);
            exception.LogicalRegion ??= region;
            throw;
        }
    }

    /// <summary>Resolves a path and decodes the selected value. Pointers stay <see cref="StoredPointer"/> unless the path consumes <c>.value</c>.</summary>
    /// <remarks>An empty path reads the root. Structs and unions produce <see cref="StructValue"/> dictionaries,
    /// arrays produce <c>object?[]</c>, scalars use their codec's managed type, and signed bit slices become
    /// <see cref="long"/>. A selected path decodes only the storage it needs, so an unavailable unrelated member
    /// does not prevent reading a known field. Reading a whole union returns every member's interpretation of the
    /// shared bytes; it does not decide which one the application's tag selects.</remarks>
    /// <param name="region">Finite caller-owned region holding the root record.</param>
    /// <param name="typeId">ID of the root record's type in the schema.</param>
    /// <param name="path">Member/index path from the root; pointer targets need an explicit <c>.value</c> step.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>The decoded scalar, stored pointer, structure, or fixed array.</returns>
    public object? Read(MemoryRegion region, string typeId, string path = "", MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            MemorySelection selected = this.Resolve(region, typeId, path, context);
            return this.ReadCore(selected, context, 0);
        }
        catch (MemoryAccessException exception)
        {
            exception.Path ??= typeId + (path.Length == 0 ? string.Empty : "." + path);
            exception.LogicalRegion ??= region;
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
    /// Every step consumes depth from the shared context, which bounds pathological paths and pointer chains.
    /// </para>
    /// </remarks>
    /// <param name="region">Finite caller-owned region holding the root record.</param>
    /// <param name="typeId">ID of the root record's type in the schema.</param>
    /// <param name="path">Member/index path from the root; pointer targets need an explicit <c>.value</c> step.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>The selected storage location and its metadata; the value is not decoded.</returns>
    public MemorySelection Resolve(MemoryRegion region, string typeId, string path = "", MemoryAccessContext? context = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(region);
            ArgumentNullException.ThrowIfNull(path);
            context ??= new MemoryAccessContext();
            context.CheckDepth(0);
            var selected = new MemorySelection(region.Slice(0, this.Schema.GetType(typeId).Size), this.Schema.GetType(typeId), null, null);
            MemoryRegion container = region;
            int depth = 0;
            bool addressSelected = false;
            foreach (string segment in Tokenize(path))
            {
                if (addressSelected)
                {
                    throw new ArgumentException("The address accessor must terminate a path.", nameof(path));
                }

                context.CheckDepth(++depth);
                MemoryTypeDefinition type = selected.Type;
                if (type.Kind == MemoryTypeKind.Pointer)
                {
                    if (segment == "address")
                    {
                        // Address selection retains StoredPointer semantics so writes cannot accidentally encode a resolved target.
                        addressSelected = true;
                        continue;
                    }

                    if (segment != "value" || type.ElementTypeId is null)
                    {
                        throw new ArgumentException("A typed pointer path must use '.value'; opaque pointers cannot be followed.", nameof(path));
                    }

                    // Following a pointer is the one path step that reads data: the target address is in the bytes.
                    StoredPointer pointer = (StoredPointer)this.ReadCore(selected, context, depth)!;
                    if (pointer.IsNull)
                    {
                        throw new MemoryAccessException(MemoryFailure.InvalidValue, selected.Region.Source.Id, selected.Region.Address, type.Size, "Cannot follow a null pointer.");
                    }

                    MemoryTypeDefinition target = this.Schema.GetType(type.ElementTypeId);
                    if (target.Kind == MemoryTypeKind.Incomplete)
                    {
                        throw new ArgumentException("Pointer target is incomplete.", nameof(path));
                    }

                    // The resolver decides what the bits mean; the session only trusts it for TargetSize bytes.
                    context.Charge(selected.Region.Source.Id, selected.Region.Address, 0);
                    MemoryRegion resolved = this.resolver(new PointerRequest(pointer, selected.Region, selected.Container ?? container, target.Id, target.Size, path, depth));
                    selected = new MemorySelection(resolved.Slice(0, target.Size), target, null, null);
                    container = selected.Region;
                }
                else if (segment.StartsWith('[') && type.Kind == MemoryTypeKind.Array)
                {
                    int index = int.Parse(segment.AsSpan(1, segment.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture);
                    if (index >= type.Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(path), "Array index exceeds the declared count.");
                    }

                    MemoryTypeDefinition element = this.Schema.GetType(type.ElementTypeId!);
                    selected = new MemorySelection(selected.Region.Slice(checked(index * element.Size), element.Size), element, null, null);
                    container = selected.Region;
                }
                else if (type.Kind is MemoryTypeKind.Struct or MemoryTypeKind.Union)
                {
                    container = selected.Region;
                    selected = this.FindMember(selected, segment, context, depth);
                }
                else
                {
                    throw new ArgumentException($"Cannot traverse '{segment}' through '{type.Id}'.", nameof(path));
                }
            }

            return selected;
        }
        catch (MemoryAccessException exception)
        {
            exception.Path ??= typeId + (path.Length == 0 ? string.Empty : "." + path);
            exception.LogicalRegion ??= region;
            throw;
        }
    }

    /// <summary>Creates the bytes of one new record from zero-filled storage. Gaps and unselected union bytes stay zero.</summary>
    /// <remarks>
    /// Struct input is a dictionary of member values, an array is an <see cref="IList"/> of the declared count, a
    /// pointer is a <see cref="StoredPointer"/> of the declared width, and a union is either its exact raw bytes or
    /// a <see cref="MemoryUnionSelection"/>. The method creates storage for this one value only; it does not allocate
    /// pointer targets or choose addresses. Write the result into a source yourself or pass it to
    /// <see cref="MemoryPatch.Create"/>. Unlike <see cref="PlanUpdate"/>, this starts from zeroes, so padding
    /// recorded by the metadata is initialized to zero rather than preserved.
    /// </remarks>
    /// <param name="typeId">ID of the type to encode.</param>
    /// <param name="value">Value in the declared shape for that type.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>A new owned byte array of the type's size containing the encoded value.</returns>
    public byte[] Serialize(string typeId, object? value, MemoryAccessContext? context = null)
    {
        context ??= new MemoryAccessContext();
        context.CheckDepth(0);
        MemoryTypeDefinition type = this.Schema.GetType(typeId);
        if (type.Size > context.MaxBytes)
        {
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, "serialize", 0, type.Size, "Output exceeds the byte budget.");
        }

        var bytes = new byte[type.Size];
        this.Encode(type, value, bytes, context, 0);
        return bytes;
    }

    /// <summary>Stages a replacement of the selected storage, preserving every byte and bit the new value does not cover. Nothing is written.</summary>
    /// <remarks>
    /// The selected bytes are read first and the new value is encoded over a copy of them, so padding and
    /// neighboring bit slices survive. Replacing a whole union through a <see cref="MemoryUnionSelection"/> is the
    /// exception: the union is cleared before the chosen member is encoded. The returned patch has already flattened
    /// mapping layers and captured expected bytes, which means planning reads the storage more than once and needs
    /// a budget larger than the replacement length. Call <see cref="MemoryPatch.Commit"/> to perform the writes.
    /// </remarks>
    /// <param name="region">Finite caller-owned region holding the root record.</param>
    /// <param name="typeId">ID of the root record's type in the schema.</param>
    /// <param name="path">Member/index path from the root; pointer targets need an explicit <c>.value</c> step.</param>
    /// <param name="value">New value in the declared shape of the selected type.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>An immutable preview of the physical writes; no backing bytes are changed.</returns>
    public MemoryPatch PlanUpdate(MemoryRegion region, string typeId, string path, object? value, MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            MemorySelection selected = this.Resolve(region, typeId, path, context);
            if (selected.Type.Size > context.MaxBytes)
            {
                throw new MemoryAccessException(MemoryFailure.BudgetExceeded, region.Source.Id, region.Address, selected.Type.Size, "Patch exceeds the byte budget.");
            }

            // Start from the current bytes so everything outside the new value is preserved.
            byte[] expected = ReadBytes(selected.Region, context);
            byte[] bytes = (byte[])expected.Clone();
            if (selected.Field?.BitWidth is not null)
            {
                // A bit slice is patched in place with the core's masked update, never re-encoded as a whole integer.
                CStruct codec = this.Schema.GetCodec(selected.Type, selected.Field, selected.ParentTypeId);
                using var staging = new MemoryStream(bytes, writable: true);
                codec.UpdateStream(staging, "__bits.value", EncodeBits(selected.Field, value));
            }
            else
            {
                this.Encode(selected.Type, value, bytes, context, 0);
            }

            return MemoryPatch.Create(selected.Region, bytes, context, expected);
        }
        catch (MemoryAccessException exception)
        {
            exception.Path ??= typeId + (path.Length == 0 ? string.Empty : "." + path);
            exception.LogicalRegion ??= region;
            throw;
        }
    }

    /// <summary>Copies a whole finite region into a new array, failing rather than padding if the source ends early.</summary>
    /// <remarks>The region must fit an <see cref="int"/>-sized allocation and the byte budget. The stream view
    /// tolerates positive short reads but turns a premature zero into <see cref="MemoryFailure.MissingBytes"/>, so
    /// absent bytes can never become default zeroes in the returned array.</remarks>
    /// <param name="region">Finite range to copy.</param>
    /// <param name="context">Shared budget charged by every underlying source read.</param>
    /// <returns>An owned array containing exactly the region's bytes.</returns>
    internal static byte[] ReadBytes(MemoryRegion region, MemoryAccessContext context)
    {
        if (region.Length > int.MaxValue || region.Length > context.MaxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "Region is too large for a materialized value.");
        }

        var bytes = new byte[(int)region.Length];
        using Stream stream = region.OpenRead(context);
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>The default resolver: the stored bits are an absolute address in the source the pointer was read from.</summary>
    /// <param name="request">The pointer being followed and where it was found.</param>
    private static MemoryRegion ResolveAbsolute(PointerRequest request) => new(request.Storage.Source, request.Pointer.Bits, request.TargetSize);

    /// <summary>Splits a path into member names and bracketed indexes, rejecting anything malformed.</summary>
    /// <remarks>Tokenizing is independent of types and bytes; <see cref="Resolve"/> later decides whether a token
    /// is a member, an element, or a pointer accessor. Rejecting empty components, stray separators, and
    /// non-numeric indexes here means a typo can never fall through to a neighboring but wrong selection.</remarks>
    /// <param name="path">Path text; empty means the root value.</param>
    /// <returns>Tokens in order: member names and <c>[n]</c> index strings.</returns>
    private static IReadOnlyList<string> Tokenize(string path)
    {
        if (path.Length == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        int position = 0;
        while (position < path.Length)
        {
            int start = position;
            if (path[position] == '[')
            {
                int end = path.IndexOf(']', position);
                if (end < 0 || !int.TryParse(path.AsSpan(position + 1, end - position - 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    throw new ArgumentException("Invalid array index.", nameof(path));
                }

                position = end + 1;
            }
            else
            {
                while (position < path.Length && path[position] is not ('.' or '['))
                {
                    position++;
                }

                if (position == start)
                {
                    throw new ArgumentException("Empty path component.", nameof(path));
                }
            }

            result.Add(path[start..position]);

            // After a token comes either a '.', a '[' (starting an index), or the end of the path.
            if (position < path.Length && path[position] == '.')
            {
                if (++position == path.Length)
                {
                    throw new ArgumentException("Trailing path separator.", nameof(path));
                }
            }
            else if (position < path.Length && path[position] != '[')
            {
                throw new ArgumentException("Missing path separator.", nameof(path));
            }
        }

        return result;
    }

    /// <summary>Range-checks a bit-slice input and converts it to the unsigned bit pattern the core updater expects.</summary>
    /// <remarks>A width-<c>w</c> slice holds <c>[0, 2^w)</c> unsigned or <c>[-2^(w-1), 2^(w-1))</c> signed.
    /// <see cref="BigInteger"/> keeps the bounds exact even for a 64-bit slice. A negative value becomes its
    /// two's-complement pattern by adding <c>2^w</c>, but only after the range check; masking first would silently
    /// turn an out-of-range input into a different valid value.</remarks>
    /// <param name="field">Field whose width and signedness define the accepted range.</param>
    /// <param name="value">Integer to encode, in any form <see cref="Convert.ToString(object, IFormatProvider)"/> renders as digits.</param>
    /// <returns>The unsigned bit pattern to store.</returns>
    private static ulong EncodeBits(MemoryField field, object? value)
    {
        BigInteger integer = BigInteger.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);
        int width = field.BitWidth!.Value;
        BigInteger modulus = BigInteger.One << width;
        BigInteger minimum = field.Signed ? -(modulus >> 1) : BigInteger.Zero;
        BigInteger maximum = field.Signed ? (modulus >> 1) - 1 : modulus - 1;
        if (integer < minimum || integer > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value does not fit the bit slice.");
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
    /// <param name="depth">Current path depth.</param>
    private MemorySelection FindMember(MemorySelection parent, string name, MemoryAccessContext context, int depth)
    {
        context.CheckDepth(depth);
        MemorySelection? found = null;
        foreach (MemoryField field in parent.Type.Fields)
        {
            MemoryTypeDefinition type = this.Schema.GetType(field.TypeId);
            var candidate = new MemorySelection(parent.Region.Slice(field.Offset, type.Size), type, field, parent.Type.Id, parent.Region);
            if (field.Name == name)
            {
                if (found is not null)
                {
                    throw new ArgumentException($"Ambiguous promoted member '{name}'.");
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
                        throw new ArgumentException($"Ambiguous promoted member '{name}'.");
                    }

                    found = nested;
                }
            }
        }

        return found ?? throw new KeyNotFoundException($"Member '{parent.Type.Id}.{name}' is absent.");
    }

    /// <summary>Like <see cref="FindMember"/> but returns null for an absent member, so promotion search can continue; ambiguity still throws.</summary>
    /// <param name="parent">Selection of the promoted composite being searched.</param>
    /// <param name="name">Member name to find.</param>
    /// <param name="context">Shared context for depth checks.</param>
    /// <param name="depth">Current path depth.</param>
    private MemorySelection? TryFindMember(MemorySelection parent, string name, MemoryAccessContext context, int depth)
    {
        try
        {
            return this.FindMember(parent, name, context, depth);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Decodes a selection: scalars and pointers through the core codecs, composites by recursing over their explicit members.</summary>
    /// <remarks>Recursion follows by-value storage only. A pointer leaf returns a <see cref="StoredPointer"/>, so a
    /// cyclic pointer graph never triggers implicit recursive reads. A signed slice is sign-extended after the core
    /// has extracted its unsigned bits. Union members are decoded as separate interpretations of the same bytes.
    /// Each composite level charges one zero-byte request, so a huge array cannot be decoded for free.</remarks>
    /// <param name="selected">Region, type, and optional field to decode.</param>
    /// <param name="context">Shared budget charged by every source read and composite level.</param>
    /// <param name="depth">Current nesting depth for the depth limit.</param>
    private object? ReadCore(MemorySelection selected, MemoryAccessContext context, int depth)
    {
        context.CheckDepth(depth);
        context.Charge(selected.Region.Source.Id, selected.Region.Address, 0);
        MemoryTypeDefinition type = selected.Type;
        if (type.Kind is MemoryTypeKind.Scalar or MemoryTypeKind.Pointer)
        {
            // The region's stream view hands the bytes to the core codec; a bit slice uses its own slice codec.
            using Stream stream = selected.Region.OpenRead(context);
            CStruct codec = this.Schema.GetCodec(type, selected.Field, selected.ParentTypeId);
            object value = codec.ReadValue(stream, selected.Field?.BitWidth is null ? MemorySchema.CodecRoot(type) : "__bits.value")!;
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

            return values;
        }

        if (type.Kind is MemoryTypeKind.Struct or MemoryTypeKind.Union)
        {
            var values = new StructValue();
            foreach (MemoryField field in type.Fields)
            {
                MemoryTypeDefinition member = this.Schema.GetType(field.TypeId);
                object? value = this.ReadCore(new MemorySelection(selected.Region.Slice(field.Offset, member.Size), member, field, type.Id), context, depth + 1);
                if (field.Promoted && value is StructValue promoted)
                {
                    // Promoted members appear directly in the parent dictionary, as they do in C source.
                    foreach (KeyValuePair<string, object?> pair in promoted)
                    {
                        values.Add(pair.Key, pair.Value);
                    }
                }
                else
                {
                    values.Add(field.Name, value);
                }
            }

            return values;
        }

        throw new ArgumentException("Cannot read an incomplete type by value.");
    }

    /// <summary>Encodes a value into a destination span, using core serialization for scalars and explicit offsets for composites.</summary>
    /// <remarks>The destination's initial contents decide what happens to bytes the value does not cover:
    /// <see cref="Serialize"/> passes zeroes, <see cref="PlanUpdate"/> passes a copy of the existing bytes. Struct
    /// encoding touches only member ranges. A whole-union encoding with a <see cref="MemoryUnionSelection"/>
    /// deliberately clears the union first so stale bytes of another interpretation cannot masquerade as part of
    /// the new value. Pointers must arrive as <see cref="StoredPointer"/> of the declared width; their bits are
    /// written unchanged.</remarks>
    /// <param name="type">Type being encoded.</param>
    /// <param name="value">Value in the declared shape for that type.</param>
    /// <param name="destination">Span of exactly the type's size to encode into.</param>
    /// <param name="context">Shared budget charged for staged bytes and each composite level.</param>
    /// <param name="depth">Current nesting depth for the depth limit.</param>
    private void Encode(MemoryTypeDefinition type, object? value, Span<byte> destination, MemoryAccessContext context, int depth)
    {
        context.CheckDepth(depth);
        context.Charge("serialize", 0, 0);
        if (type.Kind is MemoryTypeKind.Scalar or MemoryTypeKind.Pointer)
        {
            object? scalar = value;
            if (type.Kind == MemoryTypeKind.Pointer)
            {
                if (value is not StoredPointer pointer || pointer.Width != type.Size)
                {
                    throw new ArgumentException("Pointer writes require StoredPointer with the declared width.", nameof(value));
                }

                scalar = pointer.Bits;
            }

            ArgumentNullException.ThrowIfNull(scalar);
            byte[] bytes = this.Schema.GetCodec(type).Serialize(MemorySchema.CodecRoot(type), scalar);
            if (bytes.Length != destination.Length)
            {
                throw new ArgumentException("Codec output differs from the metadata extent.");
            }

            context.Charge("serialize", 0, bytes.Length);
            bytes.CopyTo(destination);
        }
        else if (type.Kind == MemoryTypeKind.Array)
        {
            if (value is not IList values || values.Count != type.Count)
            {
                throw new ArgumentException("Array value must have the declared count.", nameof(value));
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
                // Raw bytes reproduce storage exactly; a selection names one interpretation and zeroes the rest.
                if (value is byte[] raw && raw.Length == type.Size)
                {
                    context.Charge("serialize", 0, raw.Length);
                    raw.CopyTo(destination);
                    return;
                }

                if (value is not MemoryUnionSelection selection)
                {
                    throw new ArgumentException("Union writes require raw bytes or MemoryUnionSelection.", nameof(value));
                }

                destination.Clear();
                this.EncodeField(type, this.Schema.GetField(type.Id, selection.Member), selection.Value, destination, context, depth);
                return;
            }

            if (value is not IReadOnlyDictionary<string, object?> members)
            {
                throw new ArgumentException("Struct writes require a named member dictionary.", nameof(value));
            }

            foreach (MemoryField field in type.Fields)
            {
                // A promoted member may be supplied flattened in the parent dictionary, so pass the parent through.
                object? memberValue = field.Promoted && !members.ContainsKey(field.Name) ? value : members[field.Name];
                this.EncodeField(type, field, memberValue, destination, context, depth);
            }
        }
        else
        {
            throw new ArgumentException("Cannot write an incomplete type.");
        }
    }

    /// <summary>Encodes one member into its slice of the destination; a bit slice changes only its own bits of the storage unit.</summary>
    /// <remarks>A bit slice cannot be serialized as a fresh integer, because that would overwrite the other slices
    /// sharing the storage unit. Instead the unit's current bytes are copied out, the core masked
    /// <see cref="CStruct.UpdateStream"/> changes only the selected bits, and the unit is copied back.</remarks>
    /// <param name="parent">Containing struct or union, which keys the slice codec.</param>
    /// <param name="field">Member to encode.</param>
    /// <param name="value">Value for that member.</param>
    /// <param name="destination">Span of the whole containing record.</param>
    /// <param name="context">Shared budget charged for staged bytes.</param>
    /// <param name="depth">Current nesting depth for the depth limit.</param>
    private void EncodeField(MemoryTypeDefinition parent, MemoryField field, object? value, Span<byte> destination, MemoryAccessContext context, int depth)
    {
        MemoryTypeDefinition member = this.Schema.GetType(field.TypeId);
        Span<byte> target = destination.Slice(field.Offset, member.Size);
        if (field.BitWidth is not null)
        {
            byte[] bytes = target.ToArray();
            using var stream = new MemoryStream(bytes, writable: true);
            this.Schema.GetCodec(member, field, parent.Id).UpdateStream(stream, "__bits.value", EncodeBits(field, value));
            context.Charge("serialize", 0, bytes.Length);
            bytes.CopyTo(target);
        }
        else
        {
            this.Encode(member, value, target, context, depth + 1);
        }
    }
}
