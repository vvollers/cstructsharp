namespace CStructSharp.Streams;

using System.IO;

/// <summary>
///     The growable buffer behind <c>Serialize</c> that returns a new array. It is an ordinary expandable
///     <see cref="MemoryStream"/>; the distinct type tells the writer that a write cannot fail part-way for lack of
///     room, so it may write a block at once where it would otherwise write byte by byte.
/// </summary>
internal sealed class OwnedMemoryStream : MemoryStream
{
}
