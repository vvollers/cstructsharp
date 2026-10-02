namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>
///     The engine's access to one read operation's input: position, bytes, budget charges, failure texts and
///     cancellation. The executor is written once as <c>Execute&lt;TCursor&gt;(ref TCursor cursor)</c> with
///     <c>where TCursor : struct, IReadCursor</c>; .NET compiles a separate copy of a generic method for each struct
///     type argument, so every call below is direct, without an interface dispatch.
/// </summary>
/// <remarks>
///     <para>
///         Two cursors exist. <see cref="MemoryReadCursor"/> reads memory through the <c>MemoryReadCore</c> that
///         <c>ReadBudgetStream</c> also uses; <see cref="StreamReadCursor"/> calls the operation's
///         <c>ReadBudgetStream</c> directly. Both therefore consume the same bytes, charge <c>MaxTotalBytesRead</c> at
///         the same points (every byte consumed, once per read of it, after the read) and fail with the same exception
///         types, messages and final positions. A look-ahead that is not consumed - the rest of a terminated string's
///         chunk, a terminated array's scan for its terminator - is not charged, although a stream source may
///         physically read it.
///     </para>
///     <para>
///         Positions are absolute: bytes from the input's byte 0 (a stream's origin, not its starting position). A
///         position may reach the end of the input but not pass it. Cancellation is observed only by
///         <see cref="ThrowIfCancellationRequested"/> (which the executor calls at struct and union entry, pointer
///         follows and between struct-array elements), between the 64 KiB blocks of <see cref="ReadPrimitiveArray"/>
///         and between the 256-byte chunks of <see cref="ReadTerminatedString"/> - never per primitive.
///     </para>
/// </remarks>
internal interface IReadCursor
{
    /// <summary>Gets or sets the position in bytes from the input's byte 0.</summary>
    /// <exception cref="CStructReadException">A new position is negative or past the end of the input.</exception>
    long Position { get; set; }

    /// <summary>Gets the input length in bytes.</summary>
    /// <exception cref="CStructReadException">A stream source failed to report its length.</exception>
    long Length { get; }

    /// <summary>Gets the per-string encoded-byte budget of the operation.</summary>
    long MaxStringBytes { get; }

    /// <summary>Gets the operation's cancellation token.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    ///     Reports whether the input ends at or before <paramref name="address"/>, so no byte exists there: the check a
    ///     pointer target passes. Unlike comparing with <see cref="Length"/>, a partly buffered input whose length is
    ///     unknown answers from its buffered bytes when they reach the address.
    /// </summary>
    /// <param name="address">The nonnegative address in bytes from the input's byte 0.</param>
    /// <returns>Whether <paramref name="address"/> is at or past the end of the input.</returns>
    /// <exception cref="CStructReadException">A stream source failed to report its length.</exception>
    bool EndsAtOrBefore(long address);

    /// <summary>Throws when the operation's token is cancelled; the executor calls it only at the documented boundaries.</summary>
    /// <exception cref="OperationCanceledException">The token is cancelled.</exception>
    void ThrowIfCancellationRequested();

    /// <summary>Moves the position by <paramref name="count"/> bytes, forward or (negative) back.</summary>
    /// <param name="count">The signed distance in bytes.</param>
    /// <exception cref="CStructReadException">The target lies outside the input.</exception>
    /// <exception cref="OverflowException">The target position overflows.</exception>
    void Skip(long count);

    /// <summary>
    ///     Rounds the position up to the next multiple of <paramref name="alignment"/> counted from
    ///     <paramref name="origin"/> - a struct's own first byte, so alignment holds wherever the struct sits.
    /// </summary>
    /// <param name="origin">The absolute position alignment is measured from.</param>
    /// <param name="alignment">The positive boundary in bytes.</param>
    /// <exception cref="CStructReadException">The aligned position lies past the end of the input.</exception>
    /// <exception cref="CStructLayoutException"><paramref name="alignment"/> is not positive.</exception>
    void Align(long origin, int alignment);

    /// <summary>
    ///     Reports whether the input provably cannot supply <paramref name="count"/> more bytes, without consuming any;
    ///     a source whose length is unknown answers <see langword="false"/> and lets the read fail.
    /// </summary>
    /// <param name="count">The nonnegative number of bytes.</param>
    /// <returns>Whether the input definitely ends before them.</returns>
    bool IsShortBy(long count);

