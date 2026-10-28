namespace CStructSharp.Memory;

using System;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Engine;
using CStructSharp.Reading;
using CStructSharp.Syntax;

/// <summary>
///     How a memory schema decodes and encodes one scalar or pointer value, or one bitfield slice of a scalar. The schema
///     compiles a one-member layout for it (<c>struct __memory_scalar { T value; }</c>, or <c>struct __bits</c> for a
///     slice), which checks the scalar's size and encodes values. A value whose field the engine decodes as a fixed-width
///     number or an enum is decoded straight from its bytes with that field's codec, by the rules a parse of the layout
///     applies; any other value is read through the layout.
/// </summary>
internal sealed class MemoryScalarCodec
{
    /// <summary>The path a value is read through when it is not decoded directly: the scalar's spelling, or the slice's field.</summary>
    private readonly string readPath;

    /// <summary>The slice's first bit, counted from the storage integer's low bit.</summary>
    private readonly int bitOffset;

    /// <summary>The slice's width in bits, or 0 for a whole value.</summary>
    private readonly int bitWidth;

    /// <summary>
    ///     The field whose codec decodes the value (the slice's storage), or <see langword="null"/> to read through the layout;
    ///     found on first use (<see cref="resolved"/>), so building a schema compiles no plan it may never read with.
    /// </summary>
    private CompiledField? field;

    /// <summary>Whether <see cref="field"/> was found; written after it, so a thread that sees this sees the field.</summary>
    private volatile bool resolved;

    /// <summary>Creates the codec of a whole value or of a slice.</summary>
    /// <param name="layout">The one-member layout.</param>
    /// <param name="readPath">The path a value is read through when it is not decoded directly.</param>
    /// <param name="bitOffset">The slice's first bit from the storage's low bit; 0 for a whole value.</param>
    /// <param name="bitWidth">The slice's width in bits; 0 for a whole value.</param>
    private MemoryScalarCodec(CStruct layout, string readPath, int bitOffset, int bitWidth)
    {
        this.Layout = layout;
        this.readPath = readPath;
        this.bitOffset = bitOffset;
        this.bitWidth = bitWidth;
    }

    /// <summary>Gets the one-member layout, which encodes values (<c>Serialize</c>, and <c>Update</c> for a slice).</summary>
    public CStruct Layout { get; }

    /// <summary>
    ///     Creates the codec of a whole scalar or pointer: decoded directly when the layout's static read plan reads its one
    ///     member as a number or an enum.
    /// </summary>
    /// <param name="layout">The layout <c>struct __memory_scalar { T value; }</c>.</param>
    /// <param name="root">The scalar's spelling, the root a value is otherwise read as.</param>
    /// <returns>The codec.</returns>
    public static MemoryScalarCodec ForValue(CStruct layout, string root) => new(layout, root, 0, 0);

    /// <summary>
    ///     Creates the codec of a bitfield slice: decoded directly when its storage is a fixed-width integer the layout reads
    ///     with a plain codec.
    /// </summary>
    /// <param name="layout">The layout <c>struct __bits { storage :offset; storage value:width; }</c>.</param>
    /// <param name="bitOffset">The slice's first bit, counted from the storage integer's low bit.</param>
    /// <param name="bitWidth">The slice's width in bits.</param>
    /// <returns>The codec.</returns>
    public static MemoryScalarCodec ForSlice(CStruct layout, int bitOffset, int bitWidth) => new(layout, "__bits.value", bitOffset, bitWidth);

    /// <summary>Identifies a plain built-in number whose array can be materialized without boxed intermediate storage.</summary>
    /// <remarks>Call only for schemas without caller-defined codecs, declarations, or prelude options. Resolving the
    /// static plan early must not move a caller callback ahead of a source read.</remarks>
    /// <returns>The primitive kind, or <see cref="PrimitiveCodecKind.None"/> for other return shapes.</returns>
    public PrimitiveCodecKind GetPrimitiveKind()
    {
        if (!this.resolved)
        {
            this.field = this.FindDirectField();
            this.resolved = true;
        }

        return this.bitWidth == 0 && this.field is { Enum: null, } direct && direct.Codec.IsFixedWidthNumeric
            ? direct.Codec.Kind
            : PrimitiveCodecKind.None;
    }

    /// <summary>Gets the natural managed result type without invoking a caller's decoder.</summary>
    /// <param name="signedSlice">Whether the selecting bitfield requires signed extension.</param>
    /// <returns>The exact numeric or enum type, or object for a caller-defined or nonnumeric codec.</returns>
    internal Type ResultType(bool signedSlice)
    {
        CompiledField? direct = this.FindDirectField();
        if (direct?.Enum is not null)
        {
            return typeof(Values.EnumValueResult);
        }

        return this.bitWidth > 0 ? signedSlice ? typeof(long) : this.bitWidth < 32 ? typeof(int) : typeof(ulong)
            : direct is not null ? FieldAccessor<object>.NaturalType(direct.Codec) ?? typeof(object) : typeof(object);
    }

