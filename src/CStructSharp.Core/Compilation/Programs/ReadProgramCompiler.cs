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
///         Anything outside the engine's current feature set gives a reason instead of a program: bitfields and unions
///         (stage 4), pointers (stage 5), custom codecs, multidimensional, to-end and terminated arrays (stage 3). A
///         compiler is used for one request on one thread.
///     </para>
/// </remarks>
internal sealed class ReadProgramCompiler
{
    /// <summary>The reason for a bitfield or <c>: 0</c> separator.</summary>
    public const string Bitfields = "bitfields are not supported yet (stage 4)";

    /// <summary>The reason for a union member or root.</summary>
    public const string Unions = "unions are not supported yet (stage 4)";

    /// <summary>The reason for a pointer field.</summary>
    public const string Pointers = "pointers are not supported yet (stage 5)";

    /// <summary>The reason for a field read by a caller-supplied codec.</summary>
    public const string CustomCodecs = "custom codecs are not supported yet (stage 3)";

    /// <summary>The reason for an array of more than one dimension.</summary>
    public const string MultidimensionalArrays = "multidimensional arrays are not supported yet (stage 3)";

    /// <summary>The reason for an <c>[EOF]</c> array.</summary>
    public const string ToEndArrays = "to-end arrays ([EOF]) are not supported yet (stage 3)";

    /// <summary>The reason for an array ended by an all-zero element.</summary>
    public const string TerminatedArrays = "terminated arrays are not supported yet (stage 3)";

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

    /// <summary>Creates a compiler for one request.</summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="cache">The layout's program cache, which supplies nested structs' programs and qualified targets.</param>
    public ReadProgramCompiler(LayoutCompilation compilation, ReadProgramCache cache)
    {
        this.compilation = compilation;
        this.cache = cache;
    }

    /// <summary>Compiles a struct read into a value of its own.</summary>
    /// <param name="composite">The composite.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    public ReadProgramOutcome CompileComposite(CompiledCompositeType composite)
        => this.CompileStruct(composite, ReadProgramKind.Composite, composite.Shape);

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

                return ReadProgramOutcome.Eligible(builder.Build(ReadProgramKind.Root, rootName, null));
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
    private static string Refuse(string location, CompiledField field, string what)
        => location + "." + (field.Name.Length > 0 ? field.Name : field.IsPromotedComposite ? "(anonymous)" : "(unnamed)") + ": " + what;

    /// <summary>The location a struct's reasons name: its name, or a marker for an anonymous one.</summary>
    /// <param name="composite">The composite.</param>
    /// <returns>The name.</returns>
    private static string Locate(CompiledCompositeType composite) => composite.Name.Length > 0 ? composite.Name : "(anonymous struct)";

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

    /// <summary>
    ///     A size a dynamic member's extent is always a multiple of, which carries alignment knowledge past it (see
    ///     <see cref="ReadPlacement.Restart"/>): the element size of an array whose count the data decides, or the
    ///     alignment of a struct in an aligned layout (its tail padding makes its size a multiple of it); otherwise 1.
    /// </summary>
    /// <param name="field">The member.</param>
    /// <param name="aligned">Whether the layout is aligned.</param>
    /// <returns>The unit in bytes.</returns>
    private static long ExtentUnit(CompiledField field, bool aligned)
    {
        if (field.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime && field.FixedElementSize is int size && size > 0)
        {
            return size;
        }

        return field.Array.Kind is CompiledArrayKind.Scalar or CompiledArrayKind.Fixed or CompiledArrayKind.Runtime &&
               aligned && field.Composite is { } composite
                   ? composite.Symbol.Alignment
                   : 1;
    }

    /// <summary>Records where the position is after a member: a known size advances, a size the data decides restarts from a new anchor.</summary>
    /// <param name="placement">The placement state after the member was placed; the position's guarantee there is the member's start guarantee.</param>
    /// <param name="field">The member.</param>
    /// <param name="aligned">Whether the layout is aligned.</param>
    private static void AdvancePast(ref ReadPlacement placement, CompiledField field, bool aligned)
    {
        if (field.FixedStorageSize is int size)
        {
            placement.Advance(size);
        }
        else
        {
            placement.Restart(ExtentUnit(field, aligned));
        }
    }

