namespace CStructSharp.Compilation;

using System;
using System.Collections.Immutable;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>Stores one completely resolved field shape and all operation-time codec/layout facts.</summary>
/// <remarks>
///     Immutable once constructed. Compilation first builds a field from its declaration, then
///     <see cref="WithPlacement"/> builds the composite's member from it with the facts that depend on the other
///     members: offsets, the bitfield run and the conditional arms. Array elements and pointer targets are views
///     built from a member during an operation.
/// </remarks>
internal sealed class CompiledField
{
    // One reference for the rare @count facts: descriptors are copied per operation, so each field costs every read.
    private CountedPointerTarget? countedTarget;

    /// <summary>Creates the compiled view of a declared field from its resolved type, codecs, and storage facts.</summary>
    /// <remarks>
    ///     <paramref name="effectiveField"/> is the declaration with its type resolved (a typedef's terminal type, its array
    ///     shape and pointer depth merged in); its name, type spelling, width and pointer depth are read, not kept.
    /// </remarks>
    /// <param name="declaration">The field declaration as written in the layout.</param>
    /// <param name="effectiveField">The declaration with its type resolved through typedefs.</param>
    /// <param name="type">The resolved type the field's values have.</param>
    /// <param name="codecId">
    ///     The codec id of the delegate pair for one element, or <see cref="PrimitiveCatalog.NoCodec"/>.
    /// </param>
    /// <param name="terminatedCodecId">
    ///     The codec id of the terminated-string handler behind a <c>char *</c>-style pointer, or
    ///     <see cref="PrimitiveCatalog.NoCodec"/>.
    /// </param>
    /// <param name="alignment">The alignment in bytes.</param>
    /// <param name="fixedElementSize">
    ///     One element's size in bytes, or <see langword="null"/> when data decides it.
    /// </param>
    /// <param name="array">The declared array shape, or <see cref="CompiledArrayShape.Scalar"/>.</param>
    /// <param name="fixedStorageSize">
    ///     The whole storage size in bytes, or <see langword="null"/> when data decides it.
    /// </param>
    /// <param name="isUnsizedCharacterArray">Whether the field is a character array declared without a length.</param>
    /// <param name="bitStorageSize">The bitfield's declared storage size in bytes, or <see langword="null"/>.</param>
    /// <param name="bitStorageIsLittleEndian">The bitfield storage byte order, or <see langword="null"/>.</param>
    /// <param name="fixedOffset">The byte offset from the composite's start, when known at compile time.</param>
    /// <param name="bitOffset">The bit offset within the storage unit.</param>
    /// <param name="layoutLittleEndian">
    ///     The layout's neutral byte order, used for codec spellings without a suffix.
    /// </param>
    public CompiledField(
        Field declaration,
        Field effectiveField,
        CompiledTypeReference type,
        int codecId,
        int terminatedCodecId,
        int alignment,
        int? fixedElementSize,
        CompiledArrayShape array,
        int? fixedStorageSize,
        bool isUnsizedCharacterArray,
        int? bitStorageSize,
        bool? bitStorageIsLittleEndian,
        int? fixedOffset,
        int bitOffset,
        bool layoutLittleEndian)
    {
        this.Declaration = declaration;
        this.Type = type;
        this.CodecId = codecId;
        this.TerminatedCodecId = terminatedCodecId;
        this.Alignment = alignment;
        this.FixedElementSize = fixedElementSize;
        this.Array = array;
        this.FixedStorageSize = fixedStorageSize;
        this.IsUnsizedCharacterArray = isUnsizedCharacterArray;
        this.BitStorageSize = bitStorageSize;
        this.BitStorageIsLittleEndian = bitStorageIsLittleEndian;
        this.FixedOffset = fixedOffset;
        this.BitOffset = bitOffset;
        this.Name = effectiveField.Name.Name;
        this.TypeSpelling = effectiveField.Type.Name;
        this.BitSize = effectiveField.BitSize;
        this.IsZeroWidthBitfield = effectiveField.HasBitfieldDeclarator && this.BitSize == 0;
        this.PointerDepth = effectiveField.PointerDepth;
        this.IsCharElement = IsCharSpelling(this.TypeSpelling);
        this.IsWideCharElement = IsWideCharSpelling(this.TypeSpelling);

        // Resolve the codec identity once so no hot path re-derives the name and compares strings.
        // Only the 8-byte descriptor is stored; the name stays a computed property so wide layouts do not grow.
        this.Codec = this.PointerDepth > 0 || this.Type.Symbol.Kind is CompiledTypeKind.Struct or CompiledTypeKind.Union
                         ? PrimitiveCodec.None with { LayoutLittleEndian = layoutLittleEndian }
                         : this.Type.Symbol.IsCustomCodec
                             ? new PrimitiveCodec(PrimitiveCodecKind.Custom, (byte)Math.Min(this.Type.Symbol.FixedSize ?? 0, byte.MaxValue), layoutLittleEndian, '\0', layoutLittleEndian)
                             : PrimitiveCodec.Resolve(this.CodecName, layoutLittleEndian);
        this.IsFixedPoint = this.Codec.IsFixedPoint;
    }

