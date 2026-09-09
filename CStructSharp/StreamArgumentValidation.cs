namespace CStructSharp;

using System;

/// <summary>
///     Validates the <c>byte[]</c>/offset/count argument triple shared by every
///     <see cref="System.IO.Stream.Read(byte[], int, int)" />/<see cref="System.IO.Stream.Write(byte[], int, int)" />
///     override in this project - <see cref="SparseUpdateStream" />, <see cref="BufferWriterStream" />, and
///     <see cref="FixedBufferStream" /> each independently reimplemented this same null/negative/out-of-range
///     validation before delegating to their own <c>Span</c>-based overload.
/// </summary>
internal static class StreamArgumentValidation
{
    /// <summary>
    ///     Throws the same exceptions every existing call site already threw for a null buffer, a negative
    ///     <paramref name="offset" /> or <paramref name="count" />, or a range that does not fit within
    ///     <paramref name="buffer" />.
    /// </summary>
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
