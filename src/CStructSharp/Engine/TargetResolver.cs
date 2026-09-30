namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     The compiled engine's path resolver: finds the storage a path (<c>root.items[2].name</c>,
///     <c>root.next.value.id</c>) selects by walking only the members before it, on the operation's cursor and variable
///     slots, and produces its address, the captures a later member's count may name, the bytes read and charged, and
///     the failures in a fixed order.
/// </summary>
/// <remarks>
///     <para>
///         <b>The walk.</b> In each struct on the path the members are placed one after another from the struct's first
///         byte (<see cref="PlacementCursor"/>), members of an arm the data does not select are skipped, and each member
///         before the target is first captured - its value read only when an expression may need it: an integer scalar, a
///         pointer's stored address, an enum - and then measured, reading only what its size depends on: a nested struct's
///         members, variable-length values (LEB128, terminated text, a variable-size caller's codec), data-sized array
///         counts. A fixed-size member is skipped arithmetically without reading or charging its bytes. A union is measured
///         from its size, after the counts inside it are checked against the limits.
///     </para>
///     <para>
///         <b>Order.</b> A member is captured before it is measured; a variable-length value the capture read is not read
///         again to measure it, so every byte before the target is read and charged at most once. The target's own value is
///         not read here; the caller reads it from the returned address (<see cref="ReadEngine"/>), and a whole terminated
///         array the caller reads is not counted here either, since that read scans it.
///     </para>
///     <para>
///         <b>Pointers.</b> Only the pointers the path names are followed: <c>.value</c> reads the stored address and
///         continues at the target, <c>.address</c> selects the pointer's own storage. The resolver's checks come in its own
///         order (following allowed, the depth limit, a <c>@count</c> target rejected, the fixed target size, the address,
///         a cycle), and a relative address that overflows is a path failure.
///     </para>
///     <para>
///         <b>A caller's codec.</b> A value of a codec that declares a fixed size occupies exactly that size, so it is skipped
///         like any fixed-size member; only a variable-size codec's values are read to find where they end.
///     </para>
/// </remarks>
internal static partial class TargetResolver
{
    /// <summary>The largest single value a capture or measurement reads through its codec (an <c>int128</c> or a UUID).</summary>
    private const int ScratchSize = 16;

    /// <summary>
    ///     The number of elements a terminated array stores: its values and the all-zero terminator element after them,
    ///     which is part of the array's extent.
    /// </summary>
    /// <param name="valueCount">The number of values before the terminator.</param>
    /// <returns>The stored element count.</returns>
    /// <exception cref="OverflowException">The count and its terminator do not fit an <see cref="int"/>.</exception>
    internal static int CountStoredTerminatedElements(int valueCount) => checked(valueCount + 1);

    /// <summary>
    ///     Resolves a path from the cursor's position (the root's first byte). A one-segment path selects the root
    ///     itself, after a root array's count is taken and checked (<see cref="EnginePrograms.ResolvesCountFirst"/>); a
    ///     longer path walks the root struct.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the root's first byte; the walk leaves it where its last read ended.</param>
    /// <param name="state">The operation's state: the slots the walk captures into and reads counts from, the limits, the depths.</param>
    /// <param name="segments">The parsed path, at least one segment.</param>
    /// <param name="debugPrefix">
    ///     A list that receives the names of the path's struct and field segments (the path a debug parse of the target
    ///     records under), or <see langword="null"/> when no debug parse follows.
    /// </param>
    /// <param name="readsTarget">
    ///     Whether the caller reads the target's value next (<c>ReadValue</c>): a whole terminated array is then left to that
    ///     read to scan, count and check, and its <see cref="ResolvedPath.ArrayLength"/> is <see langword="null"/>.
    /// </param>
    /// <returns>The selected storage.</returns>
    /// <exception cref="CStructPathException">The path names nothing, indexes out of range, or traverses what it cannot.</exception>
    /// <exception cref="CStructException">A value the walk reads is short or invalid, or a limit is exceeded.</exception>
    public static ResolvedPath Resolve<TCursor>(ref TCursor cursor, ref ReadEngineState state, IReadOnlyList<PathSegment> segments, List<string>? debugPrefix, bool readsTarget)
        where TCursor : struct, IReadCursor
    {
        if (segments.Count == 0)
        {
            throw new CStructPathException("Path is empty.");
        }

        LayoutCompilation compilation = state.Layout.Compilation;
        CompiledModelQueries model = compilation.ModelQueries;
        if (!model.TryGetCompiledDeclaration(segments[0].Name, out CStructElement? root))
        {
            throw model.UnknownRoot(segments[0].Name);
        }

        long rootStart = cursor.Position;
        CStructElement? resolvedRoot = model.ResolveCompiledNamedElement(root);
        var walk = new PathWalk(segments, debugPrefix, compilation.SlotTable.TargetPrograms, readsTarget);
        if (segments.Count == 1)
        {
            return ResolveRoot(ref cursor, ref state, walk, root, resolvedRoot, rootStart);
        }

        if (resolvedRoot is not Struct rootStruct)
        {
            throw new CStructPathException("Root path cannot contain child segments: " + segments[0].Name);
        }

        debugPrefix?.Add(rootStruct.Name.Name);
        TargetProgram program = walk.Programs.GetComposite(compilation, compilation.SizeQueries.GetCompiledComposite(rootStruct));
        return ResolveInStruct(ref cursor, ref state, walk, program, rootStart, 1, default);
    }