    /// <summary>
    ///     A field built from <paramref name="parent"/>: a placed member, an array element or a pointer target. The
    ///     parameters are the facts that differ; the others are the parent's, and the resolved codec is kept when the
    ///     pointer depth is unchanged.
    /// </summary>
    /// <param name="parent">The field this one is built from.</param>
    /// <param name="typeSpelling">The type as this view spells it.</param>
    /// <param name="bitSize">The bitfield width.</param>
    /// <param name="isZeroWidthBitfield">Whether this is a <c>: 0</c> separator.</param>
    /// <param name="pointerDepth">The pointer levels still to follow.</param>
    /// <param name="alignment">The alignment in bytes.</param>
    /// <param name="fixedElementSize">One element's size in bytes, when fixed.</param>
    /// <param name="array">The remaining array shape.</param>
    /// <param name="fixedStorageSize">The whole storage size in bytes, when fixed.</param>
    /// <param name="isUnsizedCharacterArray">Whether this is an unsized character array.</param>
    /// <param name="bitStorageSize">The declared bitfield storage size in bytes.</param>
    /// <param name="bitStorageIsLittleEndian">The bitfield storage byte order.</param>
    /// <param name="fixedOffset">The offset from the composite's start, when known at compile time.</param>
    /// <param name="bitOffset">The bit offset within the storage unit.</param>
    /// <param name="codecId">The codec.</param>
    /// <param name="bitUnitSize">The placed storage unit size in bytes.</param>
    /// <param name="bitRunBits">The bit length of the field's bitfield run.</param>
    /// <param name="memberIndex">The field's position among its composite's members.</param>
    /// <param name="conditionalBranches">The compiled arms the field sits in.</param>
    private CompiledField(
        CompiledField parent,
        string typeSpelling,
        int bitSize,
        bool isZeroWidthBitfield,
        int pointerDepth,
        int alignment,
        int? fixedElementSize,
        CompiledArrayShape array,
        int? fixedStorageSize,
        bool isUnsizedCharacterArray,
        int? bitStorageSize,
        bool? bitStorageIsLittleEndian,
        int? fixedOffset,
        int bitOffset,
        int codecId,
        int? bitUnitSize,
        int bitRunBits,
        int memberIndex,
        ImmutableArray<CompiledConditionalBranch> conditionalBranches)
    {
        this.Declaration = parent.Declaration;
        this.Type = parent.Type;
        this.CodecId = codecId;
        this.TerminatedCodecId = parent.TerminatedCodecId;
        this.Alignment = alignment;
        this.FixedElementSize = fixedElementSize;
        this.Array = array;
        this.FixedStorageSize = fixedStorageSize;
        this.IsUnsizedCharacterArray = isUnsizedCharacterArray;
        this.BitStorageSize = bitStorageSize;
        this.BitStorageIsLittleEndian = bitStorageIsLittleEndian;
        this.FixedOffset = fixedOffset;
        this.BitOffset = bitOffset;
        this.Name = parent.Name;
        this.TypeSpelling = typeSpelling;
        this.BitSize = bitSize;
        this.IsZeroWidthBitfield = isZeroWidthBitfield;
        this.BitUnitSize = bitUnitSize;
        this.BitRunBits = bitRunBits;
        this.MemberIndex = memberIndex;
        this.ConditionalBranches = conditionalBranches;
        this.PointerDepth = pointerDepth;
        this.IsCharElement = IsCharSpelling(typeSpelling);
        this.IsWideCharElement = IsWideCharSpelling(typeSpelling);
        this.CapturesLayoutVariable = parent.CapturesLayoutVariable;
        this.HasQualifiedPrefix = parent.HasQualifiedPrefix;
        this.AssertedOffset = parent.AssertedOffset;
        this.countedTarget = parent.countedTarget;
        this.Codec = this.PointerDepth == parent.PointerDepth
                         ? parent.Codec
                         : this.PointerDepth > 0
                             ? PrimitiveCodec.None with { LayoutLittleEndian = parent.LayoutLittleEndian }
                             : this.Type.Symbol.IsCustomCodec
                                 ? new PrimitiveCodec(PrimitiveCodecKind.Custom, (byte)Math.Min(this.Type.Symbol.FixedSize ?? 0, byte.MaxValue), parent.LayoutLittleEndian, '\0', parent.LayoutLittleEndian)
                                 : PrimitiveCodec.Resolve(this.CodecName, parent.LayoutLittleEndian);
        this.IsFixedPoint = this.Codec.IsFixedPoint;
    }

