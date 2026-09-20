namespace CStructSharp.Diagnostics;

using System.Globalization;

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

    public const string OutsideRegion = "The requested position is outside the supplied memory region.";

    // The three formatted texts are built without FormattableString: on the runtime the interpolation handler formats
    // the numbers straight into the result (no boxing, one allocation on an already exceptional path), and the
    // netstandard2.0 generator build - which never sits on a hot path - concatenates.
#if NETSTANDARD2_0
    /// <summary>A short read: how many bytes the item needed and how many the source still had.</summary>
    public static string ShortRead(long needed, long available)
        => "Not enough bytes: needed " + needed.ToString(CultureInfo.InvariantCulture) + ", available " + available.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>A short read from a source that cannot say how many bytes remain.</summary>
    public static string ShortRead(long needed)
        => "Not enough bytes: needed " + needed.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>An array length past <c>MaxArrayElements</c>.</summary>
    public static string ArrayLengthLimit(long count, int maximum)
        => "Array length " + count.ToString(CultureInfo.InvariantCulture) + " exceeds MaxArrayElements (" + maximum.ToString(CultureInfo.InvariantCulture) + ").";
#else
    /// <summary>A short read: how many bytes the item needed and how many the source still had.</summary>
    public static string ShortRead(long needed, long available)
        => string.Create(CultureInfo.InvariantCulture, $"Not enough bytes: needed {needed}, available {available}.");

    /// <summary>A short read from a source that cannot say how many bytes remain.</summary>
    public static string ShortRead(long needed)
        => string.Create(CultureInfo.InvariantCulture, $"Not enough bytes: needed {needed}.");

    /// <summary>An array length past <c>MaxArrayElements</c>.</summary>
    public static string ArrayLengthLimit(long count, int maximum)
        => string.Create(CultureInfo.InvariantCulture, $"Array length {count} exceeds MaxArrayElements ({maximum}).");
#endif
}
