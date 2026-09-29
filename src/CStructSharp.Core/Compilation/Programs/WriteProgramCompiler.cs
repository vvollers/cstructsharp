namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Translates a compiled struct (or a root) into a <see cref="WriteProgram"/> that writes exactly what the
///     interpreter's writer writes, in the same order, with the same checks - or records why it cannot yet
///     (<see cref="WriteProgramOutcome.Reason"/>), naming the innermost struct and member that needs a later engine stage.
/// </summary>
/// <remarks>
///     <para>Per member, in the interpreter's order (<c>WriteStruct</c> and <c>WriteFieldValue</c>):</para>
///     <list type="number">
///         <item>one <see cref="WriteOpCode.SelectArm"/> per <c>if</c>/<c>switch</c> arm the member sits in, outermost first;
///         an unselected member rejects a supplied value for any name it makes visible;</item>
///         <item>the value: looked up for a named member, the all-zero padding value for an unnamed one;</item>
///         <item>the element count: a fixed count is checked against the array limit, a runtime count is evaluated;</item>
///         <item>placement, with the member's static offset where it is known (<see cref="ReadPlacement"/>, shared with the reader);</item>
///         <item>the encoding, specialized by value kind;</item>
///         <item>the capture of the supplied value (or an enum's exact number) and its qualified publication;</item>
///         <item>for a composite with conditional members, the scope step that saves and restores its names.</item>
///     </list>
///     <para>
///         The composite ends with <see cref="WriteOpCode.FinishComposite"/>, its tail padding. A named nested struct refers
///         to its own cached program; an anonymous promoted struct is compiled into a program of its own that reads its
///         members from the parent's value, because the interpreter writes it through a call of its own with the parent's
///         data.
///     </para>
///     <para>A member the engine cannot write yet gives a reason instead of a program. A compiler is used for one request on one thread.</para>
/// </remarks>
internal sealed class WriteProgramCompiler
{
    /// <summary>The reason for a bitfield or a <c>: 0</c> separator, which stage 6b adds.</summary>
    public const string Bitfields = "bitfields are not written by the engine yet (stage 6b)";

    /// <summary>The reason for a union, named or promoted, which stage 6b adds.</summary>
    public const string Unions = "unions are not written by the engine yet (stage 6b)";

    /// <summary>The reason for a pointer, which stage 6b adds.</summary>
    public const string Pointers = "pointers are not written by the engine yet (stage 6b)";

    /// <summary>The reason for a caller's codec, which stage 6b adds.</summary>
    public const string CustomCodecs = "custom codecs are not written by the engine yet (stage 6b)";

    /// <summary>The reason for an array whose value gives its element count (<c>[EOF]</c>, terminated, unsized), which stage 6b adds.</summary>
    public const string DataSizedArrays = "data-sized arrays are not written by the engine yet (stage 6b)";

    /// <summary>The reason for a multidimensional array, which stage 6b adds.</summary>
    public const string MultidimensionalArrays = "multidimensional arrays are not written by the engine yet (stage 6b)";

    /// <summary>The reason for a field the catalog has no writer for; the interpreter fails such a write.</summary>
    public const string NoWriter = "the field has no codec writer";

    /// <summary>The reason for a declaration kind that is not a writable root.</summary>
    public const string UnwritableRoot = "the declaration is not a writable root";

    private readonly LayoutCompilation compilation;
    private readonly WriteProgramCache cache;
    private readonly MemberExtents extents;

    /// <summary>Creates a compiler for one request.</summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="cache">The layout's write program cache, which supplies nested structs' programs and qualified targets.</param>
    public WriteProgramCompiler(LayoutCompilation compilation, WriteProgramCache cache)
    {
        this.compilation = compilation;
        this.cache = cache;
        this.extents = new MemberExtents(compilation);
    }

    /// <summary>Compiles the write of a struct from a value of its own.</summary>
    /// <param name="composite">The composite.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    public WriteProgramOutcome CompileComposite(CompiledCompositeType composite)
        => composite.IsUnion
               ? WriteProgramOutcome.NotSupported(ReadProgramCompiler.Locate(composite) + ": " + Unions)
               : this.CompileStruct(composite, WriteProgramKind.Composite, composite.Shape);