    /// <summary>Whether the field sits in an arm of an <c>if</c> or <c>switch</c>, so the data decides whether it is present.</summary>
    public bool IsConditional => this.Declaration.IsConditional;

    /// <summary>
    ///     The compiled arms this member sits in, outermost first; empty for an unconditional field, and for an array
    ///     element or pointer target, which is read only once its member was selected.
    /// </summary>
    public ImmutableArray<CompiledConditionalBranch> ConditionalBranches { get; } = ImmutableArray<CompiledConditionalBranch>.Empty;

    /// <summary>
    ///     The field's position among its composite's members, which indexes the composite's
    ///     <see cref="CompiledCompositeType.ConditionalScope"/>; 0 for a root field.
    /// </summary>
    public int MemberIndex { get; }

    /// <summary>
    ///     Whether an expression names a field of this nested struct field through a dotted path (<c>hdr.n</c>), so
    ///     its nested fields are published under <see cref="QualifiedPrefix"/> as well. A bool in the descriptor's
    ///     padding: a compiled field is copied per operation for array elements and pointer targets, so a reference
    ///     field here would cost every operation eight bytes.
    /// </summary>
    public bool HasQualifiedPrefix { get; init; }

    /// <summary>The prefix (<c>hdr.</c>) under which the nested fields are published; <see langword="null"/> for every other field.</summary>
    public string? QualifiedPrefix => this.HasQualifiedPrefix ? this.Declaration.Name.Name + "." : null;

    /// <summary>
    ///     Whether reading or writing this field must publish its value as a layout variable. False when no
    ///     expression in the layout can name it, so the capture allocation and dictionary write are skipped.
    /// </summary>
    public bool CapturesLayoutVariable { get; init; }

    /// <summary>
    ///     The element count of the pointer's final target, from a <c>@count(N)</c> suffix: the target is N
    ///     consecutive values of the pointed-to type, read as an array. <see langword="null"/> for a pointer to one
    ///     value and for every non-pointer field. Derived views (pointer levels, placement) keep it.
    /// </summary>
    public CompiledArrayShape? PointerElements
    {
        get => this.countedTarget?.Elements;
        internal init => this.countedTarget = value is null ? null : new CountedPointerTarget(value);
    }

