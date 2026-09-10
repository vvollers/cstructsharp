namespace CStructSharp;

using System;
using System.IO;

/// <summary>
///     Classifies which exceptions from a caller-supplied physical <see cref="Stream" /> represent a genuine
///     physical-stream failure worth wrapping in a CStructSharp-specific exception, shared by every stream
///     wrapper that delegates to a caller-owned inner/baseline stream.
/// </summary>
internal static class StreamFailureClassification
{
    /// <summary>
    ///     Returns whether <paramref name="exception" /> represents a physical-stream failure: <see cref="IOException" />
    ///     for an ordinary I/O failure, <see cref="NotSupportedException" /> for an operation the physical stream
    ///     does not implement (for example, <c>SetLength</c> on a stream that cannot resize), and
    ///     <see cref="ObjectDisposedException" /> for a stream disposed out from under an in-progress operation.
    ///     Anything else propagates unclassified, since it is more likely a bug than an expected physical failure.
    /// </summary>
    public static bool IsPhysicalStreamFailure(Exception exception)
    {
        return exception is IOException or NotSupportedException or ObjectDisposedException;
    }
}
