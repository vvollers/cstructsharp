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
}