    /// <summary>Compiles a root that reads one struct into a new value under <paramref name="key"/>.</summary>
    /// <param name="rootName">The name the root is requested by.</param>
    /// <param name="key">The name the struct's value is stored under (a typedef's name for a typedef of an inline struct).</param>
    /// <param name="composite">The struct.</param>
    /// <param name="rootShape">The one-member root shape.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    private ReadProgramOutcome CompileRootStruct(string rootName, string key, CompiledCompositeType composite, StructShape rootShape)
    {
        if (composite.IsUnion)
        {
            return ReadProgramOutcome.NotSupported(rootName + ": " + Unions);
        }

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
        builder.Emit(ReadOpCode.ReadRootStruct, -1, builder.AddNested(program), -1);
        return ReadProgramOutcome.Eligible(builder.Build(ReadProgramKind.Root, key, null));
    }

    /// <summary>Compiles a struct's members into a program.</summary>
    /// <param name="composite">The struct.</param>
    /// <param name="kind">Whether it has a value of its own or is promoted into its parent's.</param>
    /// <param name="shape">The layout of the value its members are stored into.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    private ReadProgramOutcome CompileStruct(CompiledCompositeType composite, ReadProgramKind kind, StructShape shape)
    {
        string location = Locate(composite);
        if (composite.IsUnion)
        {
            return ReadProgramOutcome.NotSupported(location + ": " + Unions);
        }

        CompiledField[] fields = [.. composite.Fields];
        var builder = new ReadProgramBuilder(this.cache.Table, fields, shape, composite.ConditionalGroupCount);
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
    /// <param name="standalone">Whether the member is a root field that no composite places.</param>
    /// <param name="placement">The placement state; advanced past the member (a standalone field leaves it alone).</param>
    /// <returns>A reason, or <see langword="null"/> when the member was emitted.</returns>
    private string? EmitMember(ReadProgramBuilder builder, int index, string location, bool standalone, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        if (field.IsZeroWidthBitfield || field.BitSize > 0)
        {
            return Refuse(location, field, Bitfields);
        }

        if (field.PointerDepth > 0)
        {
            return Refuse(location, field, Pointers);
        }

        if (field.Composite is { IsUnion: true, } || field.Declaration is Struct { IsUnion: true, })
        {
            return Refuse(location, field, Unions);
        }

        if (field.Codec.IsCustom)
        {
            return Refuse(location, field, CustomCodecs);
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
        case CompiledArrayKind.Fixed or CompiledArrayKind.Runtime when field.Array.Dimensions.Length > 1:
            return Refuse(location, field, MultidimensionalArrays);
        case CompiledArrayKind.ToEnd:
            return Refuse(location, field, ToEndArrays);
        case CompiledArrayKind.Terminated:
            return Refuse(location, field, TerminatedArrays);
        case CompiledArrayKind.Fixed:
            builder.Emit(ReadOpCode.CheckFixedCount, index, field.Array.FixedCount!.Value, 0);
            break;
        case CompiledArrayKind.Runtime:
            if (this.FirstUnslottedName(field.Array.CountExpression!) is { } unslotted)
            {
                return Refuse(location, field, UnslottedName + unslotted);
            }

            builder.Emit(ReadOpCode.EvaluateCount, index, builder.AddExpression(field.Array.CountExpression!, "array length for " + field.Name), 0);
            break;
        }

        if (!standalone && this.EmitPlacement(builder, index, ref placement) is { } misplaced)
        {
            return Refuse(location, field, misplaced);
        }

        if (this.EmitRead(builder, index, location, standalone) is { } refusal)
        {
            return refusal;
        }

        this.EmitCapture(builder, index);
        if (!standalone)
        {
            AdvancePast(ref placement, field, this.compilation.Aligned);
        }

        return null;
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
        if (this.EmitPlacement(builder, index, ref placement) is { } misplaced)
        {
            return Refuse(location, field, misplaced);
        }

        ReadProgramOutcome nested = promoted
                                        ? this.CompileStruct(composite, ReadProgramKind.Promoted, builder.Shape)
                                        : this.cache.GetComposite(this.compilation, composite);
        if (nested.Program is not { } program)
        {
            return nested.Reason;
        }

        if (promoted)
        {
            builder.Emit(ReadOpCode.ReadPromotedStruct, index, builder.AddNested(program), -1);
        }
        else
        {
            builder.Emit(ReadOpCode.ReadStruct, index, builder.AddNested(program), field.HasQualifiedPrefix ? builder.AddPrefix(field.QualifiedPrefix!) : -1);
        }

        AdvancePast(ref placement, field, this.compilation.Aligned);
        return null;
    }

    /// <summary>
    ///     Emits a member's placement and, when the layout's build could not check it, its <c>@N</c> offset assertion (a
    ///     statically known offset that satisfies it needs no step).
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="placement">The placement state.</param>
    /// <returns>A reason when the static placement contradicts the compiled offset; otherwise <see langword="null"/>.</returns>
    private string? EmitPlacement(ReadProgramBuilder builder, int index, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        if (placement.Place(index, field.Alignment, out ReadStep step))
        {
            builder.Emit(step);
        }

        long? known = placement.KnownOffset;
        if (field.FixedOffset is int compiled && known != compiled)
        {
            return PlacementMismatch;
        }

        if (field.AssertedOffset is int asserted && field.FixedOffset is null && known != asserted)
        {
            builder.Emit(ReadOpCode.CheckOffset, index, asserted, 0);
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

                builder.Emit(ReadOpCode.ReadStruct, index, builder.AddNested(program), field.HasQualifiedPrefix ? builder.AddPrefix(field.QualifiedPrefix!) : -1);
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

    /// <summary>Emits a one-dimensional array's read step, chosen by its element kind in the interpreter's order.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="codec">The element codec's index.</param>
    /// <param name="standalone">Whether no composite places the member.</param>
    /// <returns>A reason located at this member, a nested struct's own reason, or <see langword="null"/>.</returns>
    private string? EmitArrayRead(ReadProgramBuilder builder, int index, string location, int codec, bool standalone)
    {
        CompiledField field = builder.Fields[index];
        if (field.Composite is { } nested)
        {
            ReadProgramOutcome outcome = this.cache.GetComposite(this.compilation, nested);
            if (outcome.Program is not { } program)
            {
                return outcome.Reason;
            }

            builder.Emit(ReadOpCode.ReadStructArray, index, builder.AddNested(program), 0);
            return null;
        }

        if (field.CodecId < 0)
        {
            return Refuse(location, field, NoReader);
        }

        if (field.Enum is { } enm)
        {
            builder.Emit(ReadOpCode.ReadEnumArray, index, codec, builder.AddEnum(enm));
            return null;
        }

        // Byte-counted text is decided on the spelling before any other array shape, as the interpreter does.
        ReadOpCode op = BoundedTextCodec.IsType(field.TypeSpelling) ? ReadOpCode.ReadBoundedText
                        : field.Name.Length == 0 ? ReadOpCode.SkipElements
                        : field.IsCharElement ? ReadOpCode.ReadCharArray
                        : field.IsWideCharElement ? ReadOpCode.ReadWideCharArray
                        : !field.Codec.IsFixedWidthNumeric ? ReadOpCode.ReadCodecArray
                        : standalone ? ReadOpCode.ReadNumericElements
                        : ReadOpCode.ReadNumericArray;
        builder.Emit(op, index, codec, 0);
        return null;
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
        case CompiledArrayKind.Fixed when field.Array.FixedCount == 0:
            return;
        case CompiledArrayKind.Fixed:
            builder.Emit(ReadOpCode.CaptureNotANumber, index, slot, builder.AddUnusable(field.NotANumberReason!));
            break;
        case CompiledArrayKind.Runtime:
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
