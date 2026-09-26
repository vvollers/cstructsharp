namespace CStructSharp;

using System;
using System.Diagnostics.CodeAnalysis;

/// <summary>
///     The direct read and write of a mapped class bound to one fixed-layout struct, which the source generator emits
///     for a <c>[CStructMapped(Layout = "...")]</c> class whose layout struct has a build-time offset for every member.
///     <c>ReadValue&lt;T&gt;</c> and <c>Serialize</c> of a whole struct from memory use them instead of building a
///     <see cref="Values.StructValue"/> when the layout of the call has the same <see cref="FixedLayoutFingerprint"/>.
/// </summary>
/// <typeparam name="TSelf">The mapped class.</typeparam>
/// <remarks>
///     An implementation promises the results of the class's <see cref="ICStructMapped{TSelf}"/> members over a struct
///     of that layout: the same property values from the same bytes, and the same bytes from the same instance. It
///     returns <see langword="false"/> for anything it does not handle exactly that way, and the caller then takes the
///     <see cref="ICStructMapped{TSelf}"/> route. Hand-written implementations take on that promise; the generated
///     ones are held to it by the repository's parity tests.
/// </remarks>
public interface ICStructFixedMapped<TSelf>
    where TSelf : ICStructFixedMapped<TSelf>
{
    /// <summary>Gets the fingerprint of the layout struct the direct members were generated for.</summary>
    static abstract ulong FixedLayoutFingerprint { get; }

    /// <summary>Reads an instance from the struct's bytes, each property from its member's constant offset.</summary>
    /// <param name="source">The struct's bytes, at least its size.</param>
    /// <param name="trimFixedText">Whether fixed-capacity text drops its trailing NUL padding (<see cref="ReadOptions.TrimFixedText"/>).</param>
    /// <param name="value">The instance when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when a property cannot be read directly; nothing has been observed then.</returns>
    static abstract bool TryReadFixed(ReadOnlySpan<byte> source, bool trimFixedText, [MaybeNullWhen(false)] out TSelf value);

    /// <summary>Writes an instance into the struct's bytes, each property at its member's constant offset.</summary>
    /// <param name="value">The instance.</param>
    /// <param name="target">The struct's bytes, already zero (so padding is written as zeros), at least its size.</param>
    /// <returns><see langword="false"/> when the instance cannot be written directly; the caller then discards <paramref name="target"/>.</returns>
    static abstract bool TryWriteFixed(TSelf value, Span<byte> target);
}
