namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Translates a compiled struct, union or root into a <see cref="WriteProgram"/> that writes exactly what the
///     interpreter's writer writes, in the same order, with the same checks - or records why it cannot
///     (<see cref="WriteProgramOutcome.Reason"/>), naming the innermost struct and member.
/// </summary>
/// <remarks>
///     <para>Per member of a struct, in the interpreter's order (<c>WriteStruct</c> and <c>WriteFieldValue</c>):</para>
///     <list type="number">
///         <item>one <see cref="WriteOpCode.SelectArm"/> per <c>if</c>/<c>switch</c> arm the member sits in, outermost first;
///         an unselected member rejects a supplied value for any name it makes visible;</item>
///         <item>the value: looked up for a named member, the all-zero padding value for an unnamed one;</item>
///         <item>the element count: a fixed count is checked against the array limit, a runtime count is evaluated, and an
///         array whose value decides its count takes it from the value;</item>
///         <item>placement: by the member's declaration, statically where the offset is known (<see cref="ReadPlacement"/>,
///         shared with the reader), or through the runtime placement cursor in a struct with bitfields;</item>
///         <item>the encoding, specialized by value kind, and a terminated array's all-zero terminator;</item>
///         <item>the capture of the supplied value (or an enum's exact number) and its qualified publication;</item>
///         <item>for a composite with conditional members, the scope step that saves and restores its names.</item>
///     </list>
///     <para>
///         The composite ends with its tail padding. A named nested struct or union refers to its own cached program; an
///         anonymous promoted struct is compiled into a program of its own that reads its members from the parent's value,
///         because the interpreter writes it through a call of its own with the parent's data. A union's program has one
///         standalone segment per member, written from the union's first byte, of which the executor runs the selected one.
///     </para>
///     <para>A member the engine cannot write gives a reason instead of a program. A compiler is used for one request on one thread.</para>
/// </remarks>
internal sealed class WriteProgramCompiler
{
    /// <summary>The reason for an array of bitfields, which the interpreter writes through a path no layout reaches.</summary>
    public const string BitfieldArrays = "a bitfield array has no writer";

    /// <summary>The reason for an unsized array that is not text, which the compiled layout never produces.</summary>
    public const string UnsizedArrays = "an unsized array that is not text has no writer";

    /// <summary>The reason for a field the catalog has no writer for; the interpreter fails such a write.</summary>
    public const string NoWriter = "the field has no codec writer";

    /// <summary>The reason for a declaration kind that is not a writable root.</summary>
    public const string UnwritableRoot = "the declaration is not a writable root";

    /// <summary>The reason for a nested path that selects no writable member; the path's own failure text follows.</summary>
    public const string UnresolvedPath = "the path selects no writable member: ";

    private readonly LayoutCompilation compilation;
    private readonly WriteProgramCache cache;
    private readonly MemberExtents extents;

    /// <summary>Creates a compiler for one request.</summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="cache">The layout's write program cache, which supplies nested programs and qualified targets.</param>
    public WriteProgramCompiler(LayoutCompilation compilation, WriteProgramCache cache)
    {
        this.compilation = compilation;
        this.cache = cache;
        this.extents = new MemberExtents(compilation);
    }

    /// <summary>Compiles the write of a struct from a value of its own, or of a union from its selection.</summary>
    /// <param name="composite">The composite.</param>
    /// <returns>The program, or why it cannot be built.</returns>
    public WriteProgramOutcome CompileComposite(CompiledCompositeType composite)
        => composite.IsUnion ? this.CompileUnion(composite) : this.CompileStruct(composite, WriteProgramKind.Composite, composite.Shape);

