namespace CStructSharp.Codecs;

using System;
using System.Buffers;

/// <summary>
///     A caller-supplied primitive type: a name usable in layouts like any built-in primitive, and the span-based
///     read/write rule for one value. Register instances through <see cref="CStructCompilationOptions.Codecs"/>. A
///     custom type may be an array element or a pointer target, and is neither bitfield storage nor an enum backing
///     type.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Read"/> receives the bytes from the value's start: the whole remaining input for memory input
///         and <see cref="System.IO.MemoryStream"/>s, or a window for other streams that grows while the codec
///         answers <see cref="OperationStatus.NeedMoreData"/>. It reports how many bytes the value took; the library
///         charges them to the read budget and advances past them. <see cref="Write"/> receives a destination window
///         and reports the bytes it wrote, or <see cref="OperationStatus.DestinationTooSmall"/> to be offered a
///         larger one (a stream destination grows its scratch window; caller-owned memory cannot and the write
///         fails). Either method may answer <see cref="OperationStatus.InvalidData"/> - or throw - for a value it
///         cannot decode or encode; both surface as the operation's read or write error at the field.
///     </para>
///     <para>
///         Implementations must be thread-safe and are compared by reference, so keep one instance per codec: the
///         codec set is part of <see cref="CStruct.GetOrCompile"/>'s cache key.
///     </para>
/// </remarks>
public interface ICustomCodec
{
    /// <summary>Gets the type name accepted in layouts; it must be an identifier and not a built-in codec name.</summary>
    string Name { get; }

    /// <summary>Gets the encoded size in bytes when every value has the same size; <see langword="null"/> for a variable-length encoding.</summary>
    int? FixedSize { get; }

    /// <summary>Gets the alignment used by aligned placement; 1 for no alignment requirement.</summary>
    int Alignment { get; }

    /// <summary>Decodes one value from the start of <paramref name="source"/>.</summary>
    /// <param name="source">The bytes from the value's start; for a fixed-size codec at least <see cref="FixedSize"/> bytes when the input has them.</param>
    /// <param name="value">The decoded value, as the layout publishes it; <see langword="null"/> unless the status is <see cref="OperationStatus.Done"/>.</param>
    /// <param name="bytesConsumed">The encoded length of the value when the status is <see cref="OperationStatus.Done"/>.</param>
    /// <returns><see cref="OperationStatus.Done"/>, <see cref="OperationStatus.NeedMoreData"/> when the value continues past <paramref name="source"/>, or <see cref="OperationStatus.InvalidData"/>.</returns>
    OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed);

    /// <summary>Encodes one value at the start of <paramref name="destination"/>.</summary>
    /// <param name="destination">The window to write into; the library offers a larger one after <see cref="OperationStatus.DestinationTooSmall"/> when the destination can grow.</param>
    /// <param name="value">The caller-supplied value to encode.</param>
    /// <param name="bytesWritten">The encoded length when the status is <see cref="OperationStatus.Done"/>.</param>
    /// <returns><see cref="OperationStatus.Done"/>, <see cref="OperationStatus.DestinationTooSmall"/>, or <see cref="OperationStatus.InvalidData"/> for a value the codec cannot encode.</returns>
    OperationStatus Write(Span<byte> destination, object value, out int bytesWritten);
}
