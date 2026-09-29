namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Translates a compiled struct (or a root) into a <see cref="ReadProgram"/> that reads exactly what the
///     interpreter's reader reads, in the same order, with the same checks - or records why it cannot yet
///     (<see cref="ReadProgramOutcome.Reason"/>), naming the innermost struct and member that needs a later engine stage.
/// </summary>
/// <remarks>
///     <para>Per member, in the interpreter's order (engine plan section 10.1):</para>
///     <list type="number">
///         <item>one <see cref="ReadOpCode.SelectArm"/> per <c>if</c>/<c>switch</c> arm the member sits in, outermost first;</item>
///         <item>the element count: a fixed count is checked against the array limit, a data count is evaluated;</item>
///         <item>placement (<see cref="ReadPlacement"/>) and, where the build could not check it, the <c>@N</c> assertion;</item>
///         <item>the read, specialized by value kind;</item>
///         <item>the layout-variable capture and its qualified publication, exactly where the interpreter captures;</item>
///         <item>for a composite with conditional members, the scope step that saves and restores its names.</item>
///     </list>
///     <para>
///         The composite ends with <see cref="ReadOpCode.FinishComposite"/>. A named nested struct refers to its own cached
///         program; an anonymous promoted member is compiled into the program of the nearest named struct around it, because
///         its values go into that struct's value.
///     </para>
///     <para>
///         A data-sized array is counted from the input after it is placed (<see cref="ReadOpCode.CountToEnd"/>,
///         <see cref="ReadOpCode.CountTerminated"/>); a multidimensional one is read as its flat elements and then nested. A
///         caller's codec (and any struct holding one) ends where the codec says, whatever size it declares, so the position
///         after it is a new anchor.
///     </para>
///     <para>
///         A struct with bitfields places every member through a runtime <see cref="PlacementCursor"/>, the one the
///         interpreter uses, because the layout's packing rule decides which bitfields share a storage unit. A union is a
///         program of member views, each read from the union's first byte with the variables it was entered with.
///     </para>
///     <para>
///         A pointer inside a struct is deferred where the interpreter defers it: its address is read in place and its
///         target followed after the struct's last member (<see cref="ReadOpCode.FollowPendingPointers"/>). A pointer's
///         struct or union target is not compiled with the program that points to it (a linked list points to itself); a
///         root is eligible only when every composite its pointers can reach compiled.
///     </para>
///     <para>
///         A member the engine cannot read gives a reason instead of a program. A compiler is used for one request on one
///         thread.
///     </para>
/// </remarks>
internal sealed class ReadProgramCompiler
{
    /// <summary>The reason for an array of bitfields, which the interpreter has no reader for either.</summary>
    public const string BitfieldArrays = "a bitfield array has no reader";

    /// <summary>The reason for a field the catalog has no reader for; the interpreter fails such a read.</summary>
    public const string NoReader = "the field has no codec reader";

    /// <summary>The reason for a member whose name the value shape does not hold.</summary>
    public const string NoShapeSlot = "the value shape has no slot for the member";

    /// <summary>The reason for a member whose static offset differs from the offset the layout compiled.</summary>
    public const string PlacementMismatch = "the static placement differs from the compiled offset";

    /// <summary>The reason for a declaration kind that is not a readable root.</summary>
    public const string UnreadableRoot = "the declaration is not a readable root";

    /// <summary>
    ///     The start of the reason for an expression that names an identifier the layout's slot table does not hold. Only a
    ///     type-spelling root (<c>uint8[N]</c>), registered after the table was built, can: a caller's value of that name
    ///     would have no slot to live in.
    /// </summary>
    public const string UnslottedName = "the expression names an identifier without a slot: ";

    private readonly LayoutCompilation compilation;
    private readonly ReadProgramCache cache;
    private readonly MemberExtents extents;

    /// <summary>Creates a compiler for one request.</summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="cache">The layout's program cache, which supplies nested structs' programs and qualified targets.</param>
    public ReadProgramCompiler(LayoutCompilation compilation, ReadProgramCache cache)
    {
        this.compilation = compilation;
        this.cache = cache;
        this.extents = new MemberExtents(compilation);
    }

    /// <summary>Compiles a struct read into a value of its own.</summary>
    /// <param name="composite">The composite.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    public ReadProgramOutcome CompileComposite(CompiledCompositeType composite)
        => composite.IsUnion ? this.CompileUnion(composite) : this.CompileStruct(composite, ReadProgramKind.Composite, composite.Shape);