    /// <summary>
    ///     Compiles a root, as the interpreter's root dispatch writes it: a struct or union (or a typedef of an inline one),
    ///     a typedef or enum field written standalone, or a <c>#define</c> evaluated into the variables.
    /// </summary>
    /// <param name="rootName">The name the root is requested by.</param>
    /// <param name="declaration">The root's declaration.</param>
    /// <returns>The program, or why it cannot be built.</returns>
    public WriteProgramOutcome CompileRoot(string rootName, CStructElement declaration)
    {
        StructShape rootShape = this.compilation.ModelQueries.GetRootShape(rootName);
        switch (declaration)
        {
        case Struct strct:
            return this.CompileRootStruct(this.compilation.SizeQueries.GetCompiledComposite(strct), rootName, rootShape);
        case Typedef { Struct: { } inline, }:
            return this.CompileRootStruct(this.compilation.SizeQueries.GetCompiledComposite(inline), rootName, rootShape);
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

    /// <summary>
    ///     Compiles the write of one member on its own, as the interpreter writes the member a nested path selects
    ///     (<c>WriteFieldValue</c> without a placing struct): the value is the frame's data, a bitfield opens its own storage
    ///     unit, and no member context is noted. Its shape is its own, so no caller's value is mistaken for a struct of it.
    /// </summary>
    /// <param name="field">The member, narrowed to the element or sub-array a path's indexes select.</param>
    /// <returns>The program, or why it cannot be built.</returns>
    public WriteProgramOutcome CompileMember(CompiledField field)
    {
        var builder = new WriteProgramBuilder(this.cache.Table, [field], new StructShape([field.Name]), 0);
        var unplaced = new ReadPlacement(false);
        if (this.EmitMember(builder, 0, field.Name, standalone: true, ref unplaced) is { } reason)
        {
            return WriteProgramOutcome.NotSupported(reason);
        }

        return WriteProgramOutcome.Eligible(builder.Build(WriteProgramKind.Root, field.Name, null));
    }

    /// <summary>
    ///     The kind of one value of a member, in the interpreter's order (<c>WriteSingleFieldValue</c>): unnamed custom
    ///     padding, a pointer, an enum, a struct or union, a number, any other codec. Bitfields are decided by the caller.
    /// </summary>
    /// <param name="builder">The program under construction, which receives the operand.</param>
    /// <param name="field">The field the value is written as.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="member">The member, named in reasons.</param>
    /// <param name="kind">The value's kind.</param>
    /// <param name="operand">The codec index, nested program index, or zero-fill size.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? Classify(WriteProgramBuilder builder, CompiledField field, string location, CompiledField member, out WriteElementKind kind, out int operand)
    {
        operand = 0;
        if (field.IsUnnamed && field.Codec.IsCustom)
        {
            kind = WriteElementKind.Zeroes;
            operand = field.FixedElementSize ?? 0;
            return field.FixedElementSize is null ? ReadProgramCompiler.Refuse(location, member, NoWriter) : null;
        }

        if (field.PointerDepth > 0)
        {
            kind = WriteElementKind.Pointer;
            return null;
        }

        if (field.Enum is not null)
        {
            kind = WriteElementKind.Enum;
            operand = builder.AddCodec(field.CodecId, field.Codec);
            return field.CodecId < 0 ? ReadProgramCompiler.Refuse(location, member, NoWriter) : null;
        }

        if (field.Composite is { } nested)
        {
            kind = WriteElementKind.Composite;
            WriteProgramOutcome outcome = this.cache.GetComposite(this.compilation, nested);
            if (outcome.Program is not { } program)
            {
                return outcome.Reason;
            }

            operand = builder.AddNested(program);
            return null;
        }

        kind = field.Codec.IsFixedWidthNumeric ? WriteElementKind.Numeric : WriteElementKind.Codec;
        operand = builder.AddCodec(field.CodecId, field.Codec);
        return field.CodecId < 0 ? ReadProgramCompiler.Refuse(location, member, NoWriter) : null;
    }

    /// <summary>Compiles a root that writes one struct or union from the root value.</summary>
    /// <param name="composite">The struct or union.</param>
    /// <param name="rootName">The name the root is requested by.</param>
    /// <param name="rootShape">The one-member root shape.</param>
    /// <returns>The program, or why it cannot be built.</returns>
    private WriteProgramOutcome CompileRootStruct(CompiledCompositeType composite, string rootName, StructShape rootShape)
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

    /// <summary>
    ///     Compiles a union: one segment per member, each writing the member standalone from the union's first byte and
    ///     ending with <see cref="WriteOpCode.Return"/>, as the interpreter stages the selected member.
    /// </summary>
    /// <param name="union">The union.</param>
    /// <returns>The program, or why it cannot be built.</returns>
    private WriteProgramOutcome CompileUnion(CompiledCompositeType union)
    {
        string location = ReadProgramCompiler.Locate(union);
        CompiledField[] fields = [.. union.Fields];
        var builder = new WriteProgramBuilder(this.cache.Table, fields, union.Shape, 0) { UnionEntries = new int[fields.Length], };
        var unplaced = new ReadPlacement(false);
        for (int index = 0; index < fields.Length; index++)
        {
            builder.UnionEntries[index] = builder.Count;
            if (this.EmitMember(builder, index, location, standalone: true, ref unplaced) is { } reason)
            {
                return WriteProgramOutcome.NotSupported(reason);
            }

            builder.Emit(WriteOpCode.Return, index, 0, 0);
        }

        return WriteProgramOutcome.Eligible(builder.Build(WriteProgramKind.Union, union.Name, union));
    }

    /// <summary>Compiles a struct's members into a program.</summary>
    /// <param name="composite">The struct.</param>
    /// <param name="kind">Whether it is written from a value of its own or promoted from its parent's.</param>
    /// <param name="shape">The layout of the value its members are read from.</param>
    /// <returns>The program, or why it cannot be built.</returns>
    private WriteProgramOutcome CompileStruct(CompiledCompositeType composite, WriteProgramKind kind, StructShape shape)
    {
        string location = ReadProgramCompiler.Locate(composite);
        CompiledField[] fields = [.. composite.Fields];

        // Bitfields share storage units by the layout's packing rule, which only the runtime placement cursor applies.
        var builder = new WriteProgramBuilder(this.cache.Table, fields, shape, composite.ConditionalGroupCount)
        {
            UsesPlacementCursor = System.Array.Exists(fields, field => field.BitSize > 0 || field.IsZeroWidthBitfield),
        };
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
        if (builder.UsesPlacementCursor)
        {
            builder.Emit(WriteOpCode.FinishPlaced, -1, 0, alignment);
            return WriteProgramOutcome.Eligible(builder.Build(kind, composite.Name, composite));
        }

        bool knownTail = placement.TryFinish(alignment, out int padding);
        if (knownTail && placement.KnownOffset is long end && composite.Symbol.FixedSize is int size && end + padding != size)
        {
            return WriteProgramOutcome.NotSupported(location + ": " + ReadProgramCompiler.PlacementMismatch);
        }

        builder.Emit(WriteOpCode.FinishComposite, -1, knownTail ? padding : -1, alignment);
        return WriteProgramOutcome.Eligible(builder.Build(kind, composite.Name, composite));
    }

    /// <summary>
    ///     Emits one member's value, count, placement, encoding and capture, or returns why the member cannot be written:
    ///     a reason located at this member, or a nested composite's own reason unchanged.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member: a root field or a union member.</param>
    /// <param name="placement">The placement state; advanced past the member (a standalone field leaves it alone).</param>
    /// <returns>A reason, or <see langword="null"/> when the member was emitted.</returns>
    private string? EmitMember(WriteProgramBuilder builder, int index, string location, bool standalone, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        if (field.IsZeroWidthBitfield)
        {
            // A separator writes nothing; the cursor applies its placement. In a union it cannot be selected.
            if (!standalone)
            {
                builder.Emit(WriteOpCode.PlaceSeparator, index, 0, 0);
            }

            return null;
        }

        if (field.IsPromotedComposite)
        {
            return this.EmitPromoted(builder, index, location, standalone, ref placement);
        }

        if (field.BitSize > 0 && field.Array.Kind != CompiledArrayKind.Scalar)
        {
            return ReadProgramCompiler.Refuse(location, field, BitfieldArrays);
        }

        if (this.EmitValueField(builder, index, location) is { } unsized)
        {
            return unsized;
        }

        // The value: the root value or union selection, the member looked up in the struct's data (a failure from here to
        // the capture names the member, as the interpreter's field loop does), or the zero value padding is written with.
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

        string? reason = this.EmitCount(builder, index, location) ?? this.EmitMemberPlacement(builder, index, location, standalone, ref placement);
        reason ??= this.EmitWrite(builder, index, location, standalone);
        if (reason is not null)
        {
            return reason;
        }

        // The all-zero element that ends a terminated array follows its elements.
        if (field.Array.Kind == CompiledArrayKind.Terminated && !this.IsText(builder, index))
        {
            builder.Emit(WriteOpCode.WriteTerminator, index, field.FixedElementSize!.Value, 0);
        }

        this.EmitCompletion(builder, index, standalone, ref placement);
        this.EmitCapture(builder, index);
        builder.NotedMember = -1;
        return null;
    }

    /// <summary>
    ///     Emits where a member starts: a union member at the union's first byte (a bitfield there, and a root bitfield,
    ///     opening its own unit), a member of a struct with bitfields through the runtime cursor, and any other member of a
    ///     struct by its static placement; a root field is not placed.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member.</param>
    /// <param name="placement">The static placement state.</param>
    /// <returns>A reason when a static placement contradicts the compiled offset; otherwise <see langword="null"/>.</returns>
    private string? EmitMemberPlacement(WriteProgramBuilder builder, int index, string location, bool standalone, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        if (standalone)
        {
            if (builder.UnionEntries is not null)
            {
                builder.Emit(WriteOpCode.RewindToUnionStart, index, 0, 0);
            }

            if (field.BitSize > 0)
            {
                builder.Emit(WriteOpCode.OpenBitfieldUnit, index, 0, 0);
            }

            return null;
        }

        if (builder.UsesPlacementCursor)
        {
            builder.Emit(field.BitSize > 0 ? WriteOpCode.PlaceBitfield : WriteOpCode.PlaceMember, index, 0, 0);
            return null;
        }

        return this.EmitPlacement(builder, index, location, ref placement);
    }

    /// <summary>
    ///     Records where a placed member ended: the runtime cursor learns the position after a member that is not a bitfield
    ///     (a bitfield's unit was reserved when it opened), and a static placement advances past the member's size (or
    ///     restarts after a size the data decides).
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="standalone">Whether no composite places the member; nothing is recorded then.</param>
    /// <param name="placement">The static placement state.</param>
    private void EmitCompletion(WriteProgramBuilder builder, int index, bool standalone, ref ReadPlacement placement)
    {
        if (standalone)
        {
            return;
        }

        if (builder.UsesPlacementCursor)
        {
            if (builder.Fields[index].BitSize == 0)
            {
                builder.Emit(WriteOpCode.CompletePlacement, index, 0, 0);
            }
        }
        else
        {
            this.extents.AdvancePast(ref placement, builder.Fields[index]);
        }
    }

    /// <summary>
    ///     Chooses the field a member's value is encoded as: an unsized character array (<c>char name[]</c>) is one
    ///     terminated string, written through its terminated view; every other member is written as itself.
    /// </summary>
    /// <param name="builder">The program under construction; its <see cref="WriteProgramBuilder.ValueFields"/> entry is set.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitValueField(WriteProgramBuilder builder, int index, string location)
    {
        CompiledField field = builder.Fields[index];
        if (field.Array.Kind != CompiledArrayKind.Flexible)
        {
            return null;
        }

        CompiledField? view = field.IsCharElement
                                  ? field.SelectPointerTarget(0, CharacterFieldTypes.CstringType.Name, this.compilation.PointerSize)
                                  : field.IsWideCharElement
                                      ? field.SelectPointerTarget(0, CharacterFieldTypes.GetStringPointerHandlerKey(field.TypeSpelling), this.compilation.PointerSize)
                                      : null;
        if (view is null || !PrimitiveCodecs.IsVariableLengthType(view.TypeSpelling))
        {
            return ReadProgramCompiler.Refuse(location, field, UnsizedArrays);
        }

        builder.ValueFields[index] = view;
        return null;
    }

    /// <summary>
    ///     Emits the element count of an array member: a fixed count (every element of every dimension) checked against the
    ///     element limit, a runtime count evaluated in the write domain, or - for an array whose value decides its count -
    ///     the value's own count (a text buffer of one character, as the interpreter writes it). A scalar and an unsized
    ///     character array (one terminated string) have none.
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
            builder.Emit(WriteOpCode.EvaluateCount, index, builder.AddExpression(field.Array.CountExpression!, "array length for " + field.Name), 0);
            return null;
        case CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated:
            builder.Emit(WriteOpCode.SetCount, index, this.IsText(builder, index) ? 1 : -1, 0);
            return null;
        default:
            return null;
        }
    }