    /// <summary>
    ///     Resolves a one-segment path: the root itself at the operation's origin. A struct root must fit one nesting level,
    ///     and the count of a root array is taken now and checked against the element limit - a data-sized one counted from
    ///     the data - so a failure there comes before the root is read.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="declaredRoot">The root's declaration.</param>
    /// <param name="resolvedRoot">The root's resolved named type, or <see langword="null"/>.</param>
    /// <param name="rootStart">The root's first byte.</param>
    /// <returns>The root target.</returns>
    private static ResolvedPath ResolveRoot<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, CStructElement declaredRoot, CStructElement? resolvedRoot, long rootStart)
        where TCursor : struct, IReadCursor
    {
        LayoutCompilation compilation = state.Layout.Compilation;
        CStructElement targetElement = resolvedRoot ?? declaredRoot;
        CompiledCompositeType? rootComposite = targetElement is Struct rootStruct ? compilation.SizeQueries.GetCompiledComposite(rootStruct) : null;
        if (rootComposite is not null)
        {
            EnsureStructureDepth(ref state, 1);
        }

        // A typedef, enum or spelled root (`uint16[EOF]`) is one compiled field: its array shape and count are checked
        // like a nested field's.
        CompiledField? rootField = declaredRoot is Typedef or CstructEnum ? compilation.ModelQueries.GetCompiledRootField(declaredRoot) : null;
        bool rootIsArray = rootField is not null && IsArray(rootField);
        int? rootArrayLength = rootIsArray
                                   ? Count(ref cursor, ref state, rootField!, walk.Programs.GetRootField(compilation, rootField!), rootStart, allDimensions: false)
                                   : null;
        return new ResolvedPath
        {
            Kind = ResolvedTargetKind.Root,
            Address = rootStart,
            Effective = rootField,
            TargetComposite = rootComposite ?? rootField?.TargetComposite,
            IsArray = rootIsArray,
            ArrayLength = rootArrayLength,
            Declared = rootField,
        };
    }

    /// <summary>
    ///     Finds a requested member of a struct or union inside one claimed nesting level (cancellation observed on entry,
    ///     then the depth limit).
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The struct's or union's walk.</param>
    /// <param name="start">Its first byte.</param>
    /// <param name="pathIndex">The segment to find.</param>
    /// <param name="pointers">The pointers the path followed so far.</param>
    /// <returns>The resolved target.</returns>
    private static ResolvedPath ResolveInStruct<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program, long start, int pathIndex, PointerContext pointers)
        where TCursor : struct, IReadCursor
    {
        state.EnterStructure(ref cursor);
        try
        {
            return ResolveInComposite(ref cursor, ref state, walk, program, start, pathIndex, pointers);
        }
        finally
        {
            state.StructureDepth--;
        }
    }

    /// <summary>
    ///     Finds a requested member inside an anonymous promoted struct or union, whose members belong to the struct that
    ///     already holds the nesting level: no level of its own, but cancellation is observed on entry like every composite.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The promoted member's walk.</param>
    /// <param name="start">Its first byte.</param>
    /// <param name="pathIndex">The segment to find, which the promoted member does not consume.</param>
    /// <param name="pointers">The pointers the path followed so far.</param>
    /// <returns>The resolved target.</returns>
    private static ResolvedPath ResolveInPromoted<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program, long start, int pathIndex, PointerContext pointers)
        where TCursor : struct, IReadCursor
    {
        cursor.ThrowIfCancellationRequested();
        return ResolveInComposite(ref cursor, ref state, walk, program, start, pathIndex, pointers);
    }

    /// <summary>
    ///     Resolves one segment inside a struct or union whose level is already claimed. In a union every member starts at
    ///     the union's first byte and nothing before it is read. In a struct the members before the requested one are placed,
    ///     captured and measured in declaration order, under the struct's conditional selection and scope; the first active
    ///     member of that name - or the anonymous member that holds it - is entered.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The composite's walk.</param>
    /// <param name="start">Its first byte.</param>
    /// <param name="pathIndex">The segment to find.</param>
    /// <param name="pointers">The pointers the path followed so far.</param>
    /// <returns>The resolved target.</returns>
    /// <exception cref="CStructPathException">The path ends here, or the composite has no such member.</exception>
    private static ResolvedPath ResolveInComposite<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program, long start, int pathIndex, PointerContext pointers)
        where TCursor : struct, IReadCursor
    {
        if (pathIndex >= walk.Segments.Count)
        {
            throw new CStructPathException("Path ended before selecting a field.");
        }

        string requested = walk.Segments[pathIndex].Name;
        CompiledCompositeType composite = program.Composite;
        if (composite.IsUnion)
        {
            return ResolveInUnion(ref cursor, ref state, walk, program, start, pathIndex, pointers);
        }

        // The composite's conditional names are removed at entry, so an outer value is not read as the composite's own.
        ConditionalScopeSlots? scope = program.Scope;
        if (scope is not null)
        {
            foreach (int slot in scope.ClearedSlots)
            {
                state.Slots.Set(slot, SlotValue.Undefined);
            }
        }

        Span<int> arms = program.GroupCount <= FrameArena.StackArmLimit ? stackalloc int[FrameArena.StackArmLimit] : new int[program.GroupCount];
        arms[..program.GroupCount].Fill(FrameArena.Undecided);
        int locals = scope is { LocalCount: > 0, } ? state.TakeLocals(scope.LocalCount) : -1;
        var placer = new PlacementCursor(start, state.Layout.Aligned, state.Layout.BitfieldPacking, state.Layout.Compilation.HighBitFirst);
        try
        {
            TargetMember[] members = program.Members;
            for (int index = 0; index < members.Length; index++)
            {
                TargetMember member = members[index];
                CompiledField field = member.Field;
                if (!IsActive(ref state, program, member, arms))
                {
                    continue;
                }

                (long fieldStart, int bitOffset, int unitSize) = CompositeFieldPlacementCursor.AdvanceToField(ref placer, field);
                if (field.IsZeroWidthBitfield)
                {
                    continue;
                }

                if (string.Equals(field.Name, requested, StringComparison.Ordinal))
                {
                    return ResolveInField(ref cursor, ref state, walk, member, fieldStart, pathIndex, bitOffset, field.BitSize > 0 ? unitSize : 0, pointers);
                }

                // An anonymous member consumes no segment: the same segment is looked up among its own (recursively
                // promoted) members. The lookup is a pure walk of the compiled model, so a miss costs nothing and a hit
                // cannot fail as an unknown member (construction rejects a name two promoted members share).
                if (member.Promoted && field.Composite is { } promoted && promoted.TryFindField(requested, out _))
                {
                    return ResolveInPromoted(ref cursor, ref state, walk, NestedProgram(ref state, walk, member, promoted), fieldStart, pathIndex, pointers);
                }

                bool readByCapture = Capture(ref cursor, ref state, member, fieldStart, bitOffset, unitSize);
                if (field.BitSize == 0)
                {
                    // A variable-length value the capture read ends where the capture left the cursor; reading it again to
                    // measure it would read and charge its bytes twice.
                    placer.CompleteField(readByCapture && ReadsToMeasure(member) ? cursor.Position : MeasureFieldEnd(ref cursor, ref state, walk, member, fieldStart));
                }

                if (scope is not null)
                {
                    state.CompleteMember(scope, index, locals);
                }
            }

            throw new CStructPathException($"Unknown field '{requested}' in '{composite.Name}'.");
        }
        finally
        {
            if (locals >= 0)
            {
                state.ReleaseLocals(locals);
            }
        }
    }

    /// <summary>
    ///     Resolves one segment inside a union: a member of an anonymous struct or union member is looked up there first
    ///     (every member starts at the union's first byte), then the union's own member, a bitfield in a unit of its declared
    ///     size at bit 0.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The union's walk.</param>
    /// <param name="start">The union's first byte.</param>
    /// <param name="pathIndex">The segment to find.</param>
    /// <param name="pointers">The pointers the path followed so far.</param>
    /// <returns>The resolved target.</returns>
    /// <exception cref="CStructPathException">The union has no such member.</exception>
    private static ResolvedPath ResolveInUnion<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program, long start, int pathIndex, PointerContext pointers)
        where TCursor : struct, IReadCursor
    {
        string requested = walk.Segments[pathIndex].Name;
        CompiledCompositeType union = program.Composite;
        if (!union.FieldsByName.ContainsKey(requested))
        {
            foreach (CompiledField promoted in union.PromotedFields)
            {
                if (promoted.Composite is { } promotedMember && promotedMember.TryFindField(requested, out _))
                {
                    TargetProgram nested = walk.Programs.GetComposite(state.Layout.Compilation, promotedMember);
                    return ResolveInPromoted(ref cursor, ref state, walk, nested, start, pathIndex, pointers);
                }
            }
        }

        CompiledField field = union.FindField(requested);
        int storage = field.BitSize > 0
                          ? field.BitStorageSize ?? throw new InvalidOperationException("Compiled bitfield has no storage size: " + field.Name)
                          : 0;
        return ResolveInField(ref cursor, ref state, walk, program.Members[field.MemberIndex], start, pathIndex, 0, storage, pointers);
    }

    /// <summary>
    ///     Resolves the segment that names a member: its indexes select an element or row, one dimension per index (each
    ///     checked against its count, then the element's start measured); then the path ends there, continues into the
    ///     member's struct, or continues through its pointer (<c>.value</c>, <c>.address</c>).
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="member">The member the segment names.</param>
    /// <param name="fieldStart">The member's first byte (its storage unit's, for a bitfield).</param>
    /// <param name="pathIndex">The segment's index.</param>
    /// <param name="bitOffset">A bitfield's first bit within its unit; 0 otherwise.</param>
    /// <param name="bitStorageSize">A bitfield's unit size in bytes; 0 otherwise.</param>
    /// <param name="pointers">The pointers the path followed so far.</param>
    /// <returns>The resolved target.</returns>
    /// <exception cref="CStructPathException">An index is not allowed or out of range, or the path cannot continue from the member.</exception>
    private static ResolvedPath ResolveInField<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetMember member, long fieldStart, int pathIndex, int bitOffset, int bitStorageSize, PointerContext pointers)
        where TCursor : struct, IReadCursor
    {
        PathSegment segment = walk.Segments[pathIndex];
        CompiledField declared = member.Field;
        bool declaredIsArray = IsArray(declared);
        if (segment.Indexes.Count > 0 && !declaredIsArray)
        {
            throw new CStructPathException("Field is not an indexable fixed array: " + segment.Name);
        }

        int totalDimensions = declared.Array.Dimensions.Length;
        if (segment.Indexes.Count > totalDimensions)
        {
            throw new CStructPathException(
                $"Too many array indices for {segment.Name}: expected at most {totalDimensions}, got {segment.Indexes.Count}.");
        }

        // Each index peels one dimension: the count of the dimension it indexes is checked first, then the element's start
        // is measured over the elements before it.
        CompiledField resolved = declared;
        long elementStart = fieldStart;
        int peeled = 0;
        foreach (int index in segment.Indexes)
        {
            int dimensionCount = Count(ref cursor, ref state, resolved, member, elementStart, allDimensions: false);
            if (index >= dimensionCount)
            {
                throw new CStructPathException($"Array index {index} is out of range for {segment.Name} with length {dimensionCount}.");
            }

            CompiledField element = walk.Programs.GetElementView(declared, ++peeled, resolved);
            elementStart = ElementStart(ref cursor, ref state, walk, member, resolved, element, elementStart, index);
            resolved = element;
        }

        bool remainingIsArray = IsArray(resolved);

        // A whole terminated array the caller reads next is scanned, counted and checked by that read; counting it here too
        // would read and charge its bytes twice.
        bool readScansTarget = walk.ReadsTarget && pathIndex == walk.Segments.Count - 1 && resolved.Array.Kind == CompiledArrayKind.Terminated;
        int? arrayLength = remainingIsArray && !readScansTarget ? Count(ref cursor, ref state, resolved, member, elementStart, allDimensions: false) : null;
        int? selectedIndex = segment.Indexes.Count > 0 && !remainingIsArray ? segment.Indexes[^1] : null;
        walk.DebugPrefix?.Add(DebugSegment(declared, segment));
        if (remainingIsArray && pathIndex + 1 < walk.Segments.Count)
        {
            throw new CStructPathException("An array index is required before traversing: " + segment.Name);
        }

        var selection = new Selection(declared, segment.Indexes.Count, declaredIsArray, arrayLength, selectedIndex);
        if (pathIndex == walk.Segments.Count - 1)
        {
            // A struct or union target is read one level deeper, so it must fit.
            if (resolved.Composite is not null)
            {
                EnsureStructureDepth(ref state, state.StructureDepth + 1);
            }

            return new ResolvedPath
            {
                Kind = selectedIndex.HasValue ? ResolvedTargetKind.ArrayElement : ResolvedTargetKind.Field,
                Address = elementStart,
                Effective = resolved,
                TargetComposite = resolved.TargetComposite,
                BitOffset = bitOffset,
                BitStorageSize = bitStorageSize,
                RemainingPointerDepth = resolved.PointerDepth,
                IsArray = declaredIsArray,
                ArrayLength = arrayLength,
                SelectedArrayIndex = selectedIndex,
                PointerAccessorsConsumed = pointers.Consumed,
                PointerTargetAddress = pointers.TargetAddress,
                ContainingStructureDepth = state.StructureDepth,
                Declared = declared,
                Indexes = segment.Indexes.Count,
            };
        }

        PathSegment next = walk.Segments[pathIndex + 1];
        if (resolved.PointerDepth > 0)
        {
            if (string.Equals(next.Name, "address", StringComparison.Ordinal))
            {
                if (next.Indexes.Count > 0 || pathIndex + 1 != walk.Segments.Count - 1)
                {
                    throw new CStructPathException("Pointer .address must be the terminal path segment.");
                }

                return PointerAddress(ref state, resolved, elementStart, selection, pointers);
            }

            if (!string.Equals(next.Name, "value", StringComparison.Ordinal) || next.Indexes.Count > 0)
            {
                throw new CStructPathException("Expected pointer accessor '.value' or '.address' after: " + segment.Name);
            }

            return ResolvePointer(ref cursor, ref state, walk, resolved, elementStart, pathIndex + 1, selection, pointers);
        }

        if (resolved.Composite is { } nested)
        {
            return ResolveInStruct(ref cursor, ref state, walk, NestedProgram(ref state, walk, member, nested), elementStart, pathIndex + 1, pointers);
        }

        throw new CStructPathException("Cannot traverse through scalar field: " + segment.Name);
    }

    /// <summary>
    ///     Follows one <c>.value</c> accessor: following must be allowed, the depth limit admit one more level, the pointer
    ///     have no <c>@count</c> target (whose count may name a member after it, which a path never reads) and its fixed
    ///     target fit the target size limit; then the stored address is read, resolved (a relative one that overflows is a
    ///     path failure) and bounded, and a target already on the path is a cycle. The path then ends at the target,
    ///     follows another level, or enters the target struct; the level and the active target are released whatever happens.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="field">The pointer at the level being followed (the member, or a view of its remaining levels).</param>
    /// <param name="storage">The pointer's stored address's first byte.</param>
    /// <param name="valueIndex">The index of the <c>.value</c> segment.</param>
    /// <param name="selection">The array selection of the pointer member.</param>
    /// <param name="pointers">The pointers the path followed before this one.</param>
    /// <returns>The resolved target.</returns>
    private static ResolvedPath ResolvePointer<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, CompiledField field, long storage, int valueIndex, Selection selection, PointerContext pointers)
        where TCursor : struct, IReadCursor
    {
        if (!state.DereferencePointers)
        {
            throw new CStructPathException("Pointer dereference is disabled for the selected path.");
        }

        if (state.PointerDepth >= state.MaxPointerDepth)
        {
            throw new CStructReadLimitException(ReadFailures.PointerDepthLimit);
        }

        if (field.HasCountedTarget)
        {
            // The count may name a sibling declared after the pointer, which path resolution never reads.
            throw new CStructPathException("A path cannot select the target of a @count pointer; read the containing struct instead: " + field.Name);
        }

        state.Layout.EnsurePointerTargetSize(field.PointerDepth, field, state.MaxPointerTargetBytes, 1);
        long target = ReadPointerTargetAddress(ref cursor, ref state, storage);
        pointers = new PointerContext(target, checked(pointers.Consumed + 1));
        (long Address, string TypeName, int PointerDepth) key = (target, field.TypeSpelling, field.PointerDepth);
        HashSet<(long Address, string TypeName, int PointerDepth)>? active = target != 0 ? state.Pointers.ActiveTargets : null;
        if (active is not null && !active.Add(key))
        {
            throw new CStructReadException("Cyclic pointer target detected at stream address " + target + ".");
        }

        state.PointerDepth++;
        try
        {
            if (valueIndex == walk.Segments.Count - 1)
            {
                // A struct or union target is read one level deeper, so it must fit.
                if (field.PointerDepth == 1 && field.TargetComposite is not null)
                {
                    EnsureStructureDepth(ref state, state.StructureDepth + 1);
                }

                return new ResolvedPath
                {
                    Kind = ResolvedTargetKind.PointerValue,
                    Address = target,
                    Effective = field,
                    TargetComposite = field.TargetComposite,
                    RemainingPointerDepth = field.PointerDepth - 1,
                    IsArray = selection.IsArray,
                    ArrayLength = selection.Length,
                    SelectedArrayIndex = selection.Index,
                    PointerAccessorsConsumed = pointers.Consumed,
                    PointerTargetAddress = target,
                    ContainingStructureDepth = state.StructureDepth,
                    Declared = selection.Declared,
                    Indexes = selection.Indexes,
                };
            }

            if (target == 0)
            {
                throw new CStructPathException("Cannot traverse through a null pointer target.");
            }

            if (field.PointerDepth > 1)
            {
                PathSegment next = walk.Segments[valueIndex + 1];
                if (next.Indexes.Count > 0)
                {
                    throw new CStructPathException("Pointer accessors cannot have array indexes.");
                }

                if (string.Equals(next.Name, "address", StringComparison.Ordinal))
                {
                    if (valueIndex + 1 != walk.Segments.Count - 1)
                    {
                        throw new CStructPathException("Pointer .address must be the terminal path segment.");
                    }

                    return PointerAddress(ref state, RemainingLevels(ref state, walk, field, selection), target, selection, pointers);
                }

                if (!string.Equals(next.Name, "value", StringComparison.Ordinal))
                {
                    throw new CStructPathException("Expected another pointer accessor for a multi-level pointer.");
                }

                return ResolvePointer(ref cursor, ref state, walk, RemainingLevels(ref state, walk, field, selection), target, valueIndex + 1, selection, pointers);
            }

            if (field.TargetComposite is { } targetComposite)
            {
                TargetProgram program = walk.Programs.GetComposite(state.Layout.Compilation, targetComposite);
                return ResolveInStruct(ref cursor, ref state, walk, program, target, valueIndex + 1, pointers);
            }

            throw new CStructPathException("Cannot traverse beyond a scalar pointer target.");
        }
        finally
        {
            state.PointerDepth--;
            active?.Remove(key);
        }
    }

    /// <summary>Creates the target of a <c>.address</c> accessor: the pointer's own stored bits.</summary>
    /// <param name="state">The operation's state, whose nesting depth the target keeps.</param>
    /// <param name="field">The pointer at the level whose address is selected.</param>
    /// <param name="address">The stored address's first byte.</param>
    /// <param name="selection">The array selection of the pointer member.</param>
    /// <param name="pointers">The pointers the path followed.</param>
    /// <returns>The target.</returns>
    private static ResolvedPath PointerAddress(ref ReadEngineState state, CompiledField field, long address, Selection selection, PointerContext pointers)
        => new()
        {
            Kind = ResolvedTargetKind.PointerAddress,
            Address = address,
            Effective = field,
            TargetComposite = field.TargetComposite,
            RemainingPointerDepth = field.PointerDepth,
            IsArray = selection.IsArray,
            ArrayLength = selection.Length,
            SelectedArrayIndex = selection.Index,
            PointerAccessorsConsumed = pointers.Consumed,
            PointerTargetAddress = pointers.TargetAddress,
            ContainingStructureDepth = state.StructureDepth,
            Declared = selection.Declared,
            Indexes = selection.Indexes,
        };

    /// <summary>The view of a multi-level pointer after one <c>.value</c>: the same pointer with one level fewer.</summary>
    /// <param name="state">The operation's state, whose layout gives the pointer width.</param>
    /// <param name="walk">The resolution's programs, which share the view.</param>
    /// <param name="field">The pointer at the level just followed, with more than one level.</param>
    /// <param name="selection">The pointer member the path selected, which identifies the view.</param>
    /// <returns>The view.</returns>
    private static CompiledField RemainingLevels(ref ReadEngineState state, in PathWalk walk, CompiledField field, Selection selection)
        => walk.Programs.GetPointerView(selection.Declared, selection.Indexes, field, field.PointerDepth - 1, state.Layout.PointerSize);

    /// <summary>
    ///     Reads a pointer's stored address at <paramref name="storage"/> and resolves it: 0 stays the null pointer; a
    ///     relative address that overflows is a path failure; a target outside the input is a read failure.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor; it ends just after the stored address.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="storage">The stored address's first byte.</param>
    /// <returns>The target's position, or 0 for a null pointer.</returns>
    private static long ReadPointerTargetAddress<TCursor>(ref TCursor cursor, ref ReadEngineState state, long storage)
        where TCursor : struct, IReadCursor
    {
        cursor.Position = storage;
        Span<byte> scratch = stackalloc byte[ScratchSize];
        long stored = ReadEngine.ReadPointerAddress(ref cursor, ref state, scratch);
        if (stored == 0)
        {
            return 0;
        }

        long target;
        try
        {
            target = CStructPointerArithmetic.ResolveTargetAddress(stored, state.AddressingMode, state.PointerOrigin);
        }
        catch (OverflowException exception)
        {
            throw new CStructPathException("Relative pointer target overflowed the stream address range.", exception);
        }

        if (target < 0 || target >= cursor.Length)
        {
            throw new CStructReadException("Pointer target is outside the readable stream range: " + target);
        }

        return target;
    }

    /// <summary>
    ///     Returns whether a struct member is in the arms the data selects, evaluating each decision the first time this
    ///     struct instance needs it, outermost arm first; the first arm not selected ends the test.
    /// </summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The struct's walk.</param>
    /// <param name="member">The member.</param>
    /// <param name="arms">The struct instance's selected arms.</param>
    /// <returns>Whether the member is present.</returns>
    private static bool IsActive(ref ReadEngineState state, TargetProgram program, TargetMember member, Span<int> arms)
    {
        foreach (int branch in member.Branches)
        {
            ProgramBranch selected = program.Branches[branch];
            if (FrameArena.SelectedArm(arms, state.Slots, program.Groups, program.Expressions, program.ExpressionContexts, selected, ExpressionFailureDomain.Read) != selected.Arm)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Captures a member the walk passes into its slot by the shared rule (<see cref="LayoutVariableCapture"/>). An array
    ///     or fixed-point member, a struct or a member without a reader is not read: its capture only makes a shared name
    ///     unusable. Any other member's value is read from its start - a pointer's stored address, an enum's storage, a
    ///     bitfield's unit (its placed window when that differs from the declared type), or a value through its codec -
    ///     whether or not an expression reads it, because those bytes are charged; a bitfield then keeps its own bits.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="member">The member.</param>
    /// <param name="fieldStart">The member's first byte (its storage unit's, for a bitfield).</param>
    /// <param name="bitOffset">A bitfield's first bit within its unit.</param>
    /// <param name="unitSize">A bitfield's placed unit size in bytes.</param>
    /// <returns>
    ///     Whether the value was read through the member's codec, which leaves the cursor just after it: the walk then takes
    ///     the end of a value only reading can measure (<see cref="ReadsToMeasure"/>) from there instead of reading it again.
    /// </returns>
    private static bool Capture<TCursor>(ref TCursor cursor, ref ReadEngineState state, TargetMember member, long fieldStart, int bitOffset, int unitSize)
        where TCursor : struct, IReadCursor
    {
        CompiledField field = member.Field;
        if (field.Array.Kind != CompiledArrayKind.Scalar || (field.PointerDepth == 0 && field.IsFixedPoint))
        {
            CaptureValue(ref state, member, null);
            return false;
        }

        cursor.Position = fieldStart;
        Span<byte> scratch = stackalloc byte[ScratchSize];
        object? value;
        bool readThroughCodec = false;
        if (field.PointerDepth > 0)
        {
            value = ReadEngine.ReadPointerAddress(ref cursor, ref state, scratch);
        }
        else if (field.Enum is { } enm && field.BitSize == 0)
        {
            object storage = ReadEngine.ReadCodecValue(ref cursor, field.Codec, scratch);
            CaptureValue(ref state, member, enm.Integer.FromStorageValue(storage));
            return false;
        }
        else if (field.Composite is not null || field.CodecId < 0)
        {
            CaptureValue(ref state, member, null);
            return false;
        }
        else if (field.BitSize > 0 && unitSize != field.Codec.Size)
        {
            // A packed SysV window: the placed unit is read rather than the declared type.
            ReadOnlySpan<byte> unit = cursor.TryReadSpan(unitSize, out ReadOnlySpan<byte> direct) ? direct : ReadEngine.ReadExact(ref cursor, scratch[..unitSize]);
            value = BinaryPrimitiveIO.ReadUnsigned(unit, field.BitStorageIsLittleEndian ?? true);
        }
        else
        {
            value = ReadThroughCodec(ref cursor, ref state, field, scratch);
            readThroughCodec = true;
        }

        if (member.CaptureSlot < 0)
        {
            return readThroughCodec;
        }

        if (field.BitSize > 0)
        {
            // The field's own bits (an enum bitfield's enum value), decoded exactly as the reader decodes them.
            value = state.Layout.DecodeBitfield(field, value!, bitOffset, checked(unitSize * 8));
        }

        CaptureValue(ref state, member, value);
        return readThroughCodec;
    }

    /// <summary>
    ///     Stores a captured value in the member's slot and publishes it under the active qualified prefix: a member that is
    ///     not an integer makes the name unusable, a value with no integer meaning (or none read) removes it. A member no
    ///     expression can read captures nothing.
    /// </summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="member">The member.</param>
    /// <param name="value">The value read, or <see langword="null"/> when none was read.</param>
    private static void CaptureValue(ref ReadEngineState state, TargetMember member, object? value)
    {
        if (member.CaptureSlot < 0)
        {
            return;
        }

        SlotValue captured = member.NotANumber is { } unusable ? SlotValue.FromUnusable(unusable) : LayoutVariableCapture.ToSlotValue(value);
        state.Slots.Set(member.CaptureSlot, captured);
        state.PublishQualified(member.QualifiedTargets, captured);
    }

    /// <summary>
    ///     Reads one value of the field's codec: a caller's codec through its adapter (its value may be
    ///     <see langword="null"/>), any other codec through <see cref="ReadEngine.ReadCodecValue{TCursor}"/>.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the value.</param>
    /// <param name="state">The operation's state, whose layout holds the caller's codecs.</param>
    /// <param name="field">The field.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The value.</returns>
    private static object? ReadThroughCodec<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField field, Span<byte> scratch)
        where TCursor : struct, IReadCursor
        => field.Codec.IsCustom
               ? cursor.ReadCustom(state.Layout.Codecs.CustomCodecOf(field.CodecId))
               : ReadEngine.ReadCodecValue(ref cursor, field.Codec, scratch);

    /// <summary>Rejects a nesting depth the options do not allow.</summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="depth">The depth about to be used.</param>
    /// <exception cref="CStructReadLimitException">The depth exceeds the limit.</exception>
    private static void EnsureStructureDepth(ref ReadEngineState state, int depth)
    {
        if (depth > state.MaxNestingDepth)
        {
            throw new CStructReadLimitException(ReadFailures.NestingLimit);
        }
    }

    /// <summary>Whether a field is an array with a count: fixed, runtime-sized, to the end, or terminated (an unsized character array is text).</summary>
    /// <param name="field">The field.</param>
    /// <returns>Whether it is such an array.</returns>
    private static bool IsArray(CompiledField field)
        => field.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;

    /// <summary>
    ///     The debug path segment a debug read of the target records a member under, as a whole-root debug parse names it:
    ///     an element or row of a struct, union or composite-pointer array keeps the path's indexes (<c>items[1]</c>,
    ///     <c>grid[1][2]</c>), because every element of such an array has a path of its own; any other member is its name
    ///     alone, as the elements of a scalar array share their member's path.
    /// </summary>
    /// <param name="declared">The member's declared field.</param>
    /// <param name="segment">The path segment that names it, with the indexes it applies.</param>
    /// <returns>The segment.</returns>
    private static string DebugSegment(CompiledField declared, PathSegment segment)
    {
        if (segment.Indexes.Count == 0 || declared.TargetComposite is null)
        {
            return declared.Name;
        }

        var text = new System.Text.StringBuilder(declared.Name);
        foreach (int index in segment.Indexes)
        {
            text.Append('[').Append(index).Append(']');
        }

        return text.ToString();
    }

    /// <summary>Returns the walk of a member's struct or union, cached on the member.</summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's programs.</param>
    /// <param name="member">The member.</param>
    /// <param name="composite">The member's struct or union.</param>
    /// <returns>The walk.</returns>
    private static TargetProgram NestedProgram(ref ReadEngineState state, in PathWalk walk, TargetMember member, CompiledCompositeType composite)
        => member.Nested ??= walk.Programs.GetComposite(state.Layout.Compilation, composite);

    /// <summary>What one resolution walks: the path, the debug path it collects, the layout's programs, and whether the target is read next.</summary>
    /// <param name="Segments">The parsed path.</param>
    /// <param name="DebugPrefix">The list collecting the debug path's names, or <see langword="null"/>.</param>
    /// <param name="Programs">The layout's target programs.</param>
    /// <param name="ReadsTarget">Whether the caller reads the target's value next.</param>
    private readonly record struct PathWalk(IReadOnlyList<PathSegment> Segments, List<string>? DebugPrefix, TargetProgramCache Programs, bool ReadsTarget);

    /// <summary>The pointers a path followed so far.</summary>
    /// <param name="TargetAddress">The address the last followed pointer stored, or <see langword="null"/> before any.</param>
    /// <param name="Consumed">The number of <c>.value</c> accessors followed.</param>
    private readonly record struct PointerContext(long? TargetAddress, int Consumed);

    /// <summary>The array facts of a member the path selected, which a pointer target reached through it keeps.</summary>
    /// <param name="Declared">The member's declared field.</param>
    /// <param name="Indexes">The number of indexes applied to it.</param>
    /// <param name="IsArray">Whether the declared member is an array.</param>
    /// <param name="Length">The count of the dimension left unindexed, or <see langword="null"/>.</param>
    /// <param name="Index">The index that selected one element, or <see langword="null"/>.</param>
    private readonly record struct Selection(CompiledField Declared, int Indexes, bool IsArray, int? Length, int? Index);
}
