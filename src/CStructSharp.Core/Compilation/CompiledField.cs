namespace CStructSharp.Compilation;

using System;
using System.Collections.Immutable;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>Stores one completely resolved field shape and all operation-time codec/layout facts.</summary>
internal sealed class CompiledField
{
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
        this.EffectiveField = effectiveField;
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
        this.SetCharacterFacts();

        // Resolve the codec identity once so no hot path re-derives the name and compares strings.
        // Only the 8-byte descriptor is stored; the name stays a computed property so wide layouts do not grow.
        this.Codec = this.PointerDepth > 0 || this.Type.Symbol.Kind is CompiledTypeKind.Struct or CompiledTypeKind.Union
                         ? PrimitiveCodec.None with { LayoutLittleEndian = layoutLittleEndian }
                         : this.Type.Symbol.IsCustomCodec
                             ? new PrimitiveCodec(PrimitiveCodecKind.Custom, (byte)Math.Min(this.Type.Symbol.FixedSize ?? 0, byte.MaxValue), layoutLittleEndian, '\0', layoutLittleEndian)
                             : PrimitiveCodec.Resolve(this.CodecName, layoutLittleEndian);
        this.IsFixedPoint = this.Codec.IsFixedPoint;
    }

    /// <summary>Derived copies keep the parent's resolved codec when the pointer depth is unchanged.</summary>
    private CompiledField(CompiledField parent, Field effectiveField, int alignment, int? fixedElementSize, CompiledArrayShape array, int? fixedStorageSize, bool isUnsizedCharacterArray, int? bitStorageSize, bool? bitStorageIsLittleEndian, int? fixedOffset, int bitOffset, int codecId)
    {
        this.Declaration = parent.Declaration;
        this.EffectiveField = effectiveField;
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
        this.Name = effectiveField.Name.Name;
        this.TypeSpelling = effectiveField.Type.Name;
        this.BitSize = effectiveField.BitSize;
        this.IsZeroWidthBitfield = effectiveField.HasBitfieldDeclarator && this.BitSize == 0;
        this.BitUnitSize = parent.BitUnitSize;
        this.BitRunBits = parent.BitRunBits;
        this.PointerDepth = effectiveField.PointerDepth;
        this.SetCharacterFacts();
        this.CapturesLayoutVariable = parent.CapturesLayoutVariable;
        this.Codec = this.PointerDepth == parent.PointerDepth
                         ? parent.Codec
                         : this.PointerDepth > 0
                             ? PrimitiveCodec.None with { LayoutLittleEndian = parent.LayoutLittleEndian }
                             : this.Type.Symbol.IsCustomCodec
                                 ? new PrimitiveCodec(PrimitiveCodecKind.Custom, (byte)Math.Min(this.Type.Symbol.FixedSize ?? 0, byte.MaxValue), parent.LayoutLittleEndian, '\0', parent.LayoutLittleEndian)
                                 : PrimitiveCodec.Resolve(this.CodecName, parent.LayoutLittleEndian);
        this.IsFixedPoint = this.Codec.IsFixedPoint;
    }

    public ImmutableArray<CompiledConditionalBranch> ConditionalBranches { get; internal set; } = [];

    /// <summary>
    ///     Whether an expression names a field of this nested struct field through a dotted path (<c>hdr.n</c>), so
    ///     its nested fields are published under <see cref="QualifiedPrefix"/> as well. A bool in the descriptor's
    ///     padding: a compiled field is copied per operation for array elements and pointer targets, so a reference
    ///     field here would cost every operation eight bytes.
    /// </summary>
    public bool HasQualifiedPrefix { get; internal set; }

    /// <summary>The prefix (<c>hdr.</c>) under which the nested fields are published; <see langword="null"/> for every other field.</summary>
    public string? QualifiedPrefix => this.HasQualifiedPrefix ? this.Declaration.Name.Name + "." : null;

    public ImmutableArray<string> VisibleNames { get; internal set; } = [];

    public ImmutableArray<int> CapturedLocalSlots { get; internal set; } = [];

    public ImmutableArray<int> RestoredLocalSlots { get; internal set; } = [];

    /// <summary>
    ///     Whether reading or writing this field must publish its value as a layout variable. False when no
    ///     expression in the layout can name it, so the capture allocation and dictionary write are skipped;
    ///     an operation whose supplied variables can still name it (<see cref="LayoutVariables.CaptureAll"/>)
    ///     overrides this.
    /// </summary>
    public bool CapturesLayoutVariable { get; internal set; } = true;

    public int Alignment { get; }

    public CompiledArrayShape Array { get; }

    public int BitOffset { get; }

    public bool? BitStorageIsLittleEndian { get; }

    public int? BitStorageSize { get; }

    public Field Declaration { get; }

    public Field EffectiveField { get; }

    public int? FixedArrayCount => this.Array.FixedCount;

    public int? FixedElementSize { get; }

    public int? FixedOffset { get; }

    public int? FixedStorageSize { get; }

    public bool IsUnsizedCharacterArray { get; }

    /// <summary>Whether numeric storage represents a fixed-point value rather than an integer layout variable.</summary>
    public bool IsFixedPoint { get; }

    public CStructElement? NamedElement => this.Type.Symbol.Declaration;

    /// <summary>The field's name; empty for unnamed padding (an anonymous bitfield or a <c>_</c> field).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Whether the field has no name and therefore no value in the result.</summary>
    public bool IsUnnamed => this.Name.Length == 0;

    /// <summary>The type as this view spells it: the declared spelling, or the terminated codec of a peeled string.</summary>
    public string TypeSpelling { get; private set; } = string.Empty;

    /// <summary>The bitfield width in bits, or 0 for a field that is not a bitfield.</summary>
    public int BitSize { get; private set; }

    /// <summary>
    ///     Whether this is an unnamed <c>: 0</c> declarator: no storage and no value, but the placement rule moves the
    ///     next bitfield to a boundary of the declared type (<see cref="BitStorageSize"/> is that type's size).
    /// </summary>
    public bool IsZeroWidthBitfield { get; private set; }

    /// <summary>
    ///     The size in bytes of the storage unit the compiled placement gave this bitfield, when its offset is
    ///     static; it equals <see cref="BitStorageSize"/> except for a packed SysV field that spans declared units.
    /// </summary>
    public int? BitUnitSize { get; private set; }

    /// <summary>
    ///     The bit length of the run of adjacent bitfields this field belongs to, measured from the run's first bit
    ///     with the packed SysV rule (contiguous bits; a separator rounds up). It depends only on the run's own
    ///     declarations, so the runtime cursor can clamp packed storage units to the run's bytes.
    /// </summary>
    public int BitRunBits { get; internal set; }

    /// <summary>How many pointer levels this view still has to follow before reaching its value.</summary>
    public int PointerDepth { get; private set; }

    /// <summary>Whether this view is still a stored pointer rather than the pointed-to value.</summary>
    public bool IsPointer => this.PointerDepth > 0;

    /// <summary>Whether each element is a one-byte <c>char</c>, so a fixed array becomes a string.</summary>
    public bool IsCharElement { get; private set; }

    /// <summary>Whether each element is a two-byte <c>wchar</c> (any byte order), so a fixed array becomes a string.</summary>
    public bool IsWideCharElement { get; private set; }

    /// <summary>
    ///     The type name shown for this field in debug records: the terminated codec for an unsized character array
    ///     (<c>cstring</c>, <c>string</c>, <c>string&lt;</c>, ...), otherwise the declared spelling.
    /// </summary>
    public string DisplayTypeSpelling =>
        this.Array.Kind == CompiledArrayKind.Flexible && this.IsCharacterArray
            ? CharacterFieldTypes.GetStringPointerHandlerKey(this.EffectiveField.Type)
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

    /// <summary>Whether the field is an inline struct or union member (<c>struct { ... } name;</c>), named or anonymous.</summary>
    public bool IsInlineComposite => this.Declaration is Struct;

    /// <summary>Whether the field is an anonymous inline composite whose members are promoted into the parent.</summary>
    public bool IsPromotedComposite => this.Declaration is Struct { Name.Name.Length: 0, };

    /// <summary>
    ///     The codec id of the delegate pair that reads and writes one element of this field, or
    ///     <see cref="PrimitiveCatalog.NoCodec"/> for a composite, a pointer (read by the pointer reader), and a
    ///     bare terminated-string target. The runtime's <c>CodecTable</c> is indexed by it.
    /// </summary>
    public int CodecId { get; }

    /// <summary>The codec id of the terminated-string handler behind a <c>char *</c>-style pointer, or <see cref="PrimitiveCatalog.NoCodec"/>.</summary>
    public int TerminatedCodecId { get; }

    /// <summary>Whether a primitive delegate pair reads and writes this field (false for composites and pointers).</summary>
    public bool HasCodec => this.CodecId != PrimitiveCatalog.NoCodec;

    /// <summary>Whether this field is a pointer to a terminated string (<c>char *</c> shorthand) whose target reads through a terminated handler.</summary>
    public bool HasTerminatedCodec => this.TerminatedCodecId != PrimitiveCatalog.NoCodec;

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

    /// <summary>
    ///     The name that decides whether consecutive bitfields share a storage unit: the storage codec's terminal
    ///     name, so an enum or flag bitfield shares the unit of its backing type (and of other enums on that type).
    /// </summary>
    public string BitUnitType => this.CodecName;

    /// <summary>The layout's neutral byte order, needed to resolve suffix-less codec spellings of derived fields.</summary>
    public bool LayoutLittleEndian => this.Codec.LayoutLittleEndian;

    /// <summary>
    ///     Creates an immutable view for one selected array element, peeling exactly one dimension: a
    ///     scalar view if this was the last remaining dimension (matching this method's original one-shot
    ///     behavior exactly for every 1-D array), or a still-array view of the remaining inner dimensions
    ///     otherwise. A caller addressing an N-dimensional array calls this once per supplied index.
    /// </summary>
    public CompiledField SelectArrayElement()
    {
        CompiledArrayShape nextShape = this.Array.PeelOuterDimension();
        bool isScalarResult = nextShape.Dimensions.IsEmpty;

        // A derived/peeled field's own ArrayCount is never inspected downstream - array-ness for this shape is
        // driven entirely by CompiledArrayShape (nextShape), not by this Field's own declaration data, which only
        // ever matters during the one-time CompileComposite pass this method never runs during.
        var field = new Field(
            this.EffectiveField.Type,
            this.EffectiveField.Name,
            Field.NoArray,
            this.EffectiveField.BitSize,
            this.PointerDepth);

        int? nextStorageSize = isScalarResult || this.FixedElementSize is not int elementSize
                                    ? this.FixedElementSize
                                    : nextShape.TotalFixedElementCount is int remainingCount
                                        ? checked(elementSize * remainingCount)
                                        : null;

        return new CompiledField(
            this,
            field,
            this.Alignment,
            this.FixedElementSize,
            nextShape,
            nextStorageSize,
            false,
            this.BitStorageSize,
            this.BitStorageIsLittleEndian,
            this.FixedOffset,
            this.BitOffset,
            this.CodecId);
    }

    /// <summary>Creates an immutable target view after explicit pointer accessors consume part of the shape.</summary>
    public CompiledField SelectPointerTarget(
        int remainingPointerDepth,
        string? terminatedCodecName,
        int pointerSize)
    {
        Identifier type = terminatedCodecName is null
                              ? this.EffectiveField.Type
                              : new Identifier(terminatedCodecName);
        var field = new Field(type, this.EffectiveField.Name, Field.NoArray, 0, remainingPointerDepth);
        bool targetIsTerminated = remainingPointerDepth == 0 && terminatedCodecName is not null;
        int alignment = remainingPointerDepth > 0 ? pointerSize : targetIsTerminated ? 1 : this.Type.Symbol.Alignment;
        int? elementSize = remainingPointerDepth > 0
                               ? pointerSize
                               : targetIsTerminated
                                   ? null
                                   : this.Type.Symbol.FixedSize;
        return new CompiledField(
            this,
            field,
            alignment,
            elementSize,
            CompiledArrayShape.Scalar,
            elementSize,
            false,
            null,
            null,
            null,
            0,
            targetIsTerminated ? this.TerminatedCodecId : this.CodecId);
    }

    /// <summary>Returns the same descriptor with its compiled placement facts attached.</summary>
    public CompiledField WithPlacement(int? fixedOffset, int bitOffset, int? bitUnitSize = null)
    {
        var placed = new CompiledField(
            this,
            this.EffectiveField,
            this.Alignment,
            this.FixedElementSize,
            this.Array,
            this.FixedStorageSize,
            this.IsUnsizedCharacterArray,
            this.BitStorageSize,
            this.BitStorageIsLittleEndian,
            fixedOffset,
            bitOffset,
            this.CodecId);
        placed.BitUnitSize = bitUnitSize ?? this.BitUnitSize;
        return placed;
    }

    private void SetCharacterFacts()
    {
        this.IsCharElement = this.TypeSpelling == CharacterFieldTypes.CharType.Name;
        this.IsWideCharElement = this.TypeSpelling == CharacterFieldTypes.WcharType.Name ||
                                 this.TypeSpelling == CharacterFieldTypes.WcharBigEndianType.Name ||
                                 this.TypeSpelling == CharacterFieldTypes.WcharLittleEndianType.Name;
    }
}