    /// <summary>
    ///     Compiles a root, as the interpreter's root dispatch reads it: a struct (or a typedef of an inline struct, stored
    ///     under the typedef's name), a typedef or enum field read standalone, or a <c>#define</c> evaluated for its failures.
    /// </summary>
    /// <param name="rootName">The name the root is requested by.</param>
    /// <param name="declaration">The root's declaration.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    public ReadProgramOutcome CompileRoot(string rootName, CStructElement declaration)
    {
        StructShape rootShape = this.compilation.ModelQueries.GetRootShape(rootName);
        switch (declaration)
        {
        case Struct strct:
            return this.CompileRootStruct(rootName, strct.Name.Name, this.compilation.SizeQueries.GetCompiledComposite(strct), rootShape);
        case Typedef { Struct: { } inline, } typedef:
            return this.CompileRootStruct(rootName, typedef.Name.Name, this.compilation.SizeQueries.GetCompiledComposite(inline), rootShape);
        case Typedef:
        case CstructEnum:
            {
                CompiledField field = this.compilation.ModelQueries.GetCompiledRootField(declaration);
                var builder = new ReadProgramBuilder(this.cache.Table, [field], rootShape, 0);
                var unplaced = new ReadPlacement(false);
                if (this.EmitMember(builder, 0, rootName, standalone: true, ref unplaced) is { } reason)
                {
                    return ReadProgramOutcome.NotSupported(reason);
                }

                return this.CheckPointerTargets(builder.Build(ReadProgramKind.Root, rootName, null));
            }

        case Defines definition:
            {
                var builder = new ReadProgramBuilder(this.cache.Table, [], rootShape, 0);
                int value = builder.AddExpression(definition.Value, "definition " + definition.Name.Name);
                int slot = this.cache.Table.TryGetSlot(definition.Name.Name, out int found) ? found : -1;
                builder.Emit(ReadOpCode.EvaluateDefinition, -1, value, slot);
                return ReadProgramOutcome.Eligible(builder.Build(ReadProgramKind.Root, rootName, null));
            }

        default:
            return ReadProgramOutcome.NotSupported(rootName + ": " + UnreadableRoot);
        }
    }

    /// <summary>Formats a reason with the struct and member it concerns.</summary>
    /// <param name="location">The struct (or root) name.</param>
    /// <param name="field">The member.</param>
    /// <param name="what">What is not supported.</param>
    /// <returns>The reason, <c>struct.member: what</c>.</returns>
    internal static string Refuse(string location, CompiledField field, string what)
        => location + "." + (field.Name.Length > 0 ? field.Name : field.IsPromotedComposite ? "(anonymous)" : "(unnamed)") + ": " + what;

    /// <summary>The location a struct's reasons name: its name, or a marker for an anonymous one.</summary>
    /// <param name="composite">The composite.</param>
    /// <returns>The name.</returns>
    internal static string Locate(CompiledCompositeType composite)
        => composite.Name.Length > 0 ? composite.Name : composite.IsUnion ? "(anonymous union)" : "(anonymous struct)";

