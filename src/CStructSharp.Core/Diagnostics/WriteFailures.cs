namespace CStructSharp.Diagnostics;

using System;
using System.Globalization;
using CStructSharp.Codecs;

/// <summary>
///     The write-side diagnostic texts, in one place so the runtime writer, the budget stream, and the generated
///     code's <c>WriteCursor</c> report the same words for the same failure.
/// </summary>
internal static class WriteFailures
{
    public const string TotalBytesLimit = "Write operation exceeded the configured total byte limit.";

    public const string StringBytesLimit = "String field exceeded the configured encoded-byte write limit.";

    public const string NestingLimit = "Maximum nested struct write depth exceeded.";

    public const string DestinationCapacity = "The serialized value exceeds the supplied destination capacity.";

    public const string TerminatorInValue = "String value contains its encoded terminator.";

    public const string InvalidForEncoding = "String value contains characters that are invalid for its encoding.";

    public const string InvalidWideText = "Wide-character buffer contains an invalid UTF-16 code-unit sequence.";

    public const string Utf16CapacityOdd = "UTF-16 byte capacity must be even.";

    public const string EncodingUnrepresentable = "String cannot be represented in the selected encoding.";

    /// <summary>An array with more elements than <c>MaxArrayElements</c> allows for a write.</summary>
    public static string ArrayLengthLimit(string fieldName) => "Array length exceeds the configured write limit: " + fieldName;

    /// <summary>A fixed character array given more characters than it holds.</summary>
    public static string FixedTextTooLong(string fieldName, int length, int capacity)
        => "String is too long for " + fieldName + ": " + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " > " + capacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>An encoded text buffer given more encoded bytes than it holds.</summary>
    public static string BoundedTextTooLong(string fieldName, int encodedLength, int capacity)
        => "Encoded string is too long for " + fieldName + ": " + encodedLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + " encoded bytes > " + capacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>An array value with more elements than the field permits (its declared count, or the array element limit).</summary>
    public static string ArrayTooMany(string fieldName, int maximum)
        => "Array value for " + fieldName + " exceeds its permitted element count of " + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>A fixed array given a different number of elements than it declares.</summary>
    public static string ArrayLengthMismatch(string fieldName, int expected, int actual)
        => "Array length mismatch for " + fieldName + ": expected " + expected.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", got " + actual.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>A one-byte character outside the byte range.</summary>
    public static string NarrowCharacter(char character)
        => "Character value U+" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + " does not fit the one-byte char type.";

    /// <summary>A custom codec that reported more bytes written than its window holds.</summary>
    public static string CustomCodecWritten(string codecName, int written, int window)
        => "Custom codec '" + codecName + "' reported " + written.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes written into a " + window.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-byte window.";

    /// <summary>A custom codec that needs a window past the string byte limit.</summary>
    public static string CustomCodecLimit(string codecName, long limit)
        => "Custom codec '" + codecName + "' needs more than MaxStringBytes (" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") for one value.";

    /// <summary>A custom codec that cannot encode a value.</summary>
    public static string CustomCodecCannotEncode(string codecName, object? value)
        => "Custom codec '" + codecName + "' cannot encode the value " + (value ?? "null") + ".";

    /// <summary>A custom codec that threw while encoding.</summary>
    public static string CustomCodecFailed(string codecName, object? value, string reason)
        => "Custom codec '" + codecName + "' failed to encode " + (value ?? "null") + ": " + reason;

    /// <summary>
    ///     Says what was supplied and what the field accepts: the value as text, its CLR type when that is the
    ///     problem, and the integer range of a fixed-width codec when the value is a number outside it.
    /// </summary>
    /// <param name="value">The value the caller supplied.</param>
    /// <param name="typeSpelling">The field's type spelling.</param>
    /// <param name="acceptedRange">The integer range a fixed-width codec accepts (<see cref="AcceptedRange"/>), or <see langword="null"/>.</param>
    public static string UnwritableValue(object? value, string typeSpelling, string? acceptedRange)
    {
        string shown = value switch
        {
            null => "null",
            string text => "\"" + text + "\"",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.GetType().Name,
        };
        string accepts = acceptedRange is { Length: > 0 } ? $"{typeSpelling} accepts {acceptedRange}" : typeSpelling;
        return value is string or null || value is not IFormattable
                   ? $"Value {shown} cannot be written as {typeSpelling}."
                   : $"Value {shown} does not fit: {accepts}.";
    }

    /// <summary>The integer range a fixed-width integer codec accepts, or <see langword="null"/> for other kinds.</summary>
    /// <param name="kind">The codec kind.</param>
    public static string? AcceptedRange(PrimitiveCodecKind kind)
    {
        return kind switch
        {
            PrimitiveCodecKind.UInt8 => "0 to 255",
            PrimitiveCodecKind.Int8 => "-128 to 127",
            PrimitiveCodecKind.UInt16 => "0 to 65535",
            PrimitiveCodecKind.Int16 => "-32768 to 32767",
            PrimitiveCodecKind.UInt24 => "0 to 16777215",
            PrimitiveCodecKind.Int24 => "-8388608 to 8388607",
            PrimitiveCodecKind.UInt32 => "0 to 4294967295",
            PrimitiveCodecKind.Int32 => "-2147483648 to 2147483647",
            PrimitiveCodecKind.UInt48 => "0 to 281474976710655",
            PrimitiveCodecKind.Int48 => "-140737488355328 to 140737488355327",
            PrimitiveCodecKind.UInt64 => "0 to 18446744073709551615",
            PrimitiveCodecKind.Int64 => "-9223372036854775808 to 9223372036854775807",
            _ => null,
        };
    }
}
