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

    public const string BoundedTextShortRead = "Not enough bytes for the declared Encoded text buffer.";

    public const string BoundedTextInvalid = "Encoded text buffer contains an invalid byte sequence.";

    public const string TerminatedStringUnterminated = "Not enough bytes: the terminated string has no terminator before the end of the input.";

    public const string TerminatedStringInvalid = "String field contains bytes that are invalid for its encoding.";

    public const string WideTextInvalid = "Wide-character buffer contains an invalid UTF-16 code-unit sequence.";

    public const string PointerAddressRange = "Pointer address exceeds the supported stream address range.";

    public const string RelativePointerOverflow = "Relative pointer address overflowed the supported stream address range.";

    public const string PointerTargetVariableLength = "The configured pointer target limit does not allow a variable-length target.";

    public const string IdentifierShortRead = "Not enough bytes for a 16-byte identifier.";

    /// <summary>A negative array length from an expression.</summary>
    public static string NegativeArrayLength(string fieldName) => "Array length cannot be negative: " + fieldName;

    /// <summary>A terminated array whose terminator never comes.</summary>
    public static string TerminatedArrayUnterminated(string fieldName) => "Terminated array has no terminating zero element: " + fieldName;

    /// <summary>A bitfield placed past the end of its storage unit.</summary>
    public static string BitfieldExceedsUnit(string fieldName) => "Bitfield exceeds its storage unit: " + fieldName;

    /// <summary>A custom codec that rejected its input.</summary>
    public static string CustomCodecRejected(string codecName) => "Custom codec '" + codecName + "' rejected the input bytes.";

    /// <summary>A custom codec that threw while decoding.</summary>
    public static string CustomCodecFailed(string codecName, string reason) => "Custom codec '" + codecName + "' failed to decode a value: " + reason;

    /// <summary>A pointer whose target lies outside the input.</summary>
    public static string PointerTargetOutside(long targetAddress) => "Pointer target is outside the readable stream range: " + targetAddress.ToString(CultureInfo.InvariantCulture);

    /// <summary>A pointer target already being read on the active path.</summary>
    public static string CyclicPointer(long targetAddress) => "Cyclic pointer target detected at stream address " + targetAddress.ToString(CultureInfo.InvariantCulture) + ".";

    // The formatted texts below are built without FormattableString: on the runtime the interpolation handler formats
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

    /// <summary>A short read of a whole numeric array (the bulk reader checks the extent before reading).</summary>
    public static string ArrayShortRead(long count, int elementSize, long available)
        => "Not enough bytes: " + count.ToString(CultureInfo.InvariantCulture) + " elements of " + elementSize.ToString(CultureInfo.InvariantCulture) + " bytes need " + (count * elementSize).ToString(CultureInfo.InvariantCulture) + ", available " + available.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>A read-to-end array whose remaining bytes are not whole elements.</summary>
    public static string ToEndRemainder(long remaining, int elementSize, string fieldName)
        => "The remaining " + remaining.ToString(CultureInfo.InvariantCulture) + " bytes are not a whole number of " + elementSize.ToString(CultureInfo.InvariantCulture) + "-byte elements: " + fieldName;

    /// <summary>A custom codec that reported more bytes consumed than it was given.</summary>
    public static string CustomCodecConsumed(string codecName, int consumed, int available)
        => "Custom codec '" + codecName + "' reported " + consumed.ToString(CultureInfo.InvariantCulture) + " bytes consumed, but " + available.ToString(CultureInfo.InvariantCulture) + " were available.";

    /// <summary>A custom codec that needs more bytes than the input has.</summary>
    public static string CustomCodecShortRead(string codecName, int available)
        => "Not enough bytes: custom codec '" + codecName + "' needs more than the " + available.ToString(CultureInfo.InvariantCulture) + " available.";

    /// <summary>A custom codec that needs a window past the string byte limit.</summary>
    public static string CustomCodecLimit(string codecName, long limit)
        => "Custom codec '" + codecName + "' needs more than MaxStringBytes (" + limit.ToString(CultureInfo.InvariantCulture) + ") for one value.";
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

    /// <summary>A short read of a whole numeric array (the bulk reader checks the extent before reading).</summary>
    public static string ArrayShortRead(long count, int elementSize, long available)
        => string.Create(CultureInfo.InvariantCulture, $"Not enough bytes: {count} elements of {elementSize} bytes need {count * elementSize}, available {available}.");

    /// <summary>A read-to-end array whose remaining bytes are not whole elements.</summary>
    public static string ToEndRemainder(long remaining, int elementSize, string fieldName)
        => string.Create(CultureInfo.InvariantCulture, $"The remaining {remaining} bytes are not a whole number of {elementSize}-byte elements: {fieldName}");

    /// <summary>A custom codec that reported more bytes consumed than it was given.</summary>
    public static string CustomCodecConsumed(string codecName, int consumed, int available)
        => string.Create(CultureInfo.InvariantCulture, $"Custom codec '{codecName}' reported {consumed} bytes consumed, but {available} were available.");

    /// <summary>A custom codec that needs more bytes than the input has.</summary>
    public static string CustomCodecShortRead(string codecName, int available)
        => string.Create(CultureInfo.InvariantCulture, $"Not enough bytes: custom codec '{codecName}' needs more than the {available} available.");

    /// <summary>A custom codec that needs a window past the string byte limit.</summary>
    public static string CustomCodecLimit(string codecName, long limit)
        => string.Create(CultureInfo.InvariantCulture, $"Custom codec '{codecName}' needs more than MaxStringBytes ({limit}) for one value.");
#endif
}