    /// <summary>
    ///     Serves <paramref name="count"/> bytes straight from memory, advancing and then charging them; false, with
    ///     nothing consumed, when the input is not in memory or the bytes are not all there.
    /// </summary>
    /// <param name="count">The nonnegative number of bytes.</param>
    /// <param name="bytes">The borrowed bytes, valid until the operation ends, or an empty span.</param>
    /// <returns>Whether the bytes were served.</returns>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget (they are consumed).</exception>
    bool TryReadSpan(int count, out ReadOnlySpan<byte> bytes);

    /// <summary>
    ///     Like <see cref="TryReadSpan"/>, but also declines, without charging, when the bytes would exceed the total
    ///     read budget, so the caller can fall back to a path that fails at its usual place.
    /// </summary>
    /// <param name="count">The nonnegative number of bytes.</param>
    /// <param name="bytes">The borrowed bytes, or an empty span.</param>
    /// <returns>Whether the bytes were served within the budget.</returns>
    bool TryReadSpanWithinBudget(int count, out ReadOnlySpan<byte> bytes);

    /// <summary>
    ///     The stream-source counterpart of <see cref="TryReadSpanWithinBudget"/>: fills <paramref name="destination"/>
    ///     when a seekable stream provably holds the bytes and the budget allows; false, with nothing consumed,
    ///     otherwise and always for a memory source.
    /// </summary>
    /// <param name="destination">The span to fill; its length is the number of bytes.</param>
    /// <returns>Whether the block was read.</returns>
    bool TryReadBlockWithinBudget(Span<byte> destination);

    /// <summary>Exposes the bytes from the position to the end of a memory input without consuming or charging them.</summary>
    /// <param name="bytes">The borrowed remaining bytes, or an empty span.</param>
    /// <returns>Whether the input is in memory; pair with <see cref="Advance"/>.</returns>
    bool TryPeekRemaining(out ReadOnlySpan<byte> bytes);

    /// <summary>
    ///     Charges <paramref name="count"/> bytes the operation consumes without reading them through the cursor: an
    ///     array's terminator, or the elements a path walk passes, which a scan has already inspected.
    /// </summary>
    /// <param name="count">The nonnegative number of bytes.</param>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget.</exception>
    void Charge(long count);

    /// <summary>
    ///     Scans from the position for the first all-zero element of a terminated array without charging the read budget,
    ///     through <see cref="DynamicArrayExtent"/>'s shared scanners: memory in place, a stream through its reads. The
    ///     scan looks no further than the budget allows the array to consume, so an array whose elements and terminator
    ///     exceed the budget fails here, as consuming them would. A stream source is left somewhere after the position.
    /// </summary>
    /// <param name="elementSize">The positive size of one element in bytes.</param>
    /// <param name="maximumElements">The largest element count the read options allow.</param>
    /// <param name="fieldName">The array field, named in failure messages.</param>
    /// <returns>The number of elements before the terminator.</returns>
    /// <exception cref="CStructReadException">The input ends before an all-zero element.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximumElements"/>, or the array exceeds the read budget.</exception>
    int ScanTerminated(int elementSize, int maximumElements, string fieldName);

    /// <summary>Consumes <paramref name="count"/> bytes that <see cref="TryPeekRemaining"/> exposed, charging them.</summary>
    /// <param name="count">The number of bytes consumed.</param>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget.</exception>
    void Advance(int count);

    /// <summary>
    ///     Reads up to <paramref name="count"/> bytes as <see cref="System.IO.Stream.Read(byte[], int, int)"/> does over the
    ///     operation's budget stream: advancing past and charging only the bytes returned, which a stream source may make
    ///     fewer than asked for; 0 at the end of the input.
    /// </summary>
    /// <param name="buffer">The array that receives the bytes.</param>
    /// <param name="offset">The index in <paramref name="buffer"/> of the first byte stored.</param>
    /// <param name="count">The largest number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget (they are consumed).</exception>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>
    ///     Reads one value of a caller's codec through <see cref="CustomCodecAdapter"/>, the adapter a caller's codec
    ///     runs through: a memory input is decoded in place (the cursor advances past the bytes the codec took, or the
    ///     whole remainder when it needs more data, before a failure), a stream through its growing window.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <returns>The decoded value; <see langword="null"/> when the codec reported success without one.</returns>
    /// <exception cref="CStructReadException">The input ends before the value, or the codec rejects it.</exception>
    /// <exception cref="CStructReadLimitException">The value exceeds a budget.</exception>
    object? ReadCustom(ICustomCodec codec);

