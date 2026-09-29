namespace CStructSharp.Streams;

/// <summary>
///     The per-string part of a write operation's output budget, as a codec writer sees it through the stream it is given:
///     the interpreter's <see cref="WriteBudgetStream"/> and the compiled engine's <see cref="Engine.MemoryWriteBuffer"/>
///     both enforce <see cref="WriteOptions.MaxStringBytes"/> this way, so a codec that checks its encoded size before
///     writing fails identically on both.
/// </summary>
internal interface IWriteBudget
{
    /// <summary>Gets the configured per-string encoded-byte budget.</summary>
    long MaxStringBytes { get; }

    /// <summary>Rejects a string before its encoded payload is allocated or written.</summary>
    /// <param name="encodedByteCount">The string's encoded length in bytes, including any terminator.</param>
    /// <exception cref="Diagnostics.CStructWriteLimitException">
    ///     <paramref name="encodedByteCount"/> is negative or exceeds <see cref="MaxStringBytes"/>.
    /// </exception>
    void EnsureStringBytes(long encodedByteCount);
}
