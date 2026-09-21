namespace CStructSharp;

using System;

/// <summary>
///     Implemented by the root class the <c>[CStructLayout]</c> generator emits, so generic code can accept any
///     generated root: the runtime <see cref="CStruct"/> for the same layout, the root name, and the generated
///     span operations.
/// </summary>
/// <typeparam name="TSelf">The generated root class.</typeparam>
public interface ICStructGenerated<TSelf>
    where TSelf : ICStructGenerated<TSelf>
{
    /// <summary>Gets the runtime layout the class was generated from (built lazily on first use).</summary>
    static abstract CStruct Layout { get; }

    /// <summary>Gets the declaration name the class corresponds to.</summary>
    static abstract string RootName { get; }

    /// <summary>Parses one value from <paramref name="source"/> with the generated reader.</summary>
    /// <param name="source">The bytes; offset 0 is coordinate zero.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The parsed value.</returns>
    static abstract TSelf Parse(ReadOnlySpan<byte> source, ReadOptions? options = null);

    /// <summary>
    ///     Parses one value without throwing for a read, path, or limit failure: <see langword="false"/> and the
    ///     failure the throwing form would have raised. Cancellation and argument errors throw as in <see cref="Parse"/>.
    /// </summary>
    /// <param name="source">The bytes; offset 0 is coordinate zero.</param>
    /// <param name="value">The parsed value, or <see langword="null"/> when the read failed.</param>
    /// <param name="failure">The failure, or <see langword="null"/>.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>Whether the read succeeded.</returns>
    static abstract bool TryParse(ReadOnlySpan<byte> source, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TSelf value, out Diagnostics.CStructException? failure, ReadOptions? options = null);

    /// <summary>Serializes <paramref name="value"/> into <paramref name="destination"/> with the generated writer.</summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="options">The write options; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The number of bytes written.</returns>
    static abstract int Serialize(TSelf value, Span<byte> destination, WriteOptions? options = null);
}