    /// <summary>Decodes a validated typed numeric selection without boxing; other codecs keep their original decoding contract.</summary>
    /// <typeparam name="T">Exact managed result type checked at binding.</typeparam>
    /// <param name="bytes">The scalar's complete storage bytes.</param>
    /// <param name="signedSlice">Whether to sign-extend a bit slice.</param>
    /// <returns>The typed value.</returns>
    internal T DecodeTyped<T>(ReadOnlySpan<byte> bytes, bool signedSlice)
    {
        if (!this.resolved)
        {
            this.field = this.FindDirectField();
            this.resolved = true;
        }

        if (this.field is { Enum: null, } direct && this.bitWidth == 0 && FieldAccessor<T>.NaturalType(direct.Codec) == typeof(T))
        {
            return FieldAccessor<T>.Decode(direct.Codec, bytes);
        }

        if (this.bitWidth > 0 && this.field is { Enum: null, } storage)
        {
            // Read the storage bits in its declared byte order, then mask before extending the slice's sign bit.
            ulong bits = 0;
            for (int index = 0; index < bytes.Length; index++)
            {
                int shift = (storage.Codec.LittleEndian ? index : bytes.Length - index - 1) * 8;
                bits |= (ulong)bytes[index] << shift;
            }

            bits >>= this.bitOffset;
            bits &= ulong.MaxValue >> (64 - this.bitWidth);
            if (signedSlice)
            {
                return (T)(object)(unchecked((long)(bits << (64 - this.bitWidth))) >> (64 - this.bitWidth));
            }

            return this.bitWidth < 32 ? (T)(object)(int)bits : (T)(object)bits;
        }

        return (T)this.Decode(bytes, signedSlice);
    }

    /// <summary>Decodes stored pointer bits at every supported pointer width without following or clearing tags.</summary>
    /// <param name="bytes">Complete pointer storage.</param>
    /// <returns>The zero-extended stored address bits.</returns>
    internal ulong DecodePointer(ReadOnlySpan<byte> bytes) => bytes.Length switch
    {
        1 => this.DecodeTyped<byte>(bytes, false),
        2 => this.DecodeTyped<ushort>(bytes, false),
        4 => this.DecodeTyped<uint>(bytes, false),
        8 => this.DecodeTyped<ulong>(bytes, false),
        _ => throw new ArgumentOutOfRangeException(nameof(bytes)),
    };

    /// <summary>
    ///     Decodes a value from exactly its bytes (a slice's whole storage unit), into the value a read of the layout
    ///     returns: the codec's CLR type, an enum result, or a slice's bits as an <see cref="int"/> below 32 bits and a
    ///     <see cref="ulong"/> otherwise.
    /// </summary>
    /// <param name="bytes">The value's bytes: the scalar's size.</param>
    /// <param name="signedSlice">Whether to sign-extend a bit slice before returning or constructing its enum value.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="Diagnostics.CStructException">The layout's codec rejects the bytes.</exception>
    public object Decode(ReadOnlySpan<byte> bytes, bool signedSlice = false)
    {
        if (!this.resolved)
        {
            this.field = this.FindDirectField();
            this.resolved = true;
        }

        if (this.field is not { } direct)
        {
            return this.Layout.ReadValue(bytes, this.readPath)!;
        }

        object storage = direct.Codec.ReadNumeric(bytes);
        if (this.bitWidth == 0)
        {
            return direct.Enum is { } whole ? ValueDecoding.CreateEnumValue(whole, storage) : storage;
        }

        // The core's bitfield rule (CStruct.DecodeBitfield) with the slice allocated from the storage's low bit.
        ulong bits = BitfieldCodecTable.ExtractBitfieldValue(storage, this.bitOffset, this.bitWidth);
        if (signedSlice)
        {
            long signed = unchecked((long)(bits << (64 - this.bitWidth))) >> (64 - this.bitWidth);
            return direct.Enum is { } signedEnum ? ValueDecoding.CreateEnumValue(signedEnum, signed) : signed;
        }

        object content = this.bitWidth < 32 ? (int)bits : bits;
        return direct.Enum is { } sliced ? ValueDecoding.CreateEnumValue(sliced, content) : content;
    }

    /// <summary>Returns the compiled struct a one-member layout declares.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="name">The struct's name.</param>
    /// <returns>The compiled struct.</returns>
    private static CompiledCompositeType Composite(CStruct layout, string name)
    {
        LayoutCompilation compilation = layout.Compilation;
        _ = compilation.ModelQueries.TryGetCompiledDeclaration(name, out CStructElement? declaration);
        return compilation.SizeQueries.GetCompiledComposite((Struct)declaration!);
    }

    /// <summary>
    ///     Finds the field that decodes the value directly: for a whole value, the one member when the layout's static read
    ///     plan reads it as a number or an enum; for a slice, its storage when that is a fixed-width integer read with a
    ///     plain codec.
    /// </summary>
    /// <returns>The field, or <see langword="null"/> when the value is read through the layout.</returns>
    private CompiledField? FindDirectField()
    {
        if (this.bitWidth == 0)
        {
            StaticReadPlan? plan = Composite(this.Layout, "__memory_scalar").StaticPlan;
            return plan?.Operations is [{ Kind: StaticReadKind.Numeric or StaticReadKind.Enum, } only,] ? only.Field : null;
        }

        CompiledField value = Composite(this.Layout, "__bits").Fields[^1];
        return value.Codec.IsFixedWidthNumeric && !value.Codec.IsCustom && !value.IsFixedPoint && value.PointerDepth == 0 ? value : null;
    }
}
