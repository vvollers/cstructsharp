namespace CStructSharp.Generated;

using System;
using System.ComponentModel;

/// <summary>
///     Reads one value from the start of the bytes it is handed as the span form of an operation does and reports where
///     the read ended. The buffered forms (a segmented sequence, a stream read through a buffer) run it over the bytes
///     they buffered and run it again over more of the input when it needs bytes past them; see
///     <see cref="ReadCursor.ReadSequence{TReader, TResult}"/>.
/// </summary>
/// <typeparam name="TResult">The value the read produces.</typeparam>
/// <remarks>
///     Implement it on a struct that carries the operation's state (the generated code carries its layout variables):
///     the buffered forms take the reader as a generic struct argument, so each run is a direct call and the operation
///     allocates no delegate or closure.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IBufferedReader<TResult>
{
    /// <summary>Reads the value from <paramref name="source"/>.</summary>
    /// <param name="source">The buffered bytes; offset 0 is the operation's coordinate zero.</param>
    /// <param name="options">
    ///     The read options for this run; pass them unchanged to the span reader, which learns from them whether the
    ///     input continues past <paramref name="source"/>.
    /// </param>
    /// <param name="consumed">Where the read ended, in bytes from <paramref name="source"/>'s first byte.</param>
    /// <returns>The value.</returns>
    TResult Read(ReadOnlySpan<byte> source, ReadOptions? options, out long consumed);
}