    /// <summary>
    ///     Compiles a root, as the interpreter's root dispatch writes it: a struct (or a typedef of an inline struct), a
    ///     typedef or enum field written standalone, or a <c>#define</c> evaluated into the variables.
    /// </summary>
    /// <param name="rootName">The name the root is requested by.</param>
    /// <param name="declaration">The root's declaration.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    public WriteProgramOutcome CompileRoot(string rootName, CStructElement declaration)
    {
        StructShape rootShape = this.compilation.ModelQueries.GetRootShape(rootName);
        switch (declaration)
        {
        case Struct strct:
            return this.CompileRootStruct(rootName, this.compilation.SizeQueries.GetCompiledComposite(strct), rootShape);
        case Typedef { Struct: { } inline, }:
            return this.CompileRootStruct(rootName, this.compilation.SizeQueries.GetCompiledComposite(inline), rootShape);
        case Typedef:
        case CstructEnum:
            {
                CompiledField field = this.compilation.ModelQueries.GetCompiledRootField(declaration);
                var builder = new WriteProgramBuilder(this.cache.Table, [field], rootShape, 0);
                var unplaced = new ReadPlacement(false);
                if (this.EmitMember(builder, 0, rootName, standalone: true, ref unplaced) is { } reason)
                {
                    return WriteProgramOutcome.NotSupported(reason);
                }

                return WriteProgramOutcome.Eligible(builder.Build(WriteProgramKind.Root, rootName, null));
            }

        case Defines definition:
            {
                var builder = new WriteProgramBuilder(this.cache.Table, [], rootShape, 0);
                int value = builder.AddExpression(definition.Value, "definition " + definition.Name.Name);
                int slot = this.cache.Table.TryGetSlot(definition.Name.Name, out int found) ? found : -1;
                builder.Emit(WriteOpCode.EvaluateDefinition, -1, value, slot);
                return WriteProgramOutcome.Eligible(builder.Build(WriteProgramKind.Root, rootName, null));
            }

        default:
            return WriteProgramOutcome.NotSupported(rootName + ": " + UnwritableRoot);
        }
    }

    /// <summary>Compiles a root that writes one struct from the root value.</summary>
    /// <param name="rootName">The name the root is requested by.</param>
    /// <param name="composite">The struct.</param>
    /// <param name="rootShape">The one-member root shape.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    private WriteProgramOutcome CompileRootStruct(string rootName, CompiledCompositeType composite, StructShape rootShape)
    {
        WriteProgramOutcome nested = this.cache.GetComposite(this.compilation, composite);
        if (nested.Program is not { } program)
        {
            return nested;
        }

        var builder = new WriteProgramBuilder(this.cache.Table, [], rootShape, 0);
        builder.Emit(WriteOpCode.WriteRootStruct, -1, builder.AddNested(program), -1);
        return WriteProgramOutcome.Eligible(builder.Build(WriteProgramKind.Root, rootName, null));
    }

