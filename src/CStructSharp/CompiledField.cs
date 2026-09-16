namespace CStructSharp;

using System;
using System.Collections.Immutable;
using System.IO;
using CStructSharp.Structure;

/// <summary>Stores one completely resolved field shape and all operation-time codec/layout facts.</summary>
internal sealed class CompiledField
{
    public CompiledField(
        Field declaration,
        Field effectiveField,
        CompiledTypeReference type,
        Func<Stream, object>? reader,
        Action<Stream, object>? writer,
        Func<Stream, object>? terminatedReader,
        Action<Stream, object>? terminatedWriter,
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
        this.Reader = reader;
        this.Writer = writer;
        this.TerminatedReader = terminatedReader;
        this.TerminatedWriter = terminatedWriter;
        this.Alignment = alignment;
        this.FixedElementSize = fixedElementSize;
        this.Array = array;
        this.FixedStorageSize = fixedStorageSize;
        this.IsUnsizedCharacterArray = isUnsizedCharacterArray;
        this.BitStorageSize = bitStorageSize;
        this.BitStorageIsLittleEndian = bitStorageIsLittleEndian;
        this.FixedOffset = fixedOffset;
        this.BitOffset = bitOffset;

        // Resolve the codec identity once (E1.5): every hot path used to re-derive the name and compare strings.
        // Only the 8-byte descriptor is stored; the name stays a computed property so wide layouts do not grow.
        this.Codec = this.PointerDepth > 0 || this.Type.Symbol.Kind is CompiledTypeKind.Struct or CompiledTypeKind.Union
                         ? PrimitiveCodec.None with { LayoutLittleEndian = layoutLittleEndian }
                         : this.Type.Symbol.IsCustomCodec
                             ? new PrimitiveCodec(PrimitiveCodecKind.Custom, (byte)Math.Min(this.Type.Symbol.FixedSize ?? 0, byte.MaxValue), layoutLittleEndian, '\0', layoutLittleEndian)
                             : PrimitiveCodec.Resolve(this.CodecName, layoutLittleEndian);
        this.IsFixedPoint = this.Codec.IsFixedPoint;
    }

    /// <summary>Derived copies keep the parent's resolved codec when the pointer depth is unchanged.</summary>
    private CompiledField(CompiledField parent, Field effectiveField, int alignment, int? fixedElementSize, CompiledArrayShape array, int? fixedStorageSize, bool isUnsizedCharacterArray, int? bitStorageSize, bool? bitStorageIsLittleEndian, int? fixedOffset, int bitOffset, Func<Stream, object>? reader, Action<Stream, object>? writer)
    {
        this.Declaration = parent.Declaration;
        this.EffectiveField = effectiveField;
        this.Type = parent.Type;
        this.Reader = reader;
        this.Writer = writer;
        this.TerminatedReader = parent.TerminatedReader;
        this.TerminatedWriter = parent.TerminatedWriter;
        this.Alignment = alignment;
        this.FixedElementSize = fixedElementSize;
        this.Array = array;
        this.FixedStorageSize = fixedStorageSize;
        this.IsUnsizedCharacterArray = isUnsizedCharacterArray;
        this.BitStorageSize = bitStorageSize;
        this.BitStorageIsLittleEndian = bitStorageIsLittleEndian;
        this.FixedOffset = fixedOffset;
        this.BitOffset = bitOffset;
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

    public ImmutableArray<string> VisibleNames { get; internal set; } = [];

    public ImmutableArray<int> CapturedLocalSlots { get; internal set; } = [];

    public ImmutableArray<int> RestoredLocalSlots { get; internal set; } = [];

    /// <summary>
    ///     Whether reading or writing this field must publish its value as a layout variable. False when no
    ///     expression in the layout can name it (E2.6), so the capture allocation and dictionary write are skipped;
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

    public int PointerDepth => this.EffectiveField.PointerDepth;

    public Func<Stream, object>? Reader { get; }

    public Func<Stream, object>? TerminatedReader { get; }

    public Action<Stream, object>? TerminatedWriter { get; }

    public CompiledTypeReference Type { get; }

    public Action<Stream, object>? Writer { get; }

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
    ///     Creates an immutable view for one selected array element, peeling exactly one dimension (LANG-05): a
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
            this.Reader,
            this.Writer);
    }

    /// <summary>Creates an immutable target view after explicit pointer accessors consume part of the shape.</summary>
    public CompiledField SelectPointerTarget(
        int remainingPointerDepth,
        string? terminatedCodecName,
        Func<Stream, object>? terminatedReader,
        Action<Stream, object>? terminatedWriter,
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
            targetIsTerminated ? terminatedReader : this.Reader,
            targetIsTerminated ? terminatedWriter : this.Writer);
    }

    /// <summary>Returns the same descriptor with its compiled placement facts attached.</summary>
    public CompiledField WithPlacement(int? fixedOffset, int bitOffset)
    {
        return new CompiledField(
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
            this.Reader,
            this.Writer);
    }
}
