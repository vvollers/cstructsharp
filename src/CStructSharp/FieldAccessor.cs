namespace CStructSharp;

using System;
using System.Collections;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Generated;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>
///     One member path of a layout, resolved once and read many times: <c>layout.GetAccessor&lt;float&gt;("reading.pos.x")</c>.
///     Reading a member by a path string parses the path and looks up every segment on each call; an accessor does
///     that work when it is created, so a read is a few array lookups (<see cref="Get(StructValue)"/>) or a decode at a
///     constant byte offset (<see cref="Read(ReadOnlySpan{byte}, ReadOptions?)"/>, <see cref="StructView.Get{T}(FieldAccessor{T})"/>).
/// </summary>
/// <typeparam name="T">The type the member is read as, with the conversions of <c>StructValue.Get&lt;T&gt;</c> and <c>ReadValue&lt;T&gt;</c>.</typeparam>
/// <remarks>
///     An accessor is immutable and can be shared by threads. It is a shortcut, never a different reader: whenever the
///     prepared route does not apply to a value or an input - a struct assembled by the caller, a member that is absent,
///     an input that is too short, limits that the shortcut cannot prove - the read is the one the path string would
///     perform, with the same result and the same failure.
/// </remarks>
public sealed class FieldAccessor<T>
{
    private readonly CStruct layout;
    private readonly string relativePath;
    private readonly Step[]? steps;

    // The member's constant offset from the root's first byte, or -1 when the member is not a fixed-width number at a
    // build-time offset (or T is not the number's own CLR type); then every byte read takes ReadValue.
    private readonly int offset;
    private readonly PrimitiveCodec codec;
    private readonly int maximumArrayCount;
    private readonly int nestingDepth;

    /// <summary>Stores a resolution made by <see cref="CStruct.GetAccessor{T}(string)"/>.</summary>
    /// <param name="layout">The layout the path was resolved against; reads without a shortcut use it.</param>
    /// <param name="path">The full path, starting at the root declaration.</param>
    /// <param name="root">The name of the root struct declaration.</param>
    /// <param name="rootComposite">The compiled root struct.</param>
    /// <param name="relativePath">The part of the path below the root, empty for the root itself.</param>
    /// <param name="steps">
    ///     The prepared slot lookups through parsed struct values, or null when the path names a member the layout
    ///     does not declare and every read must use <paramref name="relativePath"/>.
    /// </param>
    /// <param name="offset">
    ///     The member's constant byte offset from the root's first byte, or -1 when it cannot be decoded directly.
    /// </param>
    /// <param name="codec">The member's codec, used for direct decoding at <paramref name="offset"/>.</param>
    /// <param name="maximumArrayCount">
    ///     The largest array element count in the root; the read limits must allow it for a direct decode.
    /// </param>
    /// <param name="nestingDepth">
    ///     The struct nesting depth of the root and path; the read limits must allow it for a direct decode.
    /// </param>
    internal FieldAccessor(CStruct layout, string path, string root, CompiledCompositeType rootComposite, string relativePath, Step[]? steps, int offset, PrimitiveCodec codec, int maximumArrayCount, int nestingDepth)
    {
        this.layout = layout;
        this.Path = path;
        this.Root = root;
        this.RootComposite = rootComposite;
        this.relativePath = relativePath;
        this.steps = steps;
        this.offset = offset;
        this.codec = codec;
        this.maximumArrayCount = maximumArrayCount;
        this.nestingDepth = nestingDepth;
    }

    /// <summary>Gets the path the accessor reads, starting at the root declaration (<c>reading.pos.x</c>).</summary>
    public string Path { get; }

    /// <summary>Gets the root declaration the path starts at.</summary>
    public string Root { get; }

    /// <summary>Gets the layout the accessor was resolved against.</summary>
    internal CStruct Layout => this.layout;

    /// <summary>Gets the root struct the accessor was resolved against.</summary>
    internal CompiledCompositeType RootComposite { get; }

    /// <summary>Gets whether the member is decoded directly at a constant offset when the input and limits allow it.</summary>
    internal bool HasFixedOffset => this.offset >= 0;

