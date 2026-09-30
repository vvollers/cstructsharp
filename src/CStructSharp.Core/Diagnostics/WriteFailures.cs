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
    /// <summary>A write that would emit more bytes in total than the configured total byte limit allows.</summary>
    public const string TotalBytesLimit = "Write operation exceeded the configured total byte limit.";

    /// <summary>One string field whose encoded bytes exceed the configured per-string byte limit.</summary>
    public const string StringBytesLimit = "String field exceeded the configured encoded-byte write limit.";

    /// <summary>A value whose nested structs go deeper than the configured write nesting limit.</summary>
    public const string NestingLimit = "Maximum nested struct write depth exceeded.";

    /// <summary>An update-semantics write of a bitfield whose whole storage unit the destination does not hold yet.</summary>
    public const string IncompleteBitfieldUnit = "Cannot update a bitfield whose complete storage unit is not present.";

    /// <summary>An update-semantics write that keeps union storage where the destination does not hold the union's whole extent.</summary>
    public const string IncompleteUnionStorage = "Cannot preserve union storage because the complete existing extent is not present.";

    /// <summary>A serialized value that does not fit the caller's destination buffer.</summary>
    public const string DestinationCapacity = "The serialized value exceeds the supplied destination capacity.";

    /// <summary>A terminated string value that contains its own terminator once encoded.</summary>
    public const string TerminatorInValue = "String value contains its encoded terminator.";

    /// <summary>A string value with characters, such as lone surrogates, that its encoding rejects.</summary>
    public const string InvalidForEncoding = "String value contains characters that are invalid for its encoding.";

    /// <summary>A wide-character buffer whose UTF-16 code units do not form a valid sequence.</summary>
    public const string InvalidWideText = "Wide-character buffer contains an invalid UTF-16 code-unit sequence.";

    /// <summary>A UTF-16 text buffer whose capacity in bytes is odd, so it cannot hold whole code units.</summary>
    public const string Utf16CapacityOdd = "UTF-16 byte capacity must be even.";

    /// <summary>A string value with characters the selected encoding has no byte sequence for.</summary>
    public const string EncodingUnrepresentable = "String cannot be represented in the selected encoding.";

    /// <summary>An array with more elements than <c>MaxArrayElements</c> allows for a write.</summary>
    /// <param name="fieldName">The array field.</param>
    /// <returns>The message.</returns>
    public static string ArrayLengthLimit(string fieldName) => "Array length exceeds the configured write limit: " + fieldName;

    /// <summary>A fixed character array given more characters than it holds.</summary>
    /// <param name="fieldName">The character array field.</param>
    /// <param name="length">The supplied text's length in characters.</param>
    /// <param name="capacity">The array's capacity in characters.</param>
    /// <returns>The message.</returns>
    public static string FixedTextTooLong(string fieldName, int length, int capacity)
        => "String is too long for " + fieldName + ": " + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " > " + capacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>An encoded text buffer given more encoded bytes than it holds.</summary>
    /// <param name="fieldName">The text field.</param>
    /// <param name="encodedLength">The supplied text's encoded length in bytes.</param>
    /// <param name="capacity">The buffer's capacity in bytes.</param>
    /// <returns>The message.</returns>
    public static string BoundedTextTooLong(string fieldName, int encodedLength, int capacity)
        => "Encoded string is too long for " + fieldName + ": " + encodedLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + " encoded bytes > " + capacity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>An array value with more elements than the field permits (its declared count, or the array element limit).</summary>
    /// <param name="fieldName">The array field.</param>
    /// <param name="maximum">The permitted element count.</param>
    /// <returns>The message.</returns>
    public static string ArrayTooMany(string fieldName, int maximum)
        => "Array value for " + fieldName + " exceeds its permitted element count of " + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>A fixed array given a different number of elements than it declares.</summary>
    /// <param name="fieldName">The array field.</param>
    /// <param name="expected">The declared element count.</param>
    /// <param name="actual">The supplied element count.</param>
    /// <returns>The message.</returns>
    public static string ArrayLengthMismatch(string fieldName, int expected, int actual)
        => "Array length mismatch for " + fieldName + ": expected " + expected.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", got " + actual.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>A null value for a struct or union.</summary>
    /// <param name="compositeName">The struct or union type.</param>
    /// <returns>The message.</returns>
    public static string NullComposite(string compositeName) => "Null is not valid for struct or union value: " + compositeName;

    /// <summary>A null value for a field that is not a scalar pointer.</summary>
    /// <param name="fieldName">The field.</param>
    /// <returns>The message.</returns>
    public static string NullForNonPointer(string fieldName) => "Null is valid only for a scalar pointer field: " + fieldName;

    /// <summary>A value supplied for a conditional field whose condition is false.</summary>
    /// <param name="fieldName">The field.</param>
    /// <returns>The message.</returns>
    public static string InactiveConditionalField(string fieldName) => "Inactive conditional field supplied: " + fieldName;

    /// <summary>A field that the data does not supply.</summary>
    /// <param name="fieldName">The field.</param>
    /// <returns>The message.</returns>
    public static string NoValueSupplied(string fieldName) => "No value was supplied for '" + fieldName + "'.";

    /// <summary>A whole union value that names neither a member nor raw storage.</summary>
    /// <param name="unionName">The union type.</param>
    /// <param name="ways">How the caller's value type selects a member or raw storage.</param>
    /// <returns>The message.</returns>
    public static string WholeUnionNeedsSelection(string unionName, string ways) => "A whole union write requires " + ways + ": " + unionName;

    /// <summary>Raw union storage of a different size than the union.</summary>
    /// <param name="unionName">The union type.</param>
    /// <param name="expected">The union's size in bytes.</param>
    /// <param name="actual">The supplied storage's length in bytes.</param>
    /// <returns>The message.</returns>
    public static string RawStorageLengthMismatch(string unionName, int expected, int actual)
        => "Raw storage length mismatch for " + unionName + ": expected " + expected.ToString(CultureInfo.InvariantCulture) + ", got " + actual.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>A union value that selects a member the union does not declare.</summary>
    /// <param name="unionName">The union type.</param>
    /// <param name="member">The selected member name.</param>
    /// <returns>The message.</returns>
    public static string UnknownUnionMember(string unionName, string? member) => "Union '" + unionName + "' has no member named '" + member + "'.";

    /// <summary>A one-byte character outside the byte range.</summary>
    /// <param name="character">The character above U+00FF.</param>
    /// <returns>The message.</returns>
    public static string NarrowCharacter(char character)
        => "Character value U+" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + " does not fit the one-byte char type.";

    /// <summary>A custom codec that reported more bytes written than its window holds.</summary>
    /// <param name="codecName">The custom codec's name.</param>
    /// <param name="written">The byte count the codec reported.</param>
    /// <param name="window">The window size in bytes the codec was given.</param>
    /// <returns>The message.</returns>
    public static string CustomCodecWritten(string codecName, int written, int window)
        => "Custom codec '" + codecName + "' reported " + written.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes written into a " + window.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-byte window.";

    /// <summary>A custom codec with a fixed size that needs more room than that size to encode a value.</summary>
    /// <param name="codecName">The custom codec's name.</param>
    /// <param name="fixedSize">The codec's declared fixed size in bytes.</param>
    /// <returns>The message.</returns>
    public static string CustomCodecOversized(string codecName, int fixedSize)
        => "Custom codec '" + codecName + "' needs more than its fixed size of " + fixedSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes for one value.";

    /// <summary>A custom codec that needs a window past the string byte limit.</summary>
    /// <param name="codecName">The custom codec's name.</param>
    /// <param name="limit">The configured <c>MaxStringBytes</c> limit in bytes.</param>
    /// <returns>The message.</returns>
    public static string CustomCodecLimit(string codecName, long limit)
        => "Custom codec '" + codecName + "' needs more than MaxStringBytes (" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") for one value.";

    /// <summary>A custom codec that cannot encode a value.</summary>
    /// <param name="codecName">The custom codec's name.</param>
    /// <param name="value">The value the codec rejected.</param>
    /// <returns>The message.</returns>
    public static string CustomCodecCannotEncode(string codecName, object? value)
        => "Custom codec '" + codecName + "' cannot encode the value " + (value ?? "null") + ".";

    /// <summary>A custom codec that threw while encoding.</summary>
    /// <param name="codecName">The custom codec's name.</param>
    /// <param name="value">The value being encoded.</param>
    /// <param name="reason">The thrown exception's message.</param>
    /// <returns>The message.</returns>
    public static string CustomCodecFailed(string codecName, object? value, string reason)
        => "Custom codec '" + codecName + "' failed to encode " + (value ?? "null") + ": " + reason;

    /// <summary>
    ///     Says what was supplied and what the field accepts: the value as text, its CLR type when that is the
    ///     problem, and the integer range of a fixed-width codec when the value is a number outside it.
    /// </summary>
    /// <param name="value">The value the caller supplied.</param>
    /// <param name="typeSpelling">The field's type spelling.</param>
    /// <param name="acceptedRange">The integer range a fixed-width codec accepts (<see cref="AcceptedRange"/>), or <see langword="null"/>.</param>
    /// <returns>The message.</returns>
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
    /// <returns>The range as text, such as <c>0 to 255</c>, or <see langword="null"/>.</returns>
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

    /// <summary>A member whose type has no value writer, such as a <c>typedef void</c> alias written on its own.</summary>
    /// <param name="typeSpelling">The member's type as the layout spells it.</param>
    /// <returns>The failure message.</returns>
    public static string NoValueHandler(string typeSpelling) => "No handler for field type " + typeSpelling;

    /// <summary>A write of a root declaration that has no binary storage, such as a text <c>#define</c>.</summary>
    /// <param name="elementType">The declaration's kind, the name of its syntax type.</param>
    /// <returns>The failure message.</returns>
    public static string UnsupportedRootElement(string elementType) => "Unsupported element type for writing: " + elementType;
}
