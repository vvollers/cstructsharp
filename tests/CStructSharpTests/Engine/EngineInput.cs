namespace CStructSharp.Tests;

/// <summary>The form in which a differential read receives its input bytes.</summary>
internal enum EngineInput
{
    /// <summary>A <see cref="ReadOnlySpan{T}"/> over the bytes.</summary>
    Span,

    /// <summary>The byte array itself.</summary>
    ByteArray,

    /// <summary>A <see cref="ReadOnlyMemory{T}"/> over the bytes.</summary>
    Memory,

    /// <summary>A <see cref="MemoryStream"/> that hides its buffer, positioned at its start; its final position is rendered.</summary>
    Stream,

    /// <summary>A <see cref="System.Buffers.ReadOnlySequence{T}"/> of three-byte segments.</summary>
    Sequence,

    /// <summary>
    ///     A <see cref="MemoryStream"/> that exposes its buffer, whose origin is three bytes into that buffer and whose
    ///     data starts eight bytes after the origin, where the stream is positioned (<see cref="EngineStreams.ExposedStart"/>).
    /// </summary>
    ExposedStream,

    /// <summary>A seekable stream that hides its buffer and returns at most one byte per read.</summary>
    ChunkedStream1,

    /// <summary>A seekable stream that hides its buffer and returns at most three bytes per read.</summary>
    ChunkedStream3,

    /// <summary>A seekable stream that hides its buffer and returns at most seven bytes per read.</summary>
    ChunkedStream7,

    /// <summary>A <see cref="System.IO.FileStream"/> over a temporary file that is deleted when the stream closes.</summary>
    FileStream,
}