    /// <summary>Compiles a struct's members into a program.</summary>
    /// <param name="composite">The struct.</param>
    /// <param name="kind">Whether it is written from a value of its own or promoted from its parent's.</param>
    /// <param name="shape">The layout of the value its members are read from.</param>
    /// <returns>The program, or why it cannot be built yet.</returns>
    private WriteProgramOutcome CompileStruct(CompiledCompositeType composite, WriteProgramKind kind, StructShape shape)
    {
        string location = ReadProgramCompiler.Locate(composite);
        CompiledField[] fields = [.. composite.Fields];
        var builder = new WriteProgramBuilder(this.cache.Table, fields, shape, composite.ConditionalGroupCount);
        if (composite.ConditionalScope is { } scope)
        {
            // The interpreter removes the kept names at entry; only the ones an expression can read matter.
            builder.Scope = new ReadConditionalScope(scope, this.cache.Table);
            if (builder.Scope.ClearedSlots.Length > 0)
            {
                builder.Emit(WriteOpCode.EnterConditionalScope, -1, 0, 0);
            }
        }

        var placement = new ReadPlacement(this.compilation.Aligned);
        var selections = new List<int>();
        for (int index = 0; index < fields.Length; index++)
        {
            CompiledField field = fields[index];

            // An unselected member is skipped whole after its inactive check: no lookup, placement, write or scope step.
            selections.Clear();
            foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
            {
                selections.Add(builder.Emit(WriteOpCode.SelectArm, index, builder.AddBranch(branch), -1));
            }

            ReadPlacement before = placement;
            if (this.EmitMember(builder, index, location, standalone: false, ref placement) is { } reason)
            {
                return WriteProgramOutcome.NotSupported(reason);
            }

            if (builder.Scope is { } mapped && mapped.HasEffect(index))
            {
                builder.Emit(WriteOpCode.CompleteMember, index, 0, 0);
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
            return WriteProgramOutcome.NotSupported(location + ": " + ReadProgramCompiler.PlacementMismatch);
        }

        builder.Emit(WriteOpCode.FinishComposite, -1, knownTail ? padding : -1, alignment);
        return WriteProgramOutcome.Eligible(builder.Build(kind, composite.Name, composite));
    }

    /// <summary>
    ///     Emits one member's value, count, placement, encoding and capture, or returns why the member cannot be written
    ///     yet: a reason located at this member, or a nested struct's own reason unchanged.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member: a root field.</param>
    /// <param name="placement">The placement state; advanced past the member (a standalone field leaves it alone).</param>
    /// <returns>A reason, or <see langword="null"/> when the member was emitted.</returns>
    private string? EmitMember(WriteProgramBuilder builder, int index, string location, bool standalone, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        if (field.IsZeroWidthBitfield || field.BitSize > 0)
        {
            return ReadProgramCompiler.Refuse(location, field, Bitfields);
        }

        if (field.PointerDepth > 0)
        {
            return ReadProgramCompiler.Refuse(location, field, Pointers);
        }

        if (field.IsPromotedComposite)
        {
            return this.EmitPromoted(builder, index, location, ref placement);
        }

        if (field.IsUnnamed && field.Codec.IsCustom)
        {
            return ReadProgramCompiler.Refuse(location, field, CustomCodecs);
        }

        if (this.EmitValueField(builder, index, location) is { } unsized)
        {
            return unsized;
        }

        // The value: the root value, the member looked up in the struct's data (a failure from here to the capture names
        // the member, as the interpreter's field loop does), or the zero value padding is written with.
        if (standalone)
        {
            builder.Emit(WriteOpCode.LoadRoot, index, 0, 0);
        }
        else if (field.IsUnnamed)
        {
            builder.Emit(WriteOpCode.LoadPadding, index, 0, 0);
        }
        else
        {
            if (!builder.SetShapeSlot(index))
            {
                return ReadProgramCompiler.Refuse(location, field, ReadProgramCompiler.NoShapeSlot);
            }

            builder.NotedMember = index;
            builder.Emit(WriteOpCode.LoadMember, index, builder.ShapeSlots[index], 0);
        }

        string? reason = this.EmitCount(builder, index, location);
        if (reason is null && !standalone)
        {
            reason = this.EmitPlacement(builder, index, location, ref placement);
        }

        reason ??= this.EmitWrite(builder, index, location, standalone);
        if (reason is not null)
        {
            return reason;
        }

        if (!standalone)
        {
            this.extents.AdvancePast(ref placement, field);
        }

        this.EmitCapture(builder, index);
        builder.NotedMember = -1;
        return null;
    }

    /// <summary>
    ///     Chooses the field a member's value is encoded as: an unsized character array (<c>char name[]</c>) is one
    ///     terminated string, written through its terminated view; every other unsized or data-sized array is refused.
    /// </summary>
    /// <param name="builder">The program under construction; its <see cref="WriteProgramBuilder.ValueFields"/> entry is set.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitValueField(WriteProgramBuilder builder, int index, string location)
    {
        CompiledField field = builder.Fields[index];
        switch (field.Array.Kind)
        {
        case CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated:
            return ReadProgramCompiler.Refuse(location, field, DataSizedArrays);
        case CompiledArrayKind.Flexible:
            {
                CompiledField? view = field.IsCharElement
                                          ? field.SelectPointerTarget(0, CharacterFieldTypes.CstringType.Name, this.compilation.PointerSize)
                                          : field.IsWideCharElement
                                              ? field.SelectPointerTarget(0, CharacterFieldTypes.GetStringPointerHandlerKey(field.TypeSpelling), this.compilation.PointerSize)
                                              : null;
                if (view is null || !PrimitiveCodecs.IsVariableLengthType(view.TypeSpelling))
                {
                    return ReadProgramCompiler.Refuse(location, field, DataSizedArrays);
                }

                builder.ValueFields[index] = view;
                return null;
            }

        default:
            return field.Array.Dimensions.Length > 1 ? ReadProgramCompiler.Refuse(location, field, MultidimensionalArrays) : null;
        }
    }

    /// <summary>
    ///     Emits the element count of an array member: a fixed count checked against the element limit, or a runtime count
    ///     evaluated in the write domain. A scalar and an unsized character array (one terminated string) have none.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitCount(WriteProgramBuilder builder, int index, string location)
    {
        CompiledField field = builder.Fields[index];
        switch (field.Array.Kind)
        {
        case CompiledArrayKind.Fixed:
            builder.Emit(WriteOpCode.CheckFixedCount, index, field.Array.TotalFixedElementCount!.Value, 0);
            return null;
        case CompiledArrayKind.Runtime:
            if (this.FirstUnslottedName(field.Array.CountExpression!) is { } unslotted)
            {
                return ReadProgramCompiler.Refuse(location, field, ReadProgramCompiler.UnslottedName + unslotted);
            }

            builder.Emit(WriteOpCode.EvaluateCount, index, builder.AddExpression(field.Array.CountExpression!, "array length for " + field.Name), 0);
            return null;
        default:
            return null;
        }
    }

    /// <summary>
    ///     Emits where a member a struct places starts - nothing, a relative <see cref="WriteOpCode.Seek"/> over known
    ///     padding, or an <see cref="WriteOpCode.Align"/> - and, when the layout's build could not check it, its <c>@N</c>
    ///     assertion. The interpreter places the member's value field: the member itself, or for an unsized character
    ///     array its terminated view, which is aligned to one byte (a <c>wchar name[]</c> is placed unaligned, unlike its
    ///     read). A static placement that contradicts the layout's compiled offset is refused, except where the view places it.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="placement">The placement state.</param>
    /// <returns>A reason when a statically known placement contradicts the compiled offset; otherwise <see langword="null"/>.</returns>
    private string? EmitPlacement(WriteProgramBuilder builder, int index, string location, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        CompiledField placed = builder.ValueFields[index];
        if (placement.Place(index, placed.Alignment, out ReadStep step))
        {
            builder.EmitPlacement(step);
        }

        // A member placed by its view is exactly where the interpreter's writer puts it, even where that differs from the
        // offset the layout compiled for the member; any other contradiction means the static placement is wrong.
        long? known = placement.KnownOffset;
        if (ReferenceEquals(placed, field) && field.FixedOffset is int compiled && known is long offset && offset != compiled)
        {
            return ReadProgramCompiler.Refuse(location, field, ReadProgramCompiler.PlacementMismatch);
        }

        if (placed.AssertedOffset is int asserted && placed.FixedOffset is null && known != asserted)
        {
            builder.Emit(WriteOpCode.CheckOffset, index, asserted, 0);
        }

        return null;
    }

    /// <summary>Emits a member's encoding step, chosen by its value kind in the interpreter's order.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member, which rules out the typed block path.</param>
    /// <returns>A reason located at this member, a nested struct's own reason, or <see langword="null"/>.</returns>
    private string? EmitWrite(WriteProgramBuilder builder, int index, string location, bool standalone)
    {
        CompiledField field = builder.Fields[index];
        CompiledField value = builder.ValueFields[index];
        if (field.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime)
        {
            // Text is decided before any other element kind, on the member's spelling, as WriteArrayValue does.
            if (value.IsCharacterArray || (!field.IsPointer && BoundedTextCodec.IsType(field.TypeSpelling)))
            {
                if (field.CodecId < 0)
                {
                    return ReadProgramCompiler.Refuse(location, field, NoWriter);
                }

                builder.Emit(WriteOpCode.WriteText, index, builder.AddCodec(field.CodecId, field.Codec), 0);
                return null;
            }

            if (field.Composite is { } element)
            {
                return this.EmitNested(builder, index, location, element, WriteOpCode.WriteStructArray, -1);
            }

            if (field.CodecId < 0)
            {
                return ReadProgramCompiler.Refuse(location, field, NoWriter);
            }

            int elementCodec = builder.AddCodec(field.CodecId, field.Codec);
            if (field.Enum is { } elementEnum)
            {
                builder.Emit(WriteOpCode.WriteEnumArray, index, elementCodec, builder.AddEnum(elementEnum));
            }
            else if (field.Codec.IsCustom)
            {
                return ReadProgramCompiler.Refuse(location, field, CustomCodecs);
            }
            else
            {
                builder.Emit(field.Codec.IsFixedWidthNumeric ? WriteOpCode.WriteNumericArray : WriteOpCode.WriteCodecArray, index, elementCodec, standalone ? 1 : 0);
            }

            return null;
        }

        if (value.Composite is { } nested)
        {
            return this.EmitNested(builder, index, location, nested, WriteOpCode.WriteStruct, field.HasQualifiedPrefix ? builder.AddPrefix(field.QualifiedPrefix!) : -1);
        }

        if (value.CodecId < 0)
        {
            return ReadProgramCompiler.Refuse(location, field, NoWriter);
        }

        int codec = builder.AddCodec(value.CodecId, value.Codec);
        if (value.Enum is { } enm)
        {
            builder.Emit(WriteOpCode.WriteEnum, index, codec, builder.AddEnum(enm));
        }
        else if (value.Codec.IsCustom)
        {
            return ReadProgramCompiler.Refuse(location, field, CustomCodecs);
        }
        else
        {
            builder.Emit(value.Codec.IsFixedWidthNumeric ? WriteOpCode.WriteNumeric : WriteOpCode.WriteCodecValue, index, codec, 0);
        }

        return null;
    }

    /// <summary>Emits the write of a nested struct, or of an array of them, through the struct's cached program.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="nested">The nested composite.</param>
    /// <param name="op">The step: a scalar struct or an array of them.</param>
    /// <param name="prefix">The qualified prefix the write activates, or -1.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitNested(WriteProgramBuilder builder, int index, string location, CompiledCompositeType nested, WriteOpCode op, int prefix)
    {
        if (nested.IsUnion)
        {
            return ReadProgramCompiler.Refuse(location, builder.Fields[index], Unions);
        }

        WriteProgramOutcome outcome = this.cache.GetComposite(this.compilation, nested);
        if (outcome.Program is not { } program)
        {
            return outcome.Reason;
        }

        builder.Emit(op, index, builder.AddNested(program), prefix);
        return null;
    }

    /// <summary>
    ///     Emits an anonymous promoted struct: placed like any member, written by a program of its own that looks its
    ///     members up in this struct's value. It is written outside the member-noting context, as the interpreter writes it.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="placement">The placement state.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitPromoted(WriteProgramBuilder builder, int index, string location, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        CompiledCompositeType composite = field.Composite ?? this.compilation.SizeQueries.GetCompiledComposite((Struct)field.Declaration);
        if (composite.IsUnion)
        {
            return ReadProgramCompiler.Refuse(location, field, Unions);
        }

        if (this.EmitPlacement(builder, index, location, ref placement) is { } misplaced)
        {
            return misplaced;
        }

        WriteProgramOutcome nested = this.CompileStruct(composite, WriteProgramKind.Promoted, builder.Shape);
        if (nested.Program is not { } program)
        {
            return nested.Reason;
        }

        builder.Emit(WriteOpCode.WritePromotedStruct, index, builder.AddNested(program), field.HasQualifiedPrefix ? builder.AddPrefix(field.QualifiedPrefix!) : -1);
        this.extents.AdvancePast(ref placement, field);
        return null;
    }

    /// <summary>
    ///     Emits the capture of a member's supplied value where the interpreter captures one: a named member some expression
    ///     can read. Unlike the reader, the writer captures every such member - a struct, text or an empty array too - from
    ///     the value it was given, through the shared capture rule.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    private void EmitCapture(WriteProgramBuilder builder, int index)
    {
        CompiledField field = builder.Fields[index];
        if (!field.CapturesLayoutVariable || field.Name.Length == 0 || !this.cache.Table.TryGetSlot(field.Name, out int slot))
        {
            return;
        }

        ReadProgram.QualifiedTarget[] targets = this.cache.GetQualifiedTargets(field.Name);
        int published = targets.Length > 0 ? builder.AddQualifiedTargets(targets) : -1;
        if (field.NotANumberReason is { } reason)
        {
            builder.NotANumbers[index] = new NotANumberVariable(reason);
            builder.Emit(WriteOpCode.CaptureNotANumber, index, slot, published);
        }
        else
        {
            builder.Emit(WriteOpCode.CaptureValue, index, slot, published);
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
}