    /// <summary>Whether the pointer's final target is an array counted by <c>@count(N)</c>.</summary>
    public bool HasCountedTarget => this.countedTarget is not null;

    /// <summary>
    ///     Whether a struct reader follows this pointer field after the struct's last field instead of in place: every
    ///     named pointer and pointer array read by a struct (not a union), except an opaque <c>void *</c>. The runtime
    ///     and the generated readers use this same rule, so both report the same first failure.
    /// </summary>
    public bool FollowsAfterStruct => this.PointerDepth > 0 && !this.IsUnnamed && this.Array.Kind != CompiledArrayKind.Flexible &&
                                      !(this.PointerDepth == 1 && this.Type.TerminalName == "void");

    /// <summary>The field's alignment in bytes, applied when its offset is placed inside the composite.</summary>
    public int Alignment { get; }

    /// <summary>
    ///     The array dimensions this view still has; <see cref="CompiledArrayShape.Scalar"/> for a single value.
    /// </summary>
    public CompiledArrayShape Array { get; }

    /// <summary>
    ///     The offset in bits of a bitfield inside its storage unit; 0 for a field that is not a bitfield.
    /// </summary>
    public int BitOffset { get; }

    /// <summary>
    ///     The byte order of the bitfield's storage unit, or <see langword="null"/> for a field that is not a bitfield.
    /// </summary>
    public bool? BitStorageIsLittleEndian { get; }

    /// <summary>
    ///     The size in bytes of the bitfield's declared storage type, or <see langword="null"/> for a field that is not
    ///     a bitfield.
    /// </summary>
    public int? BitStorageSize { get; }

    /// <summary>The field declaration as written in the layout, before its type was resolved.</summary>
    public Field Declaration { get; }

    /// <summary>
    ///     The size in bytes of one element (or of the scalar value), or <see langword="null"/> when data decides it.
    /// </summary>
    public int? FixedElementSize { get; }

    /// <summary>
    ///     The field's offset in bytes from the start of its struct or union, or <see langword="null"/> when an earlier
    ///     variable-size member means only an operation knows it.
    /// </summary>
    public int? FixedOffset { get; }

    /// <summary>
    ///     The field's <c>@N</c> offset assertion in bytes from the start of its struct or union, evaluated once when the
    ///     layout is built; <see langword="null"/> without one. See <see cref="OffsetAssertion"/>.
    /// </summary>
    public int? AssertedOffset { get; init; }

    /// <summary>
    ///     The size in bytes of the field's whole storage (all array elements), or <see langword="null"/> when it
    ///     depends on data such as a runtime count or a terminator.
    /// </summary>
    public int? FixedStorageSize { get; }

    /// <summary>
    ///     Whether the field is a character array declared without a length (<c>char name[]</c>), which reads as a
    ///     terminated string.
    /// </summary>
    public bool IsUnsizedCharacterArray { get; }

    /// <summary>Whether numeric storage represents a fixed-point value rather than an integer layout variable.</summary>
    public bool IsFixedPoint { get; }

    /// <summary>
    ///     The declaration of the struct, union, or enum the field's resolved type refers to, or
    ///     <see langword="null"/> for a built-in primitive.
    /// </summary>
    public CStructElement? NamedElement => this.Type.Symbol.Declaration;

    /// <summary>The field's name; empty for unnamed padding (an anonymous bitfield or a <c>_</c> field).</summary>
    public string Name { get; }

    /// <summary>Whether the field has no name and therefore no value in the result.</summary>
    public bool IsUnnamed => this.Name.Length == 0;

    /// <summary>The type as this view spells it: the declared spelling, or the terminated codec of a peeled string.</summary>
    public string TypeSpelling { get; }