    /// <summary>Whether an array member is written as fixed-capacity text: its value is characters, or byte-counted text.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <returns>Whether <see cref="WriteOpCode.WriteText"/> writes it.</returns>
    private bool IsText(WriteProgramBuilder builder, int index)
        => builder.ValueFields[index].IsCharacterArray || (!builder.Fields[index].IsPointer && BoundedTextCodec.IsType(builder.Fields[index].TypeSpelling));

    /// <summary>
    ///     Emits where a member a struct places starts - nothing, a relative <see cref="WriteOpCode.Seek"/> over known
    ///     padding, or an <see cref="WriteOpCode.Align"/> - and, when the layout's build could not check it, its <c>@N</c>
    ///     assertion, by the rule the reader shares (<see cref="ReadPlacement.PlaceMember"/>): a member is placed by its
    ///     declaration, so an unsized <c>wchar name[]</c> keeps its element's alignment although its value is written
    ///     through a terminated view. A static placement that contradicts the layout's compiled offset is refused.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="placement">The placement state.</param>
    /// <returns>A reason when a statically known placement contradicts the compiled offset; otherwise <see langword="null"/>.</returns>
    private string? EmitPlacement(WriteProgramBuilder builder, int index, string location, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        bool contradicts = placement.PlaceMember(index, field, out ReadStep? step, out int? asserted);
        if (step is { } move)
        {
            builder.EmitPlacement(move);
        }

        if (contradicts)
        {
            return ReadProgramCompiler.Refuse(location, field, ReadProgramCompiler.PlacementMismatch);
        }

        if (asserted is int offset)
        {
            builder.Emit(WriteOpCode.CheckOffset, index, offset, 0);
        }

        return null;
    }

