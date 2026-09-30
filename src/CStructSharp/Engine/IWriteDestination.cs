namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Addressing;
using CStructSharp.Diagnostics;

/// <summary>
///     Byte access for the compiled engine's writer: positions, budget-checked writes, read-back of bytes already
///     written, and the stream view codec writers take. <see cref="WriteEngine"/> is written once over this interface
///     (<c>where TDestination : struct, IWriteDestination</c>), so .NET compiles a copy per destination type with direct
///     calls.
/// </summary>
/// <remarks>
///     Every member behaves as a <see cref="Streams.WriteBudgetStream"/> over the corresponding destination stream does:
///     the budget is checked before a byte moves, a failure leaves the position where that stream would leave it, and
///     bytes past the destination's end read back as nothing (a new array's or span's end is its
///     high-water mark; a caller's stream reads back the bytes it already held).
/// </remarks>
internal interface IWriteDestination
{
    /// <summary>Gets or sets the position in bytes from the destination's start; moving it charges nothing.</summary>
    long Position { get; set; }

    /// <summary>Gets the destination's length in bytes: the high-water mark of a new array or span, a caller's stream's own length.</summary>
    long Length { get; }

    /// <summary>Gets a value indicating whether bytes already in the destination can be read back.</summary>
    bool CanRead { get; }

    /// <summary>Gets a value indicating whether a typed array or narrow text may be written as one block rather than element by element (not into a stream or a union's staging).</summary>
    bool AllowsBlocks { get; }

    /// <summary>Gets the destination as the stream codec writers write to; it enforces the same budget.</summary>
    Stream Stream { get; }

    /// <summary>Writes bytes at the position after the budget and the destination's room are checked.</summary>
    /// <param name="bytes">The bytes.</param>
    void Write(ReadOnlySpan<byte> bytes);

    /// <summary>Writes zero bytes, the whole region checked against the budget first.</summary>
    /// <param name="count">The number of zero bytes.</param>
    void WriteZeroes(int count);

    /// <summary>Rejects a string whose encoded size exceeds the per-string limit.</summary>
    /// <param name="encodedByteCount">The encoded size in bytes.</param>
    void EnsureStringBytes(long encodedByteCount);

    /// <summary>Whether a block of <paramref name="size"/> bytes at the position, charged as <paramref name="chargedBytes"/>, can be written without a failure.</summary>
    /// <param name="size">The block's length in bytes.</param>
    /// <param name="chargedBytes">The physical traffic the block adds to the budget.</param>
    /// <returns>Whether the budget and the destination's room allow the block.</returns>
    bool CanAffordBlock(int size, int chargedBytes);

    /// <summary>Writes a prepared block, charging <paramref name="chargedBytes"/>.</summary>
    /// <param name="block">The bytes.</param>
    /// <param name="chargedBytes">The physical traffic to charge.</param>
    void WriteBlock(ReadOnlySpan<byte> block, int chargedBytes);

    /// <summary>Reads bytes already written at the position.</summary>
    /// <param name="buffer">The span to fill.</param>
    /// <returns>The number of bytes read; 0 at or past <see cref="Length"/>.</returns>
    int Read(Span<byte> buffer);

    /// <summary>
    ///     Attaches the operation's path and the position the failure left, by destination: the
    ///     position of a new array or span, or whatever the caller's stream reports (nothing when it
    ///     cannot report one).
    /// </summary>
    /// <param name="exception">The failure; context already attached is kept.</param>
    /// <param name="segments">The operation's parsed path.</param>
    void AttachContext(CStructException exception, IReadOnlyList<PathSegment> segments);
}
