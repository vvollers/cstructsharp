namespace CStructSharp.Generated;

using System;

/// <summary>
///     Reads record <paramref name="index"/> of a sequence from <paramref name="offset"/> in <paramref name="source"/>
///     as its own region and reports the bytes it occupied; a failure names the record and carries the offset in
///     the input's coordinates (<paramref name="shift"/> plus the offset). The generated <c>Records</c> forms and the
///     runtime's <c>ParseMany</c> supply one; <see cref="RecordSequence"/> drives it.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
/// <param name="source">The bytes the records are read from.</param>
/// <param name="offset">The record's first byte in <paramref name="source"/>.</param>
/// <param name="index">The record's position in the sequence, from 0.</param>
/// <param name="shift">What <paramref name="source"/>'s first byte is in the input's coordinates (a stream's origin plus the window's start).</param>
/// <param name="options">The read options in force, with the operation's cancellation token.</param>
/// <param name="consumed">The record's encoded length.</param>
/// <returns>The record.</returns>
public delegate T RecordReader<out T>(ReadOnlyMemory<byte> source, int offset, int index, long shift, ReadOptions? options, out int consumed);