    /// <summary>The bitfield width in bits, or 0 for a field that is not a bitfield.</summary>
    public int BitSize { get; }

    /// <summary>
    ///     Whether this is an unnamed <c>: 0</c> declarator: no storage and no value, but the placement rule moves the
    ///     next bitfield to a boundary of the declared type (<see cref="BitStorageSize"/> is that type's size).
    /// </summary>
    public bool IsZeroWidthBitfield { get; }

    /// <summary>
    ///     The size in bytes of the storage unit the compiled placement gave this bitfield, when its offset is
    ///     static; it equals <see cref="BitStorageSize"/> except for a packed SysV field that spans declared units.
    /// </summary>
    public int? BitUnitSize { get; }

    /// <summary>
    ///     The bit length of the run of adjacent bitfields this field belongs to, measured from the run's first bit
    ///     with the packed SysV rule (contiguous bits, ending at a separator). It depends only on the run's own
    ///     declarations, so the runtime cursor can clamp packed storage units to the run's bytes.
    /// </summary>
    public int BitRunBits { get; }

    /// <summary>How many pointer levels this view still has to follow before reaching its value.</summary>
    public int PointerDepth { get; }

    /// <summary>Whether this view is still a stored pointer rather than the pointed-to value.</summary>
    public bool IsPointer => this.PointerDepth > 0;

    /// <summary>Whether each element is a one-byte <c>char</c>, so a fixed array becomes a string.</summary>
    public bool IsCharElement { get; }

    /// <summary>Whether each element is a two-byte <c>wchar</c> (any byte order), so a fixed array becomes a string.</summary>
    public bool IsWideCharElement { get; }

    /// <summary>
    ///     The type name shown for this field in debug records: the terminated codec for an unsized character array
    ///     (<c>cstring</c>, <c>string</c>, <c>string&lt;</c>, ...), otherwise the declared spelling.
    /// </summary>
    public string DisplayTypeSpelling =>
        this.Array.Kind == CompiledArrayKind.Flexible && this.IsCharacterArray
            ? CharacterFieldTypes.GetStringPointerHandlerKey(this.TypeSpelling)
            : this.TypeSpelling;

    /// <summary>Whether this view is an array of characters that reads as a string rather than a list.</summary>
    public bool IsCharacterArray => !this.IsPointer && (this.IsCharElement || this.IsWideCharElement);

    /// <summary>The strict UTF-16 encoding of a wide-character element with an explicit byte-order suffix, or <see langword="null"/> when it follows the layout's byte order.</summary>
    public Encoding? ExplicitWideCharacterEncoding =>
        this.TypeSpelling == CharacterFieldTypes.WcharBigEndianType.Name
            ? PrimitiveCodecs.StrictUtf16BigEndianEncoding
            : this.TypeSpelling == CharacterFieldTypes.WcharLittleEndianType.Name
                ? PrimitiveCodecs.StrictUtf16LittleEndianEncoding
                : null;

    /// <summary>The nested struct or union read by value through this field, or <see langword="null"/> for a primitive, enum, or pointer view.</summary>
    public CompiledCompositeType? Composite => this.PointerDepth == 0 ? this.Type.Symbol.Definition as CompiledCompositeType : null;

    /// <summary>The struct or union a pointer view ultimately targets, or <see langword="null"/> when the target is not a composite.</summary>
    public CompiledCompositeType? TargetComposite => this.Type.Symbol.Definition as CompiledCompositeType;

    /// <summary>The enum or flag type read through this field (also when it is a bitfield), or <see langword="null"/>.</summary>
    public CompiledEnumType? Enum => this.PointerDepth == 0 ? this.Type.Symbol.Definition as CompiledEnumType : null;

