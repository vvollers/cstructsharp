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
///         <b>Order.</b> A member is captured before it is measured, so a terminated string ahead of the target is read
///         twice and charged twice. The target's own value is not read here; the caller reads it from the returned
///         address (<see cref="ReadEngine"/>).
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
internal static class TargetResolver
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
    /// <returns>The selected storage.</returns>
    /// <exception cref="CStructPathException">The path names nothing, indexes out of range, or traverses what it cannot.</exception>
    /// <exception cref="CStructException">A value the walk reads is short or invalid, or a limit is exceeded.</exception>
    public static ResolvedPath Resolve<TCursor>(ref TCursor cursor, ref ReadEngineState state, IReadOnlyList<PathSegment> segments, List<string>? debugPrefix)
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
        var walk = new PathWalk(segments, debugPrefix, compilation.SlotTable.TargetPrograms);
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
                                   ? ArrayCount(ref cursor, ref state, rootField!, walk.Programs.GetRootField(compilation, rootField!), rootStart)
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
        ReadConditionalScope? scope = program.Scope;
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

                Capture(ref cursor, ref state, member, fieldStart, bitOffset, unitSize);
                if (field.BitSize == 0)
                {
                    placer.CompleteField(MeasureFieldEnd(ref cursor, ref state, walk, member, fieldStart));
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
            int dimensionCount = ArrayCount(ref cursor, ref state, resolved, member, elementStart);
            if (index >= dimensionCount)
            {
                throw new CStructPathException($"Array index {index} is out of range for {segment.Name} with length {dimensionCount}.");
            }

            CompiledField element = walk.Programs.GetElementView(declared, ++peeled, resolved);
            elementStart = ElementStart(ref cursor, ref state, walk, member, resolved, element, elementStart, index);
            resolved = element;
        }

        bool remainingIsArray = IsArray(resolved);
        int? arrayLength = remainingIsArray ? ArrayCount(ref cursor, ref state, resolved, member, elementStart) : null;
        int? selectedIndex = segment.Indexes.Count > 0 && !remainingIsArray ? segment.Indexes[^1] : null;
        walk.DebugPrefix?.Add(declared.Name);
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
            ReadProgram.ConditionalBranch selected = program.Branches[branch];
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
    private static void Capture<TCursor>(ref TCursor cursor, ref ReadEngineState state, TargetMember member, long fieldStart, int bitOffset, int unitSize)
        where TCursor : struct, IReadCursor
    {
        CompiledField field = member.Field;
        if (field.Array.Kind != CompiledArrayKind.Scalar || (field.PointerDepth == 0 && field.IsFixedPoint))
        {
            CaptureValue(ref state, member, null);
            return;
        }

        cursor.Position = fieldStart;
        Span<byte> scratch = stackalloc byte[ScratchSize];
        object? value;
        if (field.PointerDepth > 0)
        {
            value = ReadEngine.ReadPointerAddress(ref cursor, ref state, scratch);
        }
        else if (field.Enum is { } enm && field.BitSize == 0)
        {
            object storage = ReadEngine.ReadCodecValue(ref cursor, field.Codec, scratch);
            CaptureValue(ref state, member, enm.Integer.FromStorageValue(storage));
            return;
        }
        else if (field.Composite is not null || field.CodecId < 0)
        {
            CaptureValue(ref state, member, null);
            return;
        }
        else if (field.BitSize > 0 && unitSize != field.Codec.Size)
        {
            // A packed SysV window: the placed unit is read rather than the declared type.
            ReadOnlySpan<byte> unit = cursor.TryReadSpan(unitSize, out ReadOnlySpan<byte> direct) ? direct : ReadExact(ref cursor, scratch[..unitSize]);
            value = BinaryPrimitiveIO.ReadUnsigned(unit, field.BitStorageIsLittleEndian ?? true);
        }
        else
        {
            value = ReadThroughCodec(ref cursor, ref state, field, scratch);
        }

        if (member.CaptureSlot < 0)
        {
            return;
        }

        if (field.BitSize > 0)
        {
            // The field's own bits (an enum bitfield's enum value), decoded exactly as the reader decodes them.
            value = state.Layout.DecodeBitfield(field, value!, bitOffset, checked(unitSize * 8));
        }

        CaptureValue(ref state, member, value);
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
    ///     Reads one value through the field's codec reader: a caller's codec through its adapter (its value may be
    ///     <see langword="null"/>), any other codec as its stream reader reads it.
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

    /// <summary>Fills <paramref name="bytes"/> through <see cref="IReadCursor.ReadExactly"/> and returns it.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="bytes">The span to fill.</param>
    /// <returns><paramref name="bytes"/>, filled.</returns>
    private static ReadOnlySpan<byte> ReadExact<TCursor>(ref TCursor cursor, Span<byte> bytes)
        where TCursor : struct, IReadCursor
    {
        cursor.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    ///     Measures where a member the walk passes ends, reading only what its size depends on: a struct (or each struct of
    ///     an array of them) member by member, capturing as it goes; terminated text, an unsized character array, LEB128 and a
    ///     variable-size caller's codec by reading each value; anything else from its count and element size, without
    ///     reading or charging its bytes. A terminated array's all-zero terminator element belongs to the member.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="member">The member.</param>
    /// <param name="fieldStart">The member's first byte.</param>
    /// <returns>The position just after the member.</returns>
    /// <exception cref="CStructException">A value measured is short or invalid, or a limit is exceeded.</exception>
    /// <exception cref="OverflowException">The member's extent does not fit a signed position.</exception>
    private static long MeasureFieldEnd<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetMember member, long fieldStart)
        where TCursor : struct, IReadCursor
    {
        CompiledField field = member.Field;

        // A pointer to a struct occupies the pointer width, never the pointee's extent, so pointers fall through to the
        // fixed-size arithmetic below.
        if (field.Composite is { } nested)
        {
            int count = field.Array.Kind == CompiledArrayKind.Scalar ? 1 : TotalCount(ref cursor, ref state, field, member, fieldStart);

            // Every element is walked, even when its size is fixed: a later member may count by a value inside one.
            long current = fieldStart;
            string? outerPrefix = state.QualifiedPrefix;
            if (field.HasQualifiedPrefix && field.Array.Kind == CompiledArrayKind.Scalar)
            {
                // A member named through a dotted path (`hdr.n`) republishes its members' values under the prefix.
                state.QualifiedPrefix = outerPrefix is null ? field.QualifiedPrefix : outerPrefix + field.QualifiedPrefix;
            }

            TargetProgram program = NestedProgram(ref state, walk, member, nested);
            if (field.IsPromotedComposite)
            {
                // A promoted member's members belong to the current struct's nesting level.
                cursor.ThrowIfCancellationRequested();
                current = MeasureCompositeEnd(ref cursor, ref state, walk, program, current);
            }
            else
            {
                for (int index = 0; index < count; index++)
                {
                    current = MeasureStructEnd(ref cursor, ref state, walk, program, current);
                }
            }

            state.QualifiedPrefix = outerPrefix;
            return field.Array.Kind == CompiledArrayKind.Terminated ? checked(current + (field.FixedElementSize ?? 0)) : current;
        }

        Span<byte> scratch = stackalloc byte[ScratchSize];
        if (field.Array.Kind == CompiledArrayKind.Scalar && field.Codec.IsTerminatedText)
        {
            cursor.Position = fieldStart;
            _ = ReadEngine.ReadCodecValue(ref cursor, field.Codec, scratch);
            return cursor.Position;
        }

        if (field.Array.Kind == CompiledArrayKind.Flexible)
        {
            if (!field.IsCharacterArray)
            {
                throw new CStructLayoutException("Only character fields can use an unsized array declarator: " + field.Name);
            }

            if (field.TerminatedCodecId < 0)
            {
                throw new InvalidOperationException("Compiled unsized character array has no reader: " + field.Name);
            }

            cursor.Position = fieldStart;
            _ = ReadEngine.ReadCodecValue(ref cursor, PrimitiveCodec.Resolve(field.DisplayTypeSpelling, field.LayoutLittleEndian), scratch);
            return cursor.Position;
        }

        if (ReadsToMeasure(member))
        {
            // Variable-length integers, terminated strings and variable-size caller codecs are measured by reading every
            // value.
            int count = field.Array.Kind == CompiledArrayKind.Scalar ? 1 : TotalCount(ref cursor, ref state, field, member, fieldStart);
            cursor.Position = fieldStart;
            for (int index = 0; index < count; index++)
            {
                _ = ReadThroughCodec(ref cursor, ref state, field, scratch);
            }

            return cursor.Position;
        }

        int scalarCount = field.Array.Kind == CompiledArrayKind.Scalar ? 1 : TotalCount(ref cursor, ref state, field, member, fieldStart);
        int elementSize = field.FixedElementSize ?? ElementSize(ref state, field);
        if (field.Array.Kind == CompiledArrayKind.Terminated)
        {
            // The terminator element is part of the field's extent.
            scalarCount = CountStoredTerminatedElements(scalarCount);
        }

        return checked(fieldStart + checked(elementSize * scalarCount));
    }

    /// <summary>
    ///     Whether a member's values are read to find where they end: LEB128, terminated text and a caller's codec without a
    ///     fixed size, whose size the data decides. A caller's codec with a fixed size occupies exactly that size.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>Whether each value is read.</returns>
    private static bool ReadsToMeasure(TargetMember member)
    {
        CompiledField field = member.Field;
        PrimitiveCodec codec = field.Codec;
        return codec.IsLeb128 || codec.IsTerminatedText || (codec.IsCustom && !field.FixedElementSize.HasValue);
    }

    /// <summary>Measures one struct or union inside one claimed nesting level (cancellation first, then the depth limit).</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The composite's walk.</param>
    /// <param name="start">Its first byte.</param>
    /// <returns>The position just after it, tail padding included.</returns>
    private static long MeasureStructEnd<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program, long start)
        where TCursor : struct, IReadCursor
    {
        state.EnterStructure(ref cursor);
        try
        {
            return MeasureCompositeEnd(ref cursor, ref state, walk, program, start);
        }
        finally
        {
            state.StructureDepth--;
        }
    }

    /// <summary>
    ///     Measures a struct or union whose level is claimed. A union's counts are checked against the limits and its size
    ///     taken from the variables, reading nothing; a struct's active members are placed, captured and measured in
    ///     declaration order under its conditional selection and scope, and its end padded to its alignment.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The composite's walk.</param>
    /// <param name="start">Its first byte.</param>
    /// <returns>The position just after it.</returns>
    private static long MeasureCompositeEnd<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program, long start)
        where TCursor : struct, IReadCursor
    {
        CompiledCompositeType composite = program.Composite;
        if (composite.IsUnion)
        {
            ValidateTraversalLimits(ref cursor, ref state, walk, program);
            int size = composite.Symbol.FixedSize ?? state.Layout.Compilation.SizeQueries.GetCompiledStructSizeInBytes(composite, state.Slots.ToDictionary(), false);
            return checked(start + size);
        }

        var placer = new PlacementCursor(start, state.Layout.Aligned, state.Layout.BitfieldPacking, state.Layout.Compilation.HighBitFirst);
        ReadConditionalScope? scope = program.Scope;
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

                Capture(ref cursor, ref state, member, fieldStart, bitOffset, unitSize);
                if (field.BitSize == 0)
                {
                    placer.CompleteField(MeasureFieldEnd(ref cursor, ref state, walk, member, fieldStart));
                }

                if (scope is not null)
                {
                    state.CompleteMember(scope, index, locals);
                }
            }

            return placer.Finish(composite.Symbol.Alignment)!.Value;
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
    ///     Checks the array and nesting work inside a union without reading its bytes: every fixed or runtime count of its
    ///     members (and of their structs' members, whatever arm they sit in) against the element limit, and every nested
    ///     struct against the depth limit. A data-sized array's count needs a position, so only its element type is checked.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, which carries the cancellation token.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The composite's walk.</param>
    private static void ValidateTraversalLimits<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program)
        where TCursor : struct, IReadCursor
    {
        foreach (TargetMember member in program.Members)
        {
            CompiledField field = member.Field;
            int count = field.Array.Kind is CompiledArrayKind.Scalar or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated
                            ? 1
                            : ArrayCount(ref cursor, ref state, field, member, 0);
            if (count == 0 || field.Composite is not { } nested)
            {
                continue;
            }

            TargetProgram nestedProgram = NestedProgram(ref state, walk, member, nested);
            if (field.IsPromotedComposite)
            {
                // A promoted member's members belong to this composite's level.
                ValidateTraversalLimits(ref cursor, ref state, walk, nestedProgram);
                continue;
            }

            state.EnterStructure(ref cursor);
            try
            {
                ValidateTraversalLimits(ref cursor, ref state, walk, nestedProgram);
            }
            finally
            {
                state.StructureDepth--;
            }
        }
    }

    /// <summary>
    ///     Returns where one element of an array (or row of a multidimensional one) starts: the array's start for index 0;
    ///     one multiplication when every element has a fixed size; otherwise the elements before it are measured - structs
    ///     member by member, variable-length values by reading them.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="member">The array member.</param>
    /// <param name="field">The array shape at the dimension indexed.</param>
    /// <param name="element">One element of <paramref name="field"/> (the shape one dimension deeper).</param>
    /// <param name="start">The array's (or row's) first byte.</param>
    /// <param name="index">The element's index, already checked against the count.</param>
    /// <returns>The element's first byte.</returns>
    private static long ElementStart<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetMember member, CompiledField field, CompiledField element, long start, int index)
        where TCursor : struct, IReadCursor
    {
        if (index == 0)
        {
            return start;
        }

        // A fixed element size makes the start one multiplication: resolving an element never continues to a later
        // sibling, and nothing inside a fixed-size element depends on a value captured in an earlier one.
        if (element.FixedStorageSize is int stride)
        {
            return checked(start + ((long)stride * index));
        }

        if (field.Composite is { } nested)
        {
            // One step at this dimension skips as many leaf structs as one element holds.
            TargetProgram program = NestedProgram(ref state, walk, member, nested);
            int leaves = checked(index * (element.Array.TotalFixedElementCount ?? 1));
            long current = start;
            for (int leaf = 0; leaf < leaves; leaf++)
            {
                current = MeasureStructEnd(ref cursor, ref state, walk, program, current);
            }

            return current;
        }

        if (ReadsToMeasure(member))
        {
            Span<byte> scratch = stackalloc byte[ScratchSize];
            cursor.Position = start;
            int values = checked(index * (element.Array.TotalFixedElementCount ?? 1));
            for (int value = 0; value < values; value++)
            {
                _ = ReadThroughCodec(ref cursor, ref state, field, scratch);
            }

            return cursor.Position;
        }

        return checked(start + ((long)ElementSize(ref state, field) * index));
    }

    /// <summary>
    ///     Returns the element count of an array's outermost dimension as the resolver checks it before an index or a length
    ///     is used: a data-sized array counted from the data (the position is restored), a runtime count evaluated (a
    ///     negative one fails), a fixed count taken from the shape, and any count past the element limit rejected.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array shape at the dimension counted.</param>
    /// <param name="member">The member the shape belongs to, whose count program a runtime count evaluates.</param>
    /// <param name="start">The array's first byte, where a data-sized array is counted from.</param>
    /// <returns>The count.</returns>
    /// <exception cref="CStructLayoutException">The array is an unsized character array, which has no count.</exception>
    /// <exception cref="CStructReadException">The count is negative, or a data-sized array does not fit the data.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds the element limit.</exception>
    private static int ArrayCount<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField field, TargetMember member, long start)
        where TCursor : struct, IReadCursor
    {
        if (field.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            return CountDataSized(ref cursor, ref state, field, start);
        }

        Int128 count = field.Array.Kind switch
        {
            CompiledArrayKind.Scalar => 1,
            CompiledArrayKind.Flexible => throw new CStructLayoutException("Flexible array has no fixed storage size: " + field.Name),
            CompiledArrayKind.Runtime => EvaluateCount(ref state, field, member),
            _ => field.Array.FixedCount!.Value,
        };
        return CheckLimit(ref state, count);
    }

    /// <summary>
    ///     Returns the number of elements a whole array holds, every dimension together, as a walk over all of them visits
    ///     them: a data-sized array counted from the data, a runtime count evaluated, a fixed shape's product; checked against
    ///     the element limit.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array member.</param>
    /// <param name="member">The member, whose count program a runtime count evaluates.</param>
    /// <param name="start">The array's first byte.</param>
    /// <returns>The total count.</returns>
    private static int TotalCount<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField field, TargetMember member, long start)
        where TCursor : struct, IReadCursor
    {
        if (field.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            return CountDataSized(ref cursor, ref state, field, start);
        }

        Int128 count = field.Array.Kind switch
        {
            CompiledArrayKind.Scalar => 1,
            CompiledArrayKind.Flexible => throw new CStructLayoutException("Flexible array has no fixed storage size: " + field.Name),
            CompiledArrayKind.Runtime => EvaluateCount(ref state, field, member),
            _ => FixedTotal(field),
        };
        return CheckLimit(ref state, count);
    }

    /// <summary>The product of a fixed array's dimensions, exact in the expression domain, so a total past <see cref="int"/> fails the element limit naming its value.</summary>
    /// <param name="field">The fixed array.</param>
    /// <returns>The total.</returns>
    private static Int128 FixedTotal(CompiledField field)
    {
        Int128 total = 1;
        foreach (CompiledArrayDimension dimension in field.Array.Dimensions)
        {
            total = checked(total * dimension.FixedCount!.Value);
        }

        return total;
    }

    /// <summary>Evaluates a runtime-sized array's count against the slots; a negative count is a read failure naming the array.</summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array.</param>
    /// <param name="member">The member whose count program is evaluated.</param>
    /// <returns>The count, in the expression domain.</returns>
    private static Int128 EvaluateCount(ref ReadEngineState state, CompiledField field, TargetMember member)
    {
        Int128 count = state.Slots.Evaluate(member.Count!, member.CountContext, ExpressionFailureDomain.Read);
        return count < 0 ? throw new CStructReadException(LayoutFailures.NegativeArrayLength(field.Name)) : count;
    }

    /// <summary>Rejects a count past the element limit, naming its exact value.</summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="count">The count.</param>
    /// <returns>The count as an <see cref="int"/>.</returns>
    /// <exception cref="CStructReadLimitException">The count exceeds the limit.</exception>
    private static int CheckLimit(ref ReadEngineState state, Int128 count)
        => count > state.MaxArrayElements ? throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(count, state.MaxArrayElements)) : (int)count;

    /// <summary>
    ///     Counts a data-sized array from its first byte - whole elements to the end of the input, or elements before the
    ///     first all-zero one (read and charged) - and restores the position the walk was at, whatever happens.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array.</param>
    /// <param name="start">The array's first byte.</param>
    /// <returns>The element count, without the terminator.</returns>
    private static int CountDataSized<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField field, long start)
        where TCursor : struct, IReadCursor
    {
        int elementSize = field.FixedElementSize ?? throw new InvalidOperationException("Data-sized array has no fixed element size: " + field.Name);
        long position = cursor.Position;
        try
        {
            return field.Array.Kind == CompiledArrayKind.ToEnd
                       ? DynamicArrayExtent.CountToEnd(ref cursor, start, elementSize, state.MaxArrayElements, field.Name)
                       : DynamicArrayExtent.CountTerminated(ref cursor, start, elementSize, state.MaxArrayElements, field.Name);
        }
        finally
        {
            cursor.Position = position;
        }
    }

    /// <summary>The size of one element of a field without a fixed element size: a runtime-sized struct measured from the variables.</summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The field.</param>
    /// <returns>The element size in bytes.</returns>
    /// <exception cref="CStructLayoutException">The element type has no storage size.</exception>
    private static int ElementSize(ref ReadEngineState state, CompiledField field)
        => field.FixedElementSize ?? state.Layout.Compilation.SizeQueries.GetCompiledFieldElementSize(field, state.Slots.ToDictionary(), false);

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

    /// <summary>Returns the walk of a member's struct or union, cached on the member.</summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's programs.</param>
    /// <param name="member">The member.</param>
    /// <param name="composite">The member's struct or union.</param>
    /// <returns>The walk.</returns>
    private static TargetProgram NestedProgram(ref ReadEngineState state, in PathWalk walk, TargetMember member, CompiledCompositeType composite)
        => member.Nested ??= walk.Programs.GetComposite(state.Layout.Compilation, composite);

    /// <summary>What one resolution walks: the path, the debug path it collects, and the layout's programs.</summary>
    /// <param name="Segments">The parsed path.</param>
    /// <param name="DebugPrefix">The list collecting the debug path's names, or <see langword="null"/>.</param>
    /// <param name="Programs">The layout's target programs.</param>
    private readonly record struct PathWalk(IReadOnlyList<PathSegment> Segments, List<string>? DebugPrefix, TargetProgramCache Programs);

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
