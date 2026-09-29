namespace CStructSharp.Diagnostics;

using System;
using System.Globalization;

/// <summary>
///     The read-side diagnostic texts, in one place so the runtime reader, the budget stream, and the generated
///     code's <c>ReadCursor</c> report the same words for the same failure.
/// </summary>
internal static class ReadFailures
{
    /// <summary>The start of every short-read text, so a caller can recognize the whole family.</summary>
    public const string ShortReadPrefix = "Not enough bytes";

    /// <summary>The start of <see cref="PointerTargetOutside"/>'s text.</summary>
    public const string PointerTargetOutsidePrefix = "Pointer target is outside the readable stream range: ";

    /// <summary>The start of <see cref="CyclicPointer"/>'s text.</summary>
    public const string CyclicPointerPrefix = "Cyclic pointer target detected at stream address ";

    /// <summary>The part of <see cref="ArrayLengthLimit"/>'s text that names the limit.</summary>
    public const string ArrayLengthLimitMarker = " exceeds MaxArrayElements (";

    /// <summary>A read-to-end array that starts after the end of the input.</summary>
    public const string ArrayStartsPastEnd = ShortReadPrefix + ": the array starts beyond the end of the input.";

    /// <summary>A read that consumed more bytes in total than the configured total read-byte limit allows.</summary>
    public const string TotalBytesLimit = "Read operation exceeded the configured total read-byte limit.";

    /// <summary>A read that nested structs deeper than the configured maximum depth.</summary>
    public const string NestingLimit = "Maximum nested struct depth exceeded.";

    /// <summary>A read that followed pointers through more levels than the configured maximum depth.</summary>
    public const string PointerDepthLimit = "Maximum pointer dereference depth exceeded.";

    /// <summary>A pointer target whose size in bytes exceeds the configured pointer target limit.</summary>
    public const string PointerTargetLimit = "Pointer target exceeds the configured size limit.";

    /// <summary>An encoded text buffer whose declared byte length exceeds the configured string byte limit.</summary>
    public const string BoundedTextLimit = "Encoded text buffer exceeds the configured string byte limit.";

    /// <summary>A terminated string that ran past the configured encoded-byte limit without a terminator.</summary>
    public const string TerminatedStringLimit = "String field exceeded the configured encoded-byte limit.";

    /// <summary>A position, set by a seek or a padding skip, that lies outside the supplied memory region.</summary>
    public const string OutsideRegion = "The requested position is outside the supplied memory region.";

    /// <summary>An encoded text buffer whose declared bytes extend past the end of the input.</summary>
    public const string BoundedTextShortRead = ShortReadPrefix + " for the declared Encoded text buffer.";

    /// <summary>An encoded text buffer whose bytes do not decode in its declared encoding.</summary>
    public const string BoundedTextInvalid = "Encoded text buffer contains an invalid byte sequence.";

    /// <summary>A terminated string whose input ends before its terminator.</summary>
    public const string TerminatedStringUnterminated = ShortReadPrefix + ": the terminated string has no terminator before the end of the input.";

    /// <summary>A terminated string whose bytes do not decode in its declared encoding.</summary>
    public const string TerminatedStringInvalid = "String field contains bytes that are invalid for its encoding.";

    /// <summary>A wide-character buffer that holds an invalid UTF-16 sequence, such as an unpaired surrogate.</summary>
    public const string WideTextInvalid = "Wide-character buffer contains an invalid UTF-16 code-unit sequence.";

    /// <summary>A pointer value too large for the signed stream-position range.</summary>
    public const string PointerAddressRange = "Pointer address exceeds the supported stream address range.";

    /// <summary>A relative pointer whose base plus offset overflows the supported stream address range.</summary>
    public const string RelativePointerOverflow = "Relative pointer address overflowed the supported stream address range.";

    /// <summary>A pointer to a variable-length target while a pointer target size limit is configured.</summary>
    public const string PointerTargetVariableLength = "The configured pointer target limit does not allow a variable-length target.";