    /// <summary>
    ///     Gets why this field cannot be used in a layout expression, as the phrase after "is" (<c>text</c>,
    ///     <c>an array</c>, <c>a struct</c>, ...), or <see langword="null"/> when its value is an integer: an integer,
    ///     character, <c>bool</c>, enum or pointer scalar, whose value (the character's code, 1 or 0, the enum's number,
    ///     the pointer's stored address) is what an expression reads. A custom codec's scalar is allowed: the value it
    ///     decodes decides at run time.
    /// </summary>
    public string? NotANumberReason
    {
        get
        {
            if (this.Array.Kind != CompiledArrayKind.Scalar)
            {
                return this.IsCharacterArray || this.Codec.IsBoundedText ? "text" : "an array";
            }

            if (this.PointerDepth > 0 || this.Enum is not null)
            {
                return null;
            }

            if (this.Composite is { } composite)
            {
                return composite.IsUnion ? "a union" : "a struct";
            }

            return this.Codec.Kind switch
            {
                PrimitiveCodecKind.Bool or PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Int8 or PrimitiveCodecKind.Char or
                    PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Utf16LeUnit or
                    PrimitiveCodecKind.Utf16BeUnit or PrimitiveCodecKind.WChar or PrimitiveCodecKind.Int16 or PrimitiveCodecKind.UInt16 or
                    PrimitiveCodecKind.Int24 or PrimitiveCodecKind.UInt24 or PrimitiveCodecKind.Int32 or PrimitiveCodecKind.UInt32 or
                    PrimitiveCodecKind.Int48 or PrimitiveCodecKind.UInt48 or PrimitiveCodecKind.Int64 or PrimitiveCodecKind.UInt64 or
                    PrimitiveCodecKind.Int128 or PrimitiveCodecKind.UInt128 or PrimitiveCodecKind.ULeb128_32 or PrimitiveCodecKind.ULeb128_64 or
                    PrimitiveCodecKind.SLeb128_32 or PrimitiveCodecKind.SLeb128_64 => null,

                // A custom codec decides its own value type: an integer it decodes is a number, anything else is not.
                PrimitiveCodecKind.Custom => null,
                PrimitiveCodecKind.Float16 or PrimitiveCodecKind.Float32 or PrimitiveCodecKind.Float64 => "a floating-point value",
                PrimitiveCodecKind.Fixed16_16 or PrimitiveCodecKind.UFixed16_16 or PrimitiveCodecKind.Fixed2_30 or PrimitiveCodecKind.UFixed8_8 => "a fixed-point value",
                PrimitiveCodecKind.Uuid or PrimitiveCodecKind.Guid => "a UUID",
                PrimitiveCodecKind.TerminatedAscii or PrimitiveCodecKind.TerminatedUtf8 or PrimitiveCodecKind.TerminatedUtf16 => "text",
                _ => "a value that is not an integer",
            };
        }
    }

    /// <summary>Whether the field is an inline struct or union member (<c>struct { ... } name;</c>), named or anonymous.</summary>
    public bool IsInlineComposite => this.Declaration is Struct;

    /// <summary>Whether the field is an anonymous inline composite whose members are promoted into the parent.</summary>
    public bool IsPromotedComposite => this.Declaration is Struct { Name.Name.Length: 0, };

    /// <summary>
    ///     Whether the field is a row of a multidimensional array selected by fewer indices than it has dimensions
    ///     (<c>matrix[1]</c> of <c>uint8 matrix[3][4]</c>). A row keeps the shape it has inside the whole array's
    ///     value - a list - rather than the typed array a declared one-dimensional array reads as.
    /// </summary>
    public bool IsArrayRow { get; private init; }

    /// <summary>
    ///     The codec id of the delegate pair that reads and writes one element of this field, or
    ///     <see cref="PrimitiveCatalog.NoCodec"/> for a composite, a pointer (read by the pointer reader), and a
    ///     bare terminated-string target. The runtime's <c>CodecTable</c> is indexed by it.
    /// </summary>
    public int CodecId { get; }

