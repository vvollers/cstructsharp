namespace CStructSharp.Tests;

/// <summary>A memory stream that reports it cannot be read, to exercise an operation's readable-source check.</summary>
internal sealed class UnreadableStream : MemoryStream
{
    /// <summary>Gets false: the stream reports that it cannot be read.</summary>
    public override bool CanRead => false;
}