    /// <summary>A 16-byte identifier (such as a GUID) whose bytes extend past the end of the input.</summary>
    public const string IdentifierShortRead = ShortReadPrefix + " for a 16-byte identifier.";

    /// <summary>A terminated array whose terminator never comes.</summary>
    /// <param name="fieldName">The name of the array field, appended to the text.</param>
    /// <returns>The diagnostic text naming the field.</returns>
    public static string TerminatedArrayUnterminated(string fieldName) => "Terminated array has no terminating zero element: " + fieldName;

    /// <summary>A custom codec that rejected its input.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <returns>The diagnostic text naming the codec.</returns>
    public static string CustomCodecRejected(string codecName) => "Custom codec '" + codecName + "' rejected the input bytes.";

    /// <summary>A custom codec that threw while decoding.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="reason">The failure message reported by the codec, appended to the text.</param>
    /// <returns>The diagnostic text naming the codec and its failure.</returns>
    public static string CustomCodecFailed(string codecName, string reason) => "Custom codec '" + codecName + "' failed to decode a value: " + reason;

    /// <summary>A pointer whose target lies outside the input.</summary>
    /// <param name="targetAddress">The stream address, in bytes, that the pointer resolved to.</param>
    /// <returns>The diagnostic text, starting with <see cref="PointerTargetOutsidePrefix"/>.</returns>
    public static string PointerTargetOutside(long targetAddress) => PointerTargetOutsidePrefix + targetAddress.ToString(CultureInfo.InvariantCulture);

    /// <summary>A pointer target already being read on the active path.</summary>
    /// <param name="targetAddress">The stream address, in bytes, of the target that is already being read.</param>
    /// <returns>The diagnostic text, starting with <see cref="CyclicPointerPrefix"/>.</returns>
    public static string CyclicPointer(long targetAddress) => CyclicPointerPrefix + targetAddress.ToString(CultureInfo.InvariantCulture) + ".";

    // The formatted texts below are built without FormattableString: on the runtime the interpolation handler formats
    // the numbers straight into the result (no boxing, one allocation on an already exceptional path), and the
    // netstandard2.0 generator build - which never sits on a hot path - concatenates.
#if NETSTANDARD2_0

    /// <summary>A short read: how many bytes the item needed and how many the source still had.</summary>
    /// <param name="needed">The number of bytes the item needed.</param>
    /// <param name="available">The number of bytes the source still had.</param>
    /// <returns>The short-read text, starting with <see cref="ShortReadPrefix"/>.</returns>
    public static string ShortRead(long needed, long available)
        => ShortReadPrefix + ": needed " + needed.ToString(CultureInfo.InvariantCulture) + ", available " + available.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>A short read from a source that cannot say how many bytes remain.</summary>
    /// <param name="needed">The number of bytes the item needed.</param>
    /// <returns>The short-read text, starting with <see cref="ShortReadPrefix"/>, without an available count.</returns>
    public static string ShortRead(long needed)
        => ShortReadPrefix + ": needed " + needed.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>An array length past <c>MaxArrayElements</c>.</summary>
    /// <param name="count">The array length, in elements, that the layout asked for.</param>
    /// <param name="maximum">The configured <c>MaxArrayElements</c> limit, in elements.</param>
    /// <returns>The diagnostic text, containing <see cref="ArrayLengthLimitMarker"/>.</returns>
    public static string ArrayLengthLimit(Int128 count, int maximum)
        => "Array length " + count.ToString(CultureInfo.InvariantCulture) + ArrayLengthLimitMarker + maximum.ToString(CultureInfo.InvariantCulture) + ").";