    /// <summary>The codec id of the terminated-string handler behind a <c>char *</c>-style pointer, or <see cref="PrimitiveCatalog.NoCodec"/>.</summary>
    public int TerminatedCodecId { get; }

    /// <summary>Whether this field is a pointer to a terminated string (<c>char *</c> shorthand) whose target reads through a terminated handler.</summary>
    public bool HasTerminatedCodec => this.TerminatedCodecId != PrimitiveCatalog.NoCodec;

    /// <summary>
    ///     The resolved type the field's values have, after typedefs; pointer and array levels are separate.
    /// </summary>
    public CompiledTypeReference Type { get; }

    /// <summary>The primitive codec vocabulary name (for example <c>uint32&lt;</c>), <c>pointer</c>, or a composite's own name.</summary>
    public string CodecName
    {
        get
        {
            if (this.PointerDepth > 0)
            {
                return "pointer";
            }

            return this.Type.Symbol.Kind switch
            {
                CompiledTypeKind.Enum when this.Type.Symbol.Definition is CompiledEnumType enm =>
                    enm.Underlying.TerminalName,
                CompiledTypeKind.Struct or CompiledTypeKind.Union => this.Type.Symbol.Name,
                _ => this.Type.TerminalName,
            };
        }
    }

    /// <summary>Compile-time codec identity; <see cref="PrimitiveCodec.None"/> for pointers and composites.</summary>
    public PrimitiveCodec Codec { get; }

    /// <summary>The layout's neutral byte order, needed to resolve suffix-less codec spellings of derived fields.</summary>
    public bool LayoutLittleEndian => this.Codec.LayoutLittleEndian;

    /// <summary>
    ///     Creates an immutable view for one selected array element, peeling exactly one dimension: a
    ///     scalar view if this was the last remaining dimension (always the case for a one-dimensional array), or a
    ///     still-array view of the remaining inner dimensions
    ///     otherwise. A caller addressing an N-dimensional array calls this once per supplied index.
    /// </summary>
    /// <returns>
    ///     The element view, whose storage size is one element (scalar result) or the remaining dimensions' bytes.
    /// </returns>
    public CompiledField SelectArrayElement()
    {
        CompiledArrayShape nextShape = this.Array.PeelOuterDimension();
        bool isScalarResult = nextShape.Dimensions.IsEmpty;

        int? nextStorageSize = isScalarResult || this.FixedElementSize is not int elementSize
                                    ? this.FixedElementSize
                                    : nextShape.TotalFixedElementCount is int remainingCount
                                        ? checked(elementSize * remainingCount)
                                        : null;

        return new CompiledField(
            this,
            this.TypeSpelling,
            this.BitSize,
            false,
            this.PointerDepth,
            this.Alignment,
            this.FixedElementSize,
            nextShape,
            nextStorageSize,
            false,
            this.BitStorageSize,
            this.BitStorageIsLittleEndian,
            this.FixedOffset,
            this.BitOffset,
            this.CodecId,
            this.BitUnitSize,
            this.BitRunBits,
            this.MemberIndex,
            ImmutableArray<CompiledConditionalBranch>.Empty)
        {
            IsArrayRow = !isScalarResult,
        };
    }

    /// <summary>
    ///     The scalar view of one element of a counted target (<c>uint8</c> for <c>uint8 *iv @count(n)</c>), created
    ///     on first use and shared by every derived view of the field.
    /// </summary>
    /// <param name="pointerSize">The layout's pointer width in bytes.</param>
    /// <returns>The element view with its own codec, enum, or composite.</returns>
    /// <exception cref="InvalidOperationException">The field has no counted target.</exception>
    public CompiledField CountedElement(int pointerSize)
    {
        CountedPointerTarget target = this.countedTarget ?? throw new InvalidOperationException("The field has no counted target: " + this.Name);

        // A benign race may create the view twice; both are equal.
        return target.Element ??= this.SelectPointerTarget(0, null, pointerSize);
    }