    /// <summary>Reads one required byte, the way every one-byte primitive codec reads.</summary>
    /// <returns>The byte.</returns>
    /// <exception cref="CStructReadException">The input is at its end (the short-read text, no inner exception).</exception>
    /// <exception cref="CStructReadLimitException">The byte exceeds the total read budget.</exception>
    byte ReadByteExactly();

    /// <summary>
    ///     Reads exactly <c>destination.Length</c> bytes. On a short read the bytes that were there are consumed and
    ///     charged, the failure states the bytes needed and available at the start, and the position ends at the end
    ///     of the input.
    /// </summary>
    /// <param name="destination">The span to fill.</param>
    /// <exception cref="CStructReadException">The input ends before the span is full.</exception>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget.</exception>
    void ReadExactly(Span<byte> destination);

    /// <summary>
    ///     Reads exactly <c>destination.Length</c> bytes as <see cref="System.IO.Stream.ReadExactly(Span{byte})"/> does,
    ///     for a codec that words its own short-read failure (a 16-byte identifier): on a short read the bytes that were
    ///     there are consumed and charged, and the runtime's <see cref="System.IO.EndOfStreamException"/> is thrown.
    /// </summary>
    /// <param name="destination">The span to fill.</param>
    /// <exception cref="System.IO.EndOfStreamException">The input ends before the span is full.</exception>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget.</exception>
    void ReadExactlyOrEndOfStream(Span<byte> destination);

    /// <summary>
    ///     Reads one fixed-width value of <c>scratch.Length</c> bytes as a primitive codec does: straight from memory
    ///     when possible, otherwise into <paramref name="scratch"/> - a one-byte value through
    ///     <see cref="ReadByteExactly"/>, a wider one through <see cref="ReadExactly"/>. A bitfield storage unit
    ///     instead uses <see cref="TryReadSpan"/> then <see cref="ReadExactly"/> at every width.
    /// </summary>
    /// <param name="scratch">A caller buffer whose length is the value's width in bytes.</param>
    /// <returns>The value's bytes: borrowed from memory or <paramref name="scratch"/> itself.</returns>
    /// <exception cref="CStructReadException">The input ends before the value.</exception>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget.</exception>
    ReadOnlySpan<byte> ReadFixed(Span<byte> scratch);

    /// <summary>
    ///     Reads a one-dimensional array of fixed-width numbers in blocks of at most 64 KiB, observing cancellation
    ///     before each block; a count the input provably cannot back fails before the result is allocated.
    /// </summary>
    /// <param name="codec">The element codec, including its byte order.</param>
    /// <param name="count">The number of elements.</param>
    /// <returns>The typed array value.</returns>
    /// <exception cref="CStructReadException">The input ends before the last element.</exception>
    /// <exception cref="CStructReadLimitException">A block exceeds the total read budget.</exception>
    /// <exception cref="OperationCanceledException">The token is cancelled before a block.</exception>
    IList<object?> ReadPrimitiveArray(PrimitiveCodec codec, int count);

    /// <summary>
    ///     Reads a terminated string through the shared reader in <see cref="PrimitiveCodecs"/>: chunks of at most 256
    ///     bytes, of which only the bytes through the terminator are charged, the position left just after the terminator.
    /// </summary>
    /// <param name="encoding">The strict encoding.</param>
    /// <param name="terminator">The terminating character.</param>
    /// <returns>The text without its terminator.</returns>
    /// <exception cref="CStructReadException">The input ends before the terminator or holds invalid text.</exception>
    /// <exception cref="CStructReadLimitException">The string or the total exceeds its budget.</exception>
    /// <exception cref="OperationCanceledException">The token is cancelled before a chunk.</exception>
    string ReadTerminatedString(Encoding encoding, char terminator);

    /// <summary>Reads a byte-counted text field through the shared reader in <see cref="PrimitiveCodecs"/>.</summary>
    /// <param name="byteCount">The field's extent in bytes.</param>
    /// <param name="type">The bounded-text type spelling, such as <c>utf8</c>.</param>
    /// <returns>The decoded text.</returns>
    /// <exception cref="CStructReadException">The input ends early or the bytes are invalid text.</exception>
    /// <exception cref="CStructReadLimitException">The extent or the total exceeds its budget.</exception>
    string ReadBoundedText(int byteCount, string type);

    /// <summary>Writes the final position back to a caller's stream that the cursor reads from memory; otherwise nothing.</summary>
    void FlushPosition();
}