    /// <summary>
    ///     Emits a member's encoding step in the interpreter's order: a multidimensional array (character rows or leaves),
    ///     text, a numeric array (with its typed block path), any other array element by element; or a scalar by its kind.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member, which rules out the typed block path.</param>
    /// <returns>A reason located at this member, a nested composite's own reason, or <see langword="null"/>.</returns>
    private string? EmitWrite(WriteProgramBuilder builder, int index, string location, bool standalone)
    {
        CompiledField field = builder.Fields[index];
        CompiledField value = builder.ValueFields[index];
        if (field.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            bool text = this.IsText(builder, index);
            if (field.Array.Dimensions.Length > 1 && value.IsCharacterArray)
            {
                if (field.CodecId < 0)
                {
                    return ReadProgramCompiler.Refuse(location, field, NoWriter);
                }

                builder.Emit(WriteOpCode.WriteTextTable, index, builder.AddCodec(field.CodecId, field.Codec), 0);
                return null;
            }

            if (text && field.Array.Dimensions.Length <= 1)
            {
                if (field.CodecId < 0)
                {
                    return ReadProgramCompiler.Refuse(location, field, NoWriter);
                }

                builder.Emit(WriteOpCode.WriteText, index, builder.AddCodec(field.CodecId, field.Codec), 0);
                return null;
            }

            if (this.Classify(builder, field, location, field, out WriteElementKind elements, out int operand) is { } refused)
            {
                return refused;
            }

            WriteOpCode op = field.Array.Dimensions.Length > 1 ? WriteOpCode.WriteLeaves
                             : elements == WriteElementKind.Numeric ? WriteOpCode.WriteNumericArray
                             : WriteOpCode.WriteElements;
            builder.Emit(op, index, operand, op == WriteOpCode.WriteNumericArray ? (standalone ? 1 : 0) : (int)elements);
            return null;
        }

        if (value.BitSize > 0)
        {
            builder.Emit(WriteOpCode.WriteBitfield, index, 0, 0);
            return null;
        }

        if (this.Classify(builder, value, location, field, out WriteElementKind kind, out int scalar) is { } reason)
        {
            return reason;
        }

        switch (kind)
        {
        case WriteElementKind.Zeroes:
            builder.Emit(WriteOpCode.WriteZeroes, index, scalar, 0);
            break;
        case WriteElementKind.Pointer:
            builder.Emit(WriteOpCode.WritePointer, index, 0, 0);
            break;
        case WriteElementKind.Enum:
            builder.Emit(WriteOpCode.WriteEnum, index, scalar, builder.AddEnum(value.Enum!));
            break;
        case WriteElementKind.Composite:
            builder.Emit(WriteOpCode.WriteStruct, index, scalar, field.HasQualifiedPrefix ? builder.AddPrefix(field.QualifiedPrefix!) : -1);
            break;
        case WriteElementKind.Numeric:
            builder.Emit(WriteOpCode.WriteNumeric, index, scalar, 0);
            break;
        default:
            builder.Emit(WriteOpCode.WriteCodecValue, index, scalar, 0);
            break;
        }

        return null;
    }