    /// <summary>Creates an immutable target view after explicit pointer accessors consume part of the shape.</summary>
    /// <param name="remainingPointerDepth">
    ///     The pointer levels the target view still has to follow; 0 selects the pointed-to value itself.
    /// </param>
    /// <param name="terminatedCodecName">
    ///     The terminated-string codec (<c>cstring</c>, ...) the final target reads through, or <see langword="null"/>
    ///     to read it as the declared type.
    /// </param>
    /// <param name="pointerSize">
    ///     The layout's pointer width in bytes, the element size of a view that is still a pointer.
    /// </param>
    /// <returns>A scalar view of the target; it has no fixed offset or bitfield storage of its own.</returns>
    public CompiledField SelectPointerTarget(
        int remainingPointerDepth,
        string? terminatedCodecName,
        int pointerSize)
    {
        bool targetIsTerminated = remainingPointerDepth == 0 && terminatedCodecName is not null;
        int alignment = remainingPointerDepth > 0 ? pointerSize : targetIsTerminated ? 1 : this.Type.Symbol.Alignment;
        int? elementSize = remainingPointerDepth > 0
                               ? pointerSize
                               : targetIsTerminated
                                   ? null
                                   : this.Type.Symbol.FixedSize;
        return new CompiledField(
            this,
            terminatedCodecName ?? this.TypeSpelling,
            0,
            false,
            remainingPointerDepth,
            alignment,
            elementSize,
            CompiledArrayShape.Scalar,
            elementSize,
            false,
            null,
            null,
            null,
            0,
            targetIsTerminated ? this.TerminatedCodecId : this.CodecId,
            this.BitUnitSize,
            this.BitRunBits,
            this.MemberIndex,
            ImmutableArray<CompiledConditionalBranch>.Empty);
    }

    /// <summary>Builds the composite's member from this field with the facts that depend on the other members.</summary>
    /// <param name="memberIndex">The member's position in its composite.</param>
    /// <param name="fixedOffset">The offset from the composite's start, or <see langword="null"/> when only an operation knows it.</param>
    /// <param name="bitOffset">The bit offset within the storage unit.</param>
    /// <param name="bitUnitSize">The placed storage unit size in bytes, or <see langword="null"/> to keep this field's.</param>
    /// <param name="bitRunBits">The bit length of the field's bitfield run; 0 outside a run.</param>
    /// <param name="conditionalBranches">The compiled arms the member sits in.</param>
    /// <returns>The member.</returns>
    public CompiledField WithPlacement(int memberIndex, int? fixedOffset, int bitOffset, int? bitUnitSize, int bitRunBits, ImmutableArray<CompiledConditionalBranch> conditionalBranches)
    {
        return new CompiledField(
            this,
            this.TypeSpelling,
            this.BitSize,
            this.IsZeroWidthBitfield,
            this.PointerDepth,
            this.Alignment,
            this.FixedElementSize,
            this.Array,
            this.FixedStorageSize,
            this.IsUnsizedCharacterArray,
            this.BitStorageSize,
            this.BitStorageIsLittleEndian,
            fixedOffset,
            bitOffset,
            this.CodecId,
            bitUnitSize ?? this.BitUnitSize,
            bitRunBits,
            memberIndex,
            conditionalBranches);
    }

    /// <summary>Whether a type spelling is the one-byte <c>char</c>.</summary>
    private static bool IsCharSpelling(string typeSpelling) => typeSpelling == CharacterFieldTypes.CharType.Name;

    /// <summary>Whether a type spelling is a two-byte <c>wchar</c> in any byte order.</summary>
    private static bool IsWideCharSpelling(string typeSpelling)
        => typeSpelling == CharacterFieldTypes.WcharType.Name ||
           typeSpelling == CharacterFieldTypes.WcharBigEndianType.Name ||
           typeSpelling == CharacterFieldTypes.WcharLittleEndianType.Name;
}
