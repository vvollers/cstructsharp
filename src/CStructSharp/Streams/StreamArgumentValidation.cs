namespace CStructSharp.Streams;

using System;

/// <summary>
///     Validates the <c>byte[]</c>/offset/count argument triple shared by every
///     <see cref="System.IO.Stream.Read(byte[], int, int)" />/<see cref="System.IO.Stream.Write(byte[], int, int)" />
///     override in this project - <see cref="SparseUpdateStream" />, <see cref="BufferWriterStream" />, and
///     <see cref="FixedBufferStream" /> - before each delegates to its <c>Span</c>-based overload.
/// </summary>
internal static class StreamArgumentValidation
{
    /// <summary>
    ///     Throws the exceptions <see cref="System.IO.Stream"/> documents for a null buffer, a negative
    ///     <paramref name="offset" /> or <paramref name="count" />, or a range that does not fit within
    ///     <paramref name="buffer" />.
    /// </summary>
    /// <param name="buffer">The caller's array.</param>
    /// <param name="offset">The index in <paramref name="buffer" /> of the first byte of the range.</param>
    /// <param name="count">The length of the range in bytes.</param>
    /// <param name="bufferParamName">The caller's parameter name for the buffer, used in exceptions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="buffer" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="offset" /> or <paramref name="count" /> is negative.
    /// </exception>
    /// <exception cref="ArgumentException">The range extends past the end of <paramref name="buffer" />.</exception>
    public static void ValidateRange(byte[]? buffer, int offset, int count, string bufferParamName)
    {
        ArgumentNullException.ThrowIfNull(buffer, bufferParamName);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The offset and count do not fit within the supplied array.", bufferParamName);
        }
    }
}