    /// <summary>
    ///     The step that reads one scalar of a codec, or <see langword="null"/> for a codec the engine does not read
    ///     (none, custom).
    /// </summary>
    /// <param name="codec">The scalar's codec.</param>
    /// <returns>The operation.</returns>
    private static ReadOpCode? ScalarOp(PrimitiveCodec codec)
    {
        bool little = codec.LittleEndian;
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => ReadOpCode.ReadUInt8,
            PrimitiveCodecKind.Int8 => ReadOpCode.ReadInt8,
            PrimitiveCodecKind.Bool => ReadOpCode.ReadBool,
            PrimitiveCodecKind.Int16 => little ? ReadOpCode.ReadInt16Le : ReadOpCode.ReadInt16Be,
            PrimitiveCodecKind.UInt16 => little ? ReadOpCode.ReadUInt16Le : ReadOpCode.ReadUInt16Be,
            PrimitiveCodecKind.Int24 => little ? ReadOpCode.ReadInt24Le : ReadOpCode.ReadInt24Be,
            PrimitiveCodecKind.UInt24 => little ? ReadOpCode.ReadUInt24Le : ReadOpCode.ReadUInt24Be,
            PrimitiveCodecKind.Int32 => little ? ReadOpCode.ReadInt32Le : ReadOpCode.ReadInt32Be,
            PrimitiveCodecKind.UInt32 => little ? ReadOpCode.ReadUInt32Le : ReadOpCode.ReadUInt32Be,
            PrimitiveCodecKind.Int64 => little ? ReadOpCode.ReadInt64Le : ReadOpCode.ReadInt64Be,
            PrimitiveCodecKind.UInt64 => little ? ReadOpCode.ReadUInt64Le : ReadOpCode.ReadUInt64Be,
            PrimitiveCodecKind.Float32 => little ? ReadOpCode.ReadFloat32Le : ReadOpCode.ReadFloat32Be,
            PrimitiveCodecKind.Float64 => little ? ReadOpCode.ReadFloat64Le : ReadOpCode.ReadFloat64Be,
            PrimitiveCodecKind.Int48 => ReadOpCode.ReadInt48,
            PrimitiveCodecKind.UInt48 => ReadOpCode.ReadUInt48,
            PrimitiveCodecKind.Int128 => ReadOpCode.ReadInt128,
            PrimitiveCodecKind.UInt128 => ReadOpCode.ReadUInt128,
            PrimitiveCodecKind.Float16 => ReadOpCode.ReadFloat16,
            PrimitiveCodecKind.Fixed16_16 or PrimitiveCodecKind.UFixed16_16 or PrimitiveCodecKind.Fixed2_30 or PrimitiveCodecKind.UFixed8_8 => ReadOpCode.ReadFixedPoint,
            PrimitiveCodecKind.Uuid or PrimitiveCodecKind.Guid => ReadOpCode.ReadIdentifier,
            PrimitiveCodecKind.ULeb128_32 or PrimitiveCodecKind.ULeb128_64 or PrimitiveCodecKind.SLeb128_32 or PrimitiveCodecKind.SLeb128_64 => ReadOpCode.ReadLeb128,
            PrimitiveCodecKind.Char or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or
                PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit => ReadOpCode.ReadCharacter,
            PrimitiveCodecKind.WChar => ReadOpCode.ReadWideCharacter,
            PrimitiveCodecKind.TerminatedAscii or PrimitiveCodecKind.TerminatedUtf8 or PrimitiveCodecKind.TerminatedUtf16 => ReadOpCode.ReadTerminatedText,
            _ => null,
        };
    }

    /// <summary>Emits the step that nests a multidimensional array's flat elements by its dimensions.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="table">Whether the array has more than one dimension; otherwise nothing is emitted.</param>
    private static void EmitReshape(ReadProgramBuilder builder, int index, bool table)
    {
        if (table)
        {
            builder.Emit(ReadOpCode.ReshapeTable, index, 0, 0);
        }
    }

    /// <summary>Compiles a root that reads one struct or union into a new value under <paramref name="key"/>.</summary>
    /// <param name="rootName">The name the root is requested by.</param>
    /// <param name="key">The name the value is stored under (a typedef's name for a typedef of an inline struct or union).</param>
    /// <param name="composite">The struct or union.</param>
    /// <param name="rootShape">The one-member root shape.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    private ReadProgramOutcome CompileRootStruct(string rootName, string key, CompiledCompositeType composite, StructShape rootShape)
    {
        ReadProgramOutcome nested = this.cache.GetComposite(this.compilation, composite);
        if (nested.Program is not { } program)
        {
            return nested;
        }

        if (!rootShape.TryGetIndex(key, out _))
        {
            return ReadProgramOutcome.NotSupported(rootName + ": " + NoShapeSlot);
        }

        var builder = new ReadProgramBuilder(this.cache.Table, [], rootShape, 0);
        builder.Emit(composite.IsUnion ? ReadOpCode.ReadRootUnion : ReadOpCode.ReadRootStruct, -1, builder.AddNested(program), -1);
        return this.CheckPointerTargets(builder.Build(ReadProgramKind.Root, key, null));
    }

    /// <summary>
    ///     Makes a root eligible only when every struct or union its pointers can reach - through nested programs and
    ///     through the targets' own pointers - has a program: each is compiled on first request, and the first one that
    ///     cannot be read gives the root its reason.
    /// </summary>
    /// <param name="root">The root's program.</param>
    /// <returns>The root's outcome.</returns>
    private ReadProgramOutcome CheckPointerTargets(ReadProgram root)
    {
        var visited = new HashSet<ReadProgram>(ReferenceEqualityComparer.Instance) { root, };
        var pending = new Stack<ReadProgram>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ReadProgram program = pending.Pop();
            foreach (ReadProgram nested in program.Nested)
            {
                if (visited.Add(nested))
                {
                    pending.Push(nested);
                }
            }

            foreach (ReadPointerTarget target in program.PointerTargets)
            {
                if (target.Composite is not { } composite)
                {
                    continue;
                }

                ReadProgramOutcome outcome = this.cache.GetComposite(this.compilation, composite);
                if (outcome.Program is not { } reached)
                {
                    return outcome;
                }

                if (visited.Add(reached))
                {
                    pending.Push(reached);
                }
            }
        }

        return ReadProgramOutcome.Eligible(root);
    }

    /// <summary>
    ///     Compiles a union's member views: before each member every variable is restored to the union's entry values, and
    ///     each member is read from the union's first byte as a standalone field (no alignment, no block paths), as the
    ///     interpreter reads them. Conditions on a union's own members are not evaluated there, so none are here.
    /// </summary>
    /// <param name="union">The union.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    private ReadProgramOutcome CompileUnion(CompiledCompositeType union)
    {
        string location = Locate(union);
        CompiledField[] fields = [.. union.Fields];
        var builder = new ReadProgramBuilder(this.cache.Table, fields, union.Shape, 0) { UnionMembers = true, };
        var unplaced = new ReadPlacement(false);
        for (int index = 0; index < fields.Length; index++)
        {
            builder.Emit(ReadOpCode.RestoreUnionSlots, -1, 0, 0);
            if (this.EmitMember(builder, index, location, standalone: true, ref unplaced) is { } reason)
            {
                return ReadProgramOutcome.NotSupported(reason);
            }
        }

        return ReadProgramOutcome.Eligible(builder.Build(ReadProgramKind.Union, union.Name, union));
    }

    /// <summary>Compiles a struct's members into a program.</summary>
    /// <param name="composite">The struct.</param>
    /// <param name="kind">Whether it has a value of its own or is promoted into its parent's.</param>
    /// <param name="shape">The layout of the value its members are stored into.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    private ReadProgramOutcome CompileStruct(CompiledCompositeType composite, ReadProgramKind kind, StructShape shape)
    {
        string location = Locate(composite);
        CompiledField[] fields = [.. composite.Fields];

        // Bitfields share storage units by the layout's packing rule, which only the runtime placement cursor applies.
        var builder = new ReadProgramBuilder(this.cache.Table, fields, shape, composite.ConditionalGroupCount)
        {
            UsesPlacementCursor = System.Array.Exists(fields, field => field.BitSize > 0 || field.IsZeroWidthBitfield),
        };
        if (composite.ConditionalScope is { } scope)
        {
            // The interpreter removes the kept names at entry; only the ones an expression can read matter.
            builder.Scope = new ReadConditionalScope(scope, this.cache.Table);
            if (builder.Scope.ClearedSlots.Length > 0)
            {
                builder.Emit(ReadOpCode.EnterConditionalScope, -1, 0, 0);
            }
        }

        var placement = new ReadPlacement(this.compilation.Aligned);
        var selections = new List<int>();
        for (int index = 0; index < fields.Length; index++)
        {
            CompiledField field = fields[index];

            // An unselected member is skipped whole: no placement, no read, no scope step, as the interpreter's loop
            // continues before any of them.
            selections.Clear();
            foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
            {
                selections.Add(builder.Emit(ReadOpCode.SelectArm, -1, builder.AddBranch(branch), -1));
            }

            ReadPlacement before = placement;
            if (this.EmitMember(builder, index, location, standalone: false, ref placement) is { } reason)
            {
                return ReadProgramOutcome.NotSupported(reason);
            }

            if (builder.Scope is { } mapped && mapped.HasEffect(index))
            {
                builder.Emit(ReadOpCode.CompleteMember, index, 0, 0);
            }

            if (field.IsConditional)
            {
                builder.PatchSkipTargets(selections);
                placement = ReadPlacement.Merge(before, placement);
            }
        }

        int alignment = composite.Symbol.Alignment;

        // A named struct follows the pointers it and its promoted members deferred once every member is read.
        if (kind == ReadProgramKind.Composite && builder.DefersPointers)
        {
            builder.Emit(ReadOpCode.FollowPendingPointers, -1, 0, 0);
        }

        if (builder.UsesPlacementCursor)
        {
            builder.Emit(ReadOpCode.FinishPlaced, -1, 0, alignment);
            return ReadProgramOutcome.Eligible(builder.Build(kind, composite.Name, composite));
        }

        bool knownTail = placement.TryFinish(alignment, out int padding);
        if (knownTail && placement.KnownOffset is long end && composite.Symbol.FixedSize is int size && end + padding != size)
        {
            return ReadProgramOutcome.NotSupported(location + ": " + PlacementMismatch);
        }

        builder.Emit(ReadOpCode.FinishComposite, -1, knownTail ? padding : -1, alignment);
        return ReadProgramOutcome.Eligible(builder.Build(kind, composite.Name, composite));
    }

    /// <summary>
    ///     Emits one member's count, placement, read and captures, or returns why the member cannot be read yet: a
    ///     reason located at this member, or a nested struct's own reason unchanged.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member: a root field or a union member view.</param>
    /// <param name="placement">The placement state; advanced past the member (a standalone field leaves it alone).</param>
    /// <returns>A reason, or <see langword="null"/> when the member was emitted.</returns>
    private string? EmitMember(ReadProgramBuilder builder, int index, string location, bool standalone, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        if (field.IsZeroWidthBitfield)
        {
            // A separator reads nothing; in a union it does not even move the position (the next member rewinds).
            if (!standalone)
            {
                builder.Emit(ReadOpCode.PlaceSeparator, index, 0, 0);
            }

            return null;
        }

        if (field.BitSize > 0)
        {
            return this.EmitBitfield(builder, index, location, standalone);
        }

        bool promoted = field.IsPromotedComposite;
        if (field.Name.Length > 0 && !promoted && !builder.SetShapeSlot(index))
        {
            return Refuse(location, field, NoShapeSlot);
        }

        if (field.Declaration is Struct)
        {
            return this.EmitInlineComposite(builder, index, location, promoted, ref placement);
        }

        switch (field.Array.Kind)
        {
        case CompiledArrayKind.Fixed:
            // A multidimensional array is read as all its elements, so the limit applies to their total.
            builder.Emit(ReadOpCode.CheckFixedCount, index, field.Array.TotalFixedElementCount!.Value, 0);
            break;
        case CompiledArrayKind.Runtime:
            if (this.FirstUnslottedName(field.Array.CountExpression!) is { } unslotted)
            {
                return Refuse(location, field, UnslottedName + unslotted);
            }

            builder.Emit(ReadOpCode.EvaluateCount, index, builder.AddExpression(field.Array.CountExpression!, "array length for " + field.Name), 0);
            break;
        }

        if (this.EmitMemberPlacement(builder, index, standalone, ref placement) is { } misplaced)
        {
            return Refuse(location, field, misplaced);
        }

        // A data-sized array is counted from its placed start; the layout requires its elements to have a fixed size.
        switch (field.Array.Kind)
        {
        case CompiledArrayKind.ToEnd:
            builder.Emit(ReadOpCode.CountToEnd, index, field.FixedElementSize!.Value, 0);
            break;
        case CompiledArrayKind.Terminated:
            builder.Emit(ReadOpCode.CountTerminated, index, field.FixedElementSize!.Value, 0);
            break;
        }

        if (this.EmitRead(builder, index, location, standalone) is { } refusal)
        {
            return refusal;
        }

        // The terminator follows the elements. Its skip comes after the array's read step, which also gives the array its
        // final shape, because no terminated array has character elements whose text is validated there: an unsized
        // character array is terminated text (CompiledArrayKind.Flexible).
        if (field.Array.Kind == CompiledArrayKind.Terminated)
        {
            builder.Emit(ReadOpCode.SkipTerminator, index, field.FixedElementSize!.Value, 0);
        }

        this.EmitCompletion(builder, index, standalone, ref placement);
        this.EmitCapture(builder, index);
        return null;
    }

    /// <summary>
    ///     Emits a bitfield: its placement (the runtime cursor's storage unit in a struct, or a unit of its declared size at
    ///     bit 0 in a union), the unit read with the bit extraction, and the capture. A bitfield never completes its
    ///     placement: the cursor reserved the whole unit when it opened it.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitBitfield(ReadProgramBuilder builder, int index, string location, bool standalone)
    {
        CompiledField field = builder.Fields[index];
        if (field.Array.Kind != CompiledArrayKind.Scalar)
        {
            return Refuse(location, field, BitfieldArrays);
        }

        if (field.Name.Length > 0 && !builder.SetShapeSlot(index))
        {
            return Refuse(location, field, NoShapeSlot);
        }

        if (field.CodecId < 0)
        {
            return Refuse(location, field, NoReader);
        }

        if (standalone)
        {
            if (builder.UnionMembers)
            {
                builder.Emit(ReadOpCode.RewindToUnionStart, index, 0, 0);
            }

            builder.Emit(ReadOpCode.OpenBitfieldUnit, index, 0, 0);
        }
        else
        {
            builder.Emit(ReadOpCode.PlaceBitfield, index, 0, 0);
        }

        builder.Emit(ReadOpCode.ReadBitfield, index, builder.AddCodec(field.CodecId, field.Codec), 0);
        this.EmitCapture(builder, index);
        return null;
    }

    /// <summary>
    ///     Emits where a member starts: a union member view moves back to the union's first byte, a member of a struct with
    ///     bitfields is placed by the runtime cursor, and any other placed member by the steps its static placement needs;
    ///     a root field is not placed.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="standalone">Whether no composite places the member.</param>
    /// <param name="placement">The static placement state.</param>
    /// <returns>A reason when a static placement contradicts the compiled offset; otherwise <see langword="null"/>.</returns>
    private string? EmitMemberPlacement(ReadProgramBuilder builder, int index, bool standalone, ref ReadPlacement placement)
    {
        if (builder.UnionMembers)
        {
            builder.Emit(ReadOpCode.RewindToUnionStart, index, 0, 0);
            return null;
        }

        if (standalone)
        {
            return null;
        }

        if (builder.UsesPlacementCursor)
        {
            builder.Emit(ReadOpCode.PlaceMember, index, 0, 0);
            return null;
        }

        return this.EmitPlacement(builder, index, ref placement);
    }

    /// <summary>
    ///     Records where a placed member ended: the runtime cursor learns the position after its read, and a static
    ///     placement advances past its size (or restarts after a size the data decides).
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="standalone">Whether no composite places the member; nothing is recorded then.</param>
    /// <param name="placement">The static placement state.</param>
    private void EmitCompletion(ReadProgramBuilder builder, int index, bool standalone, ref ReadPlacement placement)
    {
        if (standalone)
        {
            return;
        }

        if (builder.UsesPlacementCursor)
        {
            builder.Emit(ReadOpCode.CompletePlacement, index, 0, 0);
        }
        else
        {
            this.extents.AdvancePast(ref placement, builder.Fields[index]);
        }
    }

    /// <summary>Returns the first identifier an expression names that has no slot in the layout's table, if any.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The name, or <see langword="null"/> when every name has a slot.</returns>
    private string? FirstUnslottedName(Expr expression)
    {
        foreach (string name in this.cache.Table.Evaluator.GetDependencies(expression))
        {
            if (!this.cache.Table.TryGetSlot(name, out _))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    ///     Emits an inline struct member (<c>struct { ... } name;</c> or an anonymous promoted one): placed like any
    ///     member, never an array, and read by its own program.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="promoted">Whether the member is anonymous and promoted.</param>
    /// <param name="placement">The placement state.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitInlineComposite(ReadProgramBuilder builder, int index, string location, bool promoted, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        CompiledCompositeType composite = field.Composite ?? this.compilation.SizeQueries.GetCompiledComposite((Struct)field.Declaration);
        if (this.EmitMemberPlacement(builder, index, standalone: false, ref placement) is { } misplaced)
        {
            return Refuse(location, field, misplaced);
        }

        // An anonymous struct is compiled into this program's value; an anonymous union keeps its own cached program,
        // whose views are copied into this value after it is read.
        ReadProgramOutcome nested = promoted && !composite.IsUnion
                                        ? this.CompileStruct(composite, ReadProgramKind.Promoted, builder.Shape)
                                        : this.cache.GetComposite(this.compilation, composite);
        if (nested.Program is not { } program)
        {
            return nested.Reason;
        }

        // A promoted struct's deferred pointers are followed by this struct.
        builder.DefersPointers |= promoted && program.DefersPointers;
        int prefix = field.HasQualifiedPrefix ? builder.AddPrefix(field.QualifiedPrefix!) : -1;
        builder.Emit(
            (promoted, composite.IsUnion) switch
            {
                (true, true) => ReadOpCode.ReadPromotedUnion,
                (true, false) => ReadOpCode.ReadPromotedStruct,
                (false, true) => ReadOpCode.ReadUnion,
                _ => ReadOpCode.ReadStruct,
            },
            index,
            builder.AddNested(program),
            promoted ? -1 : prefix);
        if (!builder.UnionMembers)
        {
            this.EmitCompletion(builder, index, standalone: false, ref placement);
        }

        return null;
    }

    /// <summary>
    ///     Emits a member's placement and, when the layout's build could not check it, its <c>@N</c> offset assertion (a
    ///     statically known offset that satisfies it needs no step).
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="placement">The placement state.</param>
    /// <returns>
    ///     A reason when a statically known placement contradicts the compiled offset; otherwise <see langword="null"/>. A
    ///     placement the data decides (after a caller's codec, whose bytes may end before its declared size) is aligned at
    ///     run time from the struct's start, as the interpreter places it, whatever offset the layout compiled.
    /// </returns>
    private string? EmitPlacement(ReadProgramBuilder builder, int index, ref ReadPlacement placement)
    {
        bool contradicts = placement.PlaceMember(index, builder.Fields[index], out ReadStep? step, out int? asserted);
        if (step is { } move)
        {
            builder.Emit(move);
        }

        if (contradicts)
        {
            return PlacementMismatch;
        }

        if (asserted is int offset)
        {
            builder.Emit(ReadOpCode.CheckOffset, index, offset, 0);
        }

        return null;
    }

    /// <summary>Emits a member's read step, chosen by its value kind.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member.</param>
    /// <returns>A reason located at this member, a nested struct's own reason, or <see langword="null"/>.</returns>
    private string? EmitRead(ReadProgramBuilder builder, int index, string location, bool standalone)
    {
        CompiledField field = builder.Fields[index];
        if (field.PointerDepth > 0)
        {
            return this.EmitPointerRead(builder, index, location, standalone);
        }

        int codec = builder.AddCodec(field.CodecId, field.Codec);
        switch (field.Array.Kind)
        {
        case CompiledArrayKind.Flexible:
            {
                // An unsized character array reads through its terminated codec, as a terminated string scalar does.
                if (field.TerminatedCodecId < 0)
                {
                    return Refuse(location, field, NoReader);
                }

                PrimitiveCodec terminated = PrimitiveCodec.Resolve(field.DisplayTypeSpelling, field.LayoutLittleEndian);
                builder.Emit(ReadOpCode.ReadTerminatedText, index, builder.AddCodec(field.TerminatedCodecId, terminated), 0);
                return null;
            }

        case CompiledArrayKind.Scalar:
            if (field.Composite is { } nested)
            {
                ReadProgramOutcome outcome = this.cache.GetComposite(this.compilation, nested);
                if (outcome.Program is not { } program)
                {
                    return outcome.Reason;
                }

                builder.Emit(nested.IsUnion ? ReadOpCode.ReadUnion : ReadOpCode.ReadStruct, index, builder.AddNested(program), field.HasQualifiedPrefix ? builder.AddPrefix(field.QualifiedPrefix!) : -1);
                return null;
            }

            if (field.CodecId < 0)
            {
                return Refuse(location, field, NoReader);
            }

            if (field.Enum is { } enm)
            {
                builder.Emit(ReadOpCode.ReadEnum, index, codec, builder.AddEnum(enm));
                return null;
            }

            if (field.Codec.IsCustom)
            {
                builder.Emit(ReadOpCode.ReadCustom, index, codec, 0);
                return null;
            }

            if (ScalarOp(field.Codec) is not { } scalar)
            {
                return Refuse(location, field, NoReader);
            }

            builder.Emit(scalar, index, codec, 0);
            return null;

        default:
            return this.EmitArrayRead(builder, index, location, codec, standalone);
        }
    }

    /// <summary>
    ///     Emits an array's read step, chosen by its element kind in the interpreter's order. A multidimensional array
    ///     reads its elements in flat row-major order and is then nested (<see cref="ReadOpCode.ReshapeTable"/>), except
    ///     characters, whose innermost rows become strings in <see cref="ReadOpCode.ReadCharTable"/>, and byte-counted text,
    ///     which is one string of all its bytes.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="codec">The element codec's index.</param>
    /// <param name="standalone">Whether no composite places the member.</param>
    /// <returns>A reason located at this member, a nested struct's own reason, or <see langword="null"/>.</returns>
    private string? EmitArrayRead(ReadProgramBuilder builder, int index, string location, int codec, bool standalone)
    {
        CompiledField field = builder.Fields[index];
        bool table = field.Array.Dimensions.Length > 1;
        if (field.Composite is { } nested)
        {
            ReadProgramOutcome outcome = this.cache.GetComposite(this.compilation, nested);
            if (outcome.Program is not { } program)
            {
                return outcome.Reason;
            }

            // The interpreter takes an element struct's block path over the whole array only for one dimension, and never
            // for union elements.
            builder.Emit(
                nested.IsUnion ? ReadOpCode.ReadUnionArray : table ? ReadOpCode.ReadStructElements : ReadOpCode.ReadStructArray,
                index,
                builder.AddNested(program),
                0);
            EmitReshape(builder, index, table);
            return null;
        }

        if (field.CodecId < 0)
        {
            return Refuse(location, field, NoReader);
        }

        if (field.Enum is { } enm)
        {
            builder.Emit(ReadOpCode.ReadEnumArray, index, codec, builder.AddEnum(enm));
            EmitReshape(builder, index, table);
            return null;
        }

        // Byte-counted text is decided on the spelling before any other array shape, as the interpreter does. A caller's
        // codec keeps its own step even for an unnamed member, which reads each value and keeps none.
        if (BoundedTextCodec.IsType(field.TypeSpelling))
        {
            builder.Emit(ReadOpCode.ReadBoundedText, index, codec, 0);
            return null;
        }

        if (field.Codec.IsCustom)
        {
            builder.Emit(ReadOpCode.ReadCustomArray, index, codec, 0);
            EmitReshape(builder, index, table);
            return null;
        }

        if (field.Name.Length == 0)
        {
            builder.Emit(ReadOpCode.SkipElements, index, codec, 0);
            return null;
        }

        if (field.IsCharElement || field.IsWideCharElement)
        {
            builder.Emit(table ? ReadOpCode.ReadCharTable : field.IsCharElement ? ReadOpCode.ReadCharArray : ReadOpCode.ReadWideCharArray, index, codec, 0);
            return null;
        }

        ReadOpCode op = !field.Codec.IsFixedWidthNumeric ? ReadOpCode.ReadCodecArray
                        : table ? standalone ? ReadOpCode.ReadNumericElementList : ReadOpCode.ReadNumericList
                        : standalone ? ReadOpCode.ReadNumericElements
                        : ReadOpCode.ReadNumericArray;
        builder.Emit(op, index, codec, 0);
        EmitReshape(builder, index, table);
        return null;
    }

    /// <summary>
    ///     Emits a pointer member's read: its target description, and the pointer (or, for an array, each element) read
    ///     deferred where the interpreter defers it - a pointer a struct places that <see cref="CompiledField.FollowsAfterStruct"/>
    ///     - and followed in place otherwise.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member (a root, a union view).</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitPointerRead(ReadProgramBuilder builder, int index, string location, bool standalone)
    {
        CompiledField field = builder.Fields[index];
        if (this.DescribePointerTarget(field) is not { } target)
        {
            return Refuse(location, field, UnslottedName + this.FirstUnslottedName(field.PointerElements!.CountExpression!));
        }

        bool deferred = !standalone && field.FollowsAfterStruct;
        builder.DefersPointers |= deferred;
        bool array = field.Array.Kind != CompiledArrayKind.Scalar;
        builder.Emit(array ? ReadOpCode.ReadPointerArray : ReadOpCode.ReadPointer, index, builder.AddPointerTarget(target), deferred ? 1 : 0);
        EmitReshape(builder, index, field.Array.Dimensions.Length > 1);
        return null;
    }

    /// <summary>
    ///     Describes a pointer's final target in the interpreter's order of checks: a counted target (by its element's
    ///     kind), an enum, a struct or union, terminated text, then any other value through its codec.
    /// </summary>
    /// <param name="field">The pointer field.</param>
    /// <returns>The target, or <see langword="null"/> when a data-dependent count names an identifier without a slot.</returns>
    private ReadPointerTarget? DescribePointerTarget(CompiledField field)
    {
        int pointerSize = this.compilation.PointerSize;
        if (field.HasCountedTarget)
        {
            CompiledArrayShape elements = field.PointerElements!;
            ProgramExpression? count = null;
            if (elements.FixedCount is null)
            {
                if (this.FirstUnslottedName(elements.CountExpression!) is not null)
                {
                    return null;
                }

                count = this.cache.Table.Compile(elements.CountExpression!);
            }

            CompiledField element = field.CountedElement(pointerSize);
            var elementCodec = new ReadProgram.Codec(element.CodecId, element.Codec);
            if (element.IsCharElement || element.IsWideCharElement)
            {
                return new ReadPointerTarget(field, ReadPointerTargetKind.CountedText, elementCodec, null, null, element, count);
            }

            if (element.Codec.IsFixedWidthNumeric && element.Enum is null)
            {
                return new ReadPointerTarget(field, ReadPointerTargetKind.CountedNumbers, elementCodec, null, null, element, count);
            }

            if (element.Type.Symbol.Definition is CompiledCompositeType elementComposite)
            {
                return new ReadPointerTarget(field, ReadPointerTargetKind.CountedComposites, elementCodec, null, elementComposite, element, count);
            }

            return element.Enum is { } elementEnum
                       ? new ReadPointerTarget(field, ReadPointerTargetKind.CountedEnums, elementCodec, elementEnum, null, element, count)
                       : new ReadPointerTarget(field, ReadPointerTargetKind.CountedValues, elementCodec, null, null, element, count);
        }

        CompiledField view = field.SelectPointerTarget(0, null, pointerSize);
        var codec = new ReadProgram.Codec(field.CodecId, view.Codec);
        return field.Type.Symbol.Definition switch
        {
            CompiledEnumType enm => new ReadPointerTarget(field, ReadPointerTargetKind.Enum, codec, enm, null, null, null),
            CompiledCompositeType composite => new ReadPointerTarget(field, ReadPointerTargetKind.Composite, codec, null, composite, null, null),
            _ when field.HasTerminatedCodec => new ReadPointerTarget(
                field,
                ReadPointerTargetKind.Terminated,
                new ReadProgram.Codec(field.TerminatedCodecId, PrimitiveCodec.Resolve(PrimitiveCatalog.CanonicalNames[field.TerminatedCodecId], field.LayoutLittleEndian)),
                null,
                null,
                null,
                null),
            _ when field.CodecId >= 0 => new ReadPointerTarget(field, ReadPointerTargetKind.Value, codec, null, null, null, null),
            _ => new ReadPointerTarget(field, ReadPointerTargetKind.NoReader, codec, null, null, null, null),
        };
    }

    /// <summary>
    ///     Emits the capture of a member's value where the interpreter captures one: only a named member some expression
    ///     can read, never a struct or byte-counted text, and an array only when it has elements (the interpreter
    ///     captures per element or after a non-empty block).
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    private void EmitCapture(ReadProgramBuilder builder, int index)
    {
        CompiledField field = builder.Fields[index];
        if (!field.CapturesLayoutVariable || field.Name.Length == 0 || field.Composite is not null ||
            !this.cache.Table.TryGetSlot(field.Name, out int slot))
        {
            return;
        }

        switch (field.Array.Kind)
        {
        case CompiledArrayKind.Fixed or CompiledArrayKind.Runtime when BoundedTextCodec.IsType(field.TypeSpelling):
            return;
        case CompiledArrayKind.Fixed when field.Array.TotalFixedElementCount == 0:
            return;
        case CompiledArrayKind.Fixed:
            builder.Emit(ReadOpCode.CaptureNotANumber, index, slot, builder.AddUnusable(field.NotANumberReason!));
            break;
        case CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated:
            builder.Emit(ReadOpCode.CaptureNotANumberIfElements, index, slot, builder.AddUnusable(field.NotANumberReason!));
            break;
        default:
            if (field.NotANumberReason is { } reason)
            {
                builder.Emit(ReadOpCode.CaptureNotANumber, index, slot, builder.AddUnusable(reason));
            }
            else
            {
                builder.Emit(
                    field.Enum is not null ? ReadOpCode.CaptureEnum
                    : field.Codec.Kind == PrimitiveCodecKind.UInt128 ? ReadOpCode.CaptureUInt128
                    : ReadOpCode.CaptureInteger,
                    index,
                    slot,
                    0);
            }

            break;
        }

        ReadProgram.QualifiedTarget[] targets = this.cache.GetQualifiedTargets(field.Name);
        if (targets.Length > 0)
        {
            builder.Emit(ReadOpCode.PublishQualified, index, slot, builder.AddQualifiedTargets(targets));
        }
    }
}
