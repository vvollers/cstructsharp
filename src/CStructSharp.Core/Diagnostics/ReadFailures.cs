namespace CStructSharp.Diagnostics;

using System;

/// <summary>
///     The read-side diagnostic texts, in one place so the runtime reader, the budget stream, and the generated
///     code's <c>ReadCursor</c> report the same words for the same failure.
/// </summary>
internal static class ReadFailures
{
    public const string TotalBytesLimit = "Read operation exceeded the configured total read-byte limit.";

    public const string NestingLimit = "Maximum nested struct depth exceeded.";

    public const string PointerDepthLimit = "Maximum pointer dereference depth exceeded.";

    public const string PointerTargetLimit = "Pointer target exceeds the configured size limit.";

    public const string BoundedTextLimit = "Encoded text buffer exceeds the configured string byte limit.";

    public const string TerminatedStringLimit = "String field exceeded the configured encoded-byte limit.";

    /// <summary>A short read: how many bytes the item needed and how many the source still had.</summary>
    public static string ShortRead(long needed, long available)
        => FormattableString.Invariant($"Not enough bytes: needed {needed}, available {available}.");

    /// <summary>A short read from a source that cannot say how many bytes remain.</summary>
    public static string ShortRead(long needed)
        => FormattableString.Invariant($"Not enough bytes: needed {needed}.");

    /// <summary>An array length past <c>MaxArrayElements</c>.</summary>
    public static string ArrayLengthLimit(long count, int maximum)
        => FormattableString.Invariant($"Array length {count} exceeds MaxArrayElements ({maximum}).");
}