    /// <summary>A short read of a whole numeric array (the bulk reader checks the extent before reading).</summary>
    /// <param name="count">The number of elements in the array.</param>
    /// <param name="elementSize">The size of one element, in bytes.</param>
    /// <param name="available">The number of bytes the source still had.</param>
    /// <returns>The short-read text, starting with <see cref="ShortReadPrefix"/>, with the needed byte total.</returns>
    public static string ArrayShortRead(long count, int elementSize, long available)
        => ShortReadPrefix + ": " + count.ToString(CultureInfo.InvariantCulture) + " elements of " + elementSize.ToString(CultureInfo.InvariantCulture) + " bytes need " + (count * elementSize).ToString(CultureInfo.InvariantCulture) + ", available " + available.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>A read-to-end array whose remaining bytes are not whole elements.</summary>
    /// <param name="remaining">The number of bytes left in the input when the array starts.</param>
    /// <param name="elementSize">The size of one element, in bytes.</param>
    /// <param name="fieldName">The name of the array field, appended to the text.</param>
    /// <returns>The diagnostic text naming the field.</returns>
    public static string ToEndRemainder(long remaining, int elementSize, string fieldName)
        => "The remaining " + remaining.ToString(CultureInfo.InvariantCulture) + " bytes are not a whole number of " + elementSize.ToString(CultureInfo.InvariantCulture) + "-byte elements: " + fieldName;

    /// <summary>A custom codec that reported more bytes consumed than it was given.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="consumed">The number of bytes the codec reported as consumed.</param>
    /// <param name="available">The number of bytes the codec was given.</param>
    /// <returns>The diagnostic text naming the codec and both byte counts.</returns>
    public static string CustomCodecConsumed(string codecName, int consumed, int available)
        => "Custom codec '" + codecName + "' reported " + consumed.ToString(CultureInfo.InvariantCulture) + " bytes consumed, but " + available.ToString(CultureInfo.InvariantCulture) + " were available.";

    /// <summary>A custom codec with a fixed size that reported more bytes consumed than that size.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="consumed">The number of bytes the codec reported as consumed.</param>
    /// <param name="fixedSize">The codec's declared fixed size in bytes.</param>
    /// <returns>The diagnostic text naming the codec and both byte counts.</returns>
    public static string CustomCodecOversized(string codecName, int consumed, int fixedSize)
        => "Custom codec '" + codecName + "' reported " + consumed.ToString(CultureInfo.InvariantCulture) + " bytes consumed, more than its fixed size of " + fixedSize.ToString(CultureInfo.InvariantCulture) + " bytes.";

    /// <summary>A custom codec that needs more bytes than the input has.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="available">The number of bytes the input still had.</param>
    /// <returns>The short-read text, starting with <see cref="ShortReadPrefix"/>, naming the codec.</returns>
    public static string CustomCodecShortRead(string codecName, int available)
        => ShortReadPrefix + ": custom codec '" + codecName + "' needs more than the " + available.ToString(CultureInfo.InvariantCulture) + " available.";

    /// <summary>A custom codec that needs a window past the string byte limit.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="limit">The configured <c>MaxStringBytes</c> limit, in bytes.</param>
    /// <returns>The diagnostic text naming the codec and the limit.</returns>
    public static string CustomCodecLimit(string codecName, long limit)
        => "Custom codec '" + codecName + "' needs more than MaxStringBytes (" + limit.ToString(CultureInfo.InvariantCulture) + ") for one value.";
#else

    /// <summary>A short read: how many bytes the item needed and how many the source still had.</summary>
    /// <param name="needed">The number of bytes the item needed.</param>
    /// <param name="available">The number of bytes the source still had.</param>
    /// <returns>The short-read text, starting with <see cref="ShortReadPrefix"/>.</returns>
    public static string ShortRead(long needed, long available)
        => string.Create(CultureInfo.InvariantCulture, $"{ShortReadPrefix}: needed {needed}, available {available}.");

    /// <summary>A short read from a source that cannot say how many bytes remain.</summary>
    /// <param name="needed">The number of bytes the item needed.</param>
    /// <returns>The short-read text, starting with <see cref="ShortReadPrefix"/>, without an available count.</returns>
    public static string ShortRead(long needed)
        => string.Create(CultureInfo.InvariantCulture, $"{ShortReadPrefix}: needed {needed}.");