    /// <summary>The CLR type a fixed-width numeric codec decodes to, or <see langword="null"/> for a codec the accessor decodes through the general reader.</summary>
    /// <param name="codec">The member's codec.</param>
    /// <returns>The natural type of the member's value.</returns>
    internal static Type? NaturalType(PrimitiveCodec codec)
    {
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => typeof(byte),
            PrimitiveCodecKind.Int8 => typeof(sbyte),
            PrimitiveCodecKind.Bool => typeof(bool),
            PrimitiveCodecKind.Int16 => typeof(short),
            PrimitiveCodecKind.UInt16 => typeof(ushort),
            PrimitiveCodecKind.Int24 or PrimitiveCodecKind.Int32 => typeof(int),
            PrimitiveCodecKind.UInt24 or PrimitiveCodecKind.UInt32 => typeof(uint),
            PrimitiveCodecKind.Int64 => typeof(long),
            PrimitiveCodecKind.UInt64 => typeof(ulong),
            PrimitiveCodecKind.Float32 => typeof(float),
            PrimitiveCodecKind.Float64 => typeof(double),
            _ => null,
        };
    }

    /// <summary>
    ///     Decodes one number of the codec's own type <typeparamref name="T"/> without boxing: the JIT keeps only the
    ///     branch for <typeparamref name="T"/>, so the casts through <see cref="object"/> compile away.
    /// </summary>
    private static T Decode(PrimitiveCodec codec, ReadOnlySpan<byte> bytes)
    {
        bool le = codec.LittleEndian;
        if (typeof(T) == typeof(byte))
        {
            return (T)(object)bytes[0];
        }

        if (typeof(T) == typeof(sbyte))
        {
            return (T)(object)unchecked((sbyte)bytes[0]);
        }

        if (typeof(T) == typeof(bool))
        {
            return (T)(object)(bytes[0] != 0);
        }

        if (typeof(T) == typeof(short))
        {
            return (T)(object)Codec.ReadInt16(bytes, le);
        }

        if (typeof(T) == typeof(ushort))
        {
            return (T)(object)Codec.ReadUInt16(bytes, le);
        }

        if (typeof(T) == typeof(int))
        {
            return (T)(object)(codec.Kind == PrimitiveCodecKind.Int24 ? Codec.ReadInt24(bytes, le) : Codec.ReadInt32(bytes, le));
        }

        if (typeof(T) == typeof(uint))
        {
            return (T)(object)(codec.Kind == PrimitiveCodecKind.UInt24 ? Codec.ReadUInt24(bytes, le) : Codec.ReadUInt32(bytes, le));
        }

        if (typeof(T) == typeof(long))
        {
            return (T)(object)Codec.ReadInt64(bytes, le);
        }

        if (typeof(T) == typeof(ulong))
        {
            return (T)(object)Codec.ReadUInt64(bytes, le);
        }

        if (typeof(T) == typeof(float))
        {
            return (T)(object)Codec.ReadSingle(bytes, le);
        }

        if (typeof(T) == typeof(double))
        {
            return (T)(object)Codec.ReadDouble(bytes, le);
        }

        throw new InvalidOperationException("An accessor with a fixed offset decodes only the member's own numeric type.");
    }

    /// <summary>
    ///     Reads the member from a parsed root struct: the same value, or the same failure, as
    ///     <c>value.Get&lt;T&gt;(path below the root)</c>.
    /// </summary>
    /// <param name="value">A struct parsed as the accessor's root (any other struct works too, through the path string).</param>
    /// <returns>The member.</returns>
    /// <exception cref="Diagnostics.CStructPathException">The member is absent or the path does not apply to the value.</exception>
    /// <exception cref="Diagnostics.CStructReadException">The member cannot be converted to <typeparamref name="T"/>.</exception>
    public T Get(StructValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return this.steps is { } prepared && this.TryWalk(value, prepared, out T member) ? member : value.Get<T>(this.relativePath);
    }

    /// <summary>
    ///     Reads the member from bytes that start with the root: the same value, or the same failure, as
    ///     <c>layout.ReadValue&lt;T&gt;(source, Path, options: options)</c>, decoded directly when the member has a fixed
    ///     offset and the input and limits provably allow it.
    /// </summary>
    /// <param name="source">The bytes; the root starts at the first one.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The member.</returns>
    /// <exception cref="Diagnostics.CStructPathException">The path cannot be resolved.</exception>
    /// <exception cref="Diagnostics.CStructReadException">The bytes cannot be decoded or converted to <typeparamref name="T"/>.</exception>
    public T Read(ReadOnlySpan<byte> source, ReadOptions? options = null)
    {
        return this.TryReadFixed(source, options, out T member) ? member : this.layout.ReadValue<T>(source, this.Path, null, options);
    }

    /// <summary>
    ///     Decodes the member at its constant offset when the general selected read would read exactly its bytes and
    ///     succeed: the bytes are present, and the limits cover the member's bytes, the arrays and the structs on its path.
    /// </summary>
    /// <param name="source">The bytes, the root at the first one.</param>
    /// <param name="options">The read options.</param>
    /// <param name="member">The member when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the member was decoded directly.</returns>
    internal bool TryReadFixed(ReadOnlySpan<byte> source, ReadOptions? options, out T member)
    {
        int end = this.offset + this.codec.Size;
        if (this.offset < 0 || end > source.Length ||
            (options is not null &&
             (ReadOperationSettings.SnapshotReadOptions(options) is not { HasValidLimits: true } settings ||
              !settings.Covers(end, 0, this.nestingDepth, this.maximumArrayCount))))
        {
            member = default!;
            return false;
        }

        member = Decode(this.codec, source.Slice(this.offset, this.codec.Size));
        return true;
    }

    /// <summary>
    ///     Follows the prepared slots through structs of the expected shapes. Any value that does not match the
    ///     preparation (another shape, an absent member, a non-list at an index) returns <see langword="false"/>, and the
    ///     caller reads through the path string instead.
    /// </summary>
    private bool TryWalk(StructValue root, Step[] prepared, out T member)
    {
        object? current = root;
        for (int index = 0; index < prepared.Length; index++)
        {
            Step step = prepared[index];
            if (current is not StructValue container || !ReferenceEquals(container.Shape, step.Shape) || !container.TryGetSlot(step.Slot, out current))
            {
                member = default!;
                return false;
            }

            int[] indexes = step.Indexes;
            for (int position = 0; position < indexes.Length; position++)
            {
                // The last index of the path reads a typed array element without boxing it.
                if (index == prepared.Length - 1 && position == indexes.Length - 1 && current is ITypedElements<T> elements && elements.TryGetElement(indexes[position], out T? element))
                {
                    member = element!;
                    return true;
                }

                if (current is not IList list || indexes[position] >= list.Count)
                {
                    member = default!;
                    return false;
                }

                current = list[indexes[position]];
            }
        }

        member = TypedValueConverter.Convert<T>(current, this.relativePath);
        return true;
    }

    /// <summary>One segment of a prepared path: the member's slot in a struct of <see cref="Shape"/>, then its indexes.</summary>
    /// <param name="Shape">The shape of the struct that holds the member.</param>
    /// <param name="Slot">The member's slot in that shape.</param>
    /// <param name="Indexes">The indexes applied to the member, in order.</param>
    internal sealed record Step(StructShape Shape, int Slot, int[] Indexes);
}
