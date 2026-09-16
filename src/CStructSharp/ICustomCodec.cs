namespace CStructSharp;

using System.IO;

/// <summary>
///     A caller-supplied primitive type: a name usable in layouts like any built-in primitive, and the read/write rule
///     for one value. Register instances through <see cref="CStructCompilationOptions.Codecs"/>. A custom type is
///     read through its delegate on every operation (never a span fast path), may be an array element or a pointer
///     target, and is neither bitfield storage nor an enum backing type.
/// </summary>
/// <remarks>
///     <see cref="Read"/> receives the operation's budgeted stream positioned at the value and must consume exactly
///     the value's bytes; reading past the input surfaces as a read error. <see cref="Write"/> receives the
///     destination positioned at the value. Implementations must be thread-safe and are compared by reference, so
///     keep one instance per codec: the codec set is part of <see cref="CStruct.GetOrCompile"/>'s cache key.
/// </remarks>
public interface ICustomCodec
{
    /// <summary>Gets the type name accepted in layouts; it must be an identifier and not a built-in codec name.</summary>
    string Name { get; }

    /// <summary>Gets the encoded size in bytes when every value has the same size; <see langword="null"/> for a variable-length encoding.</summary>
    int? FixedSize { get; }

    /// <summary>Gets the alignment used by aligned placement; 1 for no alignment requirement.</summary>
    int Alignment { get; }

    /// <summary>Reads one value from the current position.</summary>
    object Read(Stream stream);

    /// <summary>Writes one value at the current position.</summary>
    void Write(Stream stream, object value);
}