    /// <summary>An array length past <c>MaxArrayElements</c>.</summary>
    /// <param name="count">The array length, in elements, that the layout asked for.</param>
    /// <param name="maximum">The configured <c>MaxArrayElements</c> limit, in elements.</param>
    /// <returns>The diagnostic text, containing <see cref="ArrayLengthLimitMarker"/>.</returns>
    public static string ArrayLengthLimit(Int128 count, int maximum)
        => string.Create(CultureInfo.InvariantCulture, $"Array length {count}{ArrayLengthLimitMarker}{maximum}).");

    /// <summary>A short read of a whole numeric array (the bulk reader checks the extent before reading).</summary>
    /// <param name="count">The number of elements in the array.</param>
    /// <param name="elementSize">The size of one element, in bytes.</param>
    /// <param name="available">The number of bytes the source still had.</param>
    /// <returns>The short-read text, starting with <see cref="ShortReadPrefix"/>, with the needed byte total.</returns>
    public static string ArrayShortRead(long count, int elementSize, long available)
        => string.Create(CultureInfo.InvariantCulture, $"{ShortReadPrefix}: {count} elements of {elementSize} bytes need {count * elementSize}, available {available}.");

    /// <summary>A read-to-end array whose remaining bytes are not whole elements.</summary>
    /// <param name="remaining">The number of bytes left in the input when the array starts.</param>
    /// <param name="elementSize">The size of one element, in bytes.</param>
    /// <param name="fieldName">The name of the array field, appended to the text.</param>
    /// <returns>The diagnostic text naming the field.</returns>
    public static string ToEndRemainder(long remaining, int elementSize, string fieldName)
        => string.Create(CultureInfo.InvariantCulture, $"The remaining {remaining} bytes are not a whole number of {elementSize}-byte elements: {fieldName}");

    /// <summary>A custom codec that reported more bytes consumed than it was given.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="consumed">The number of bytes the codec reported as consumed.</param>
    /// <param name="available">The number of bytes the codec was given.</param>
    /// <returns>The diagnostic text naming the codec and both byte counts.</returns>
    public static string CustomCodecConsumed(string codecName, int consumed, int available)
        => string.Create(CultureInfo.InvariantCulture, $"Custom codec '{codecName}' reported {consumed} bytes consumed, but {available} were available.");

    /// <summary>A custom codec with a fixed size that reported more bytes consumed than that size.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="consumed">The number of bytes the codec reported as consumed.</param>
    /// <param name="fixedSize">The codec's declared fixed size in bytes.</param>
    /// <returns>The diagnostic text naming the codec and both byte counts.</returns>
    public static string CustomCodecOversized(string codecName, int consumed, int fixedSize)
        => string.Create(CultureInfo.InvariantCulture, $"Custom codec '{codecName}' reported {consumed} bytes consumed, more than its fixed size of {fixedSize} bytes.");

    /// <summary>A custom codec that needs more bytes than the input has.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="available">The number of bytes the input still had.</param>
    /// <returns>The short-read text, starting with <see cref="ShortReadPrefix"/>, naming the codec.</returns>
    public static string CustomCodecShortRead(string codecName, int available)
        => string.Create(CultureInfo.InvariantCulture, $"{ShortReadPrefix}: custom codec '{codecName}' needs more than the {available} available.");

    /// <summary>A custom codec that needs a window past the string byte limit.</summary>
    /// <param name="codecName">The registered name of the custom codec.</param>
    /// <param name="limit">The configured <c>MaxStringBytes</c> limit, in bytes.</param>
    /// <returns>The diagnostic text naming the codec and the limit.</returns>
    public static string CustomCodecLimit(string codecName, long limit)
        => string.Create(CultureInfo.InvariantCulture, $"Custom codec '{codecName}' needs more than MaxStringBytes ({limit}) for one value.");
#endif
}
