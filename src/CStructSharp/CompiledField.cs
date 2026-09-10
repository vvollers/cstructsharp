namespace CStructSharp;

using System;
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
        int bitOffset)
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
    }

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

    public CStructElement? NamedElement => this.Type.Symbol.Declaration;

    public int PointerDepth => this.EffectiveField.PointerDepth;

    public Func<Stream, object>? Reader { get; }

    public Func<Stream, object>? TerminatedReader { get; }

    public Action<Stream, object>? TerminatedWriter { get; }

    public CompiledTypeReference Type { get; }

    public Action<Stream, object>? Writer { get; }

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
            this.Declaration,
            field,
            this.Type,
            this.Reader,
            this.Writer,
            this.TerminatedReader,
            this.TerminatedWriter,
            this.Alignment,
            this.FixedElementSize,
            nextShape,
            nextStorageSize,
            false,
            this.BitStorageSize,
            this.BitStorageIsLittleEndian,
            this.FixedOffset,
            this.BitOffset);
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
            this.Declaration,
            field,
            this.Type,
            targetIsTerminated ? terminatedReader : this.Reader,
            targetIsTerminated ? terminatedWriter : this.Writer,
            this.TerminatedReader,
            this.TerminatedWriter,
            alignment,
            elementSize,
            CompiledArrayShape.Scalar,
            elementSize,
            false,
            null,
            null,
            null,
            0);
    }

    /// <summary>Returns the same descriptor with its compiled placement facts attached.</summary>
    public CompiledField WithPlacement(int? fixedOffset, int bitOffset)
    {
        return new CompiledField(
            this.Declaration,
            this.EffectiveField,
            this.Type,
            this.Reader,
            this.Writer,
            this.TerminatedReader,
            this.TerminatedWriter,
            this.Alignment,
            this.FixedElementSize,
            this.Array,
            this.FixedStorageSize,
            this.IsUnsizedCharacterArray,
            this.BitStorageSize,
            this.BitStorageIsLittleEndian,
            fixedOffset,
            bitOffset);
    }
}