    /// <summary>
    ///     Emits an anonymous promoted member, outside the member-noting context as the interpreter writes it. In a struct it
    ///     is placed like any member: a promoted struct is written by a program of its own over the parent's value, and a
    ///     promoted union stages the widest member the value supplies. As a union's member it is written standalone from the
    ///     union's first byte, a promoted union there needing a union value of its own.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether the member belongs to a union rather than a struct.</param>
    /// <param name="placement">The placement state.</param>
    /// <returns>A reason, or <see langword="null"/>.</returns>
    private string? EmitPromoted(WriteProgramBuilder builder, int index, string location, bool standalone, ref ReadPlacement placement)
    {
        CompiledField field = builder.Fields[index];
        CompiledCompositeType composite = field.Composite ?? this.compilation.SizeQueries.GetCompiledComposite((Struct)field.Declaration);
        if (this.EmitMemberPlacement(builder, index, location, standalone, ref placement) is { } misplaced)
        {
            return misplaced;
        }

        WriteProgramOutcome nested = composite.IsUnion
                                         ? this.cache.GetComposite(this.compilation, composite)
                                         : this.CompileStruct(composite, WriteProgramKind.Promoted, builder.Shape);
        if (nested.Program is not { } program)
        {
            return nested.Reason;
        }

        int prefix = field.HasQualifiedPrefix ? builder.AddPrefix(field.QualifiedPrefix!) : -1;
        builder.Emit(composite.IsUnion && !standalone ? WriteOpCode.WritePromotedUnion : WriteOpCode.WritePromotedStruct, index, builder.AddNested(program), prefix);
        this.EmitCompletion(builder, index, standalone, ref placement);
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
}
