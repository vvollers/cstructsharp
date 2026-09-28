namespace CStructSharp.Diagnostics;

using System;
using System.Globalization;

/// <summary>
///     The diagnostic texts about a layout that reading, writing, layout compilation and generated code share, so each
///     reports the same words; the exception type still says which operation failed.
/// </summary>
internal static class LayoutFailures
{
    /// <summary>A negative array length from an expression.</summary>
    /// <param name="fieldName">The array field.</param>
    /// <returns>The message.</returns>
    public static string NegativeArrayLength(string fieldName) => "Array length cannot be negative: " + fieldName;

    /// <summary>
    ///     An expression result that is valid in the 128-bit expression domain but too large for the 32-bit integer the
    ///     layout stores it in (an alignment, an offset assertion, a bitfield width, a compile-time array length).
    /// </summary>
    /// <param name="context">What was evaluated, in the caller's words, such as <c>array length for payload</c>.</param>
    /// <param name="value">The exact result.</param>
    /// <returns>The message, naming the value.</returns>
    public static string OutsideInt32(string context, Int128 value)
        => "The " + context + " is " + value.ToString(CultureInfo.InvariantCulture) + ", which does not fit in a signed 32-bit integer.";

    /// <summary>A bitfield placed past the end of its storage unit.</summary>
    /// <param name="fieldName">The bitfield.</param>
    /// <returns>The message.</returns>
    public static string BitfieldExceedsUnit(string fieldName) => "Bitfield exceeds its storage unit: " + fieldName;
}
