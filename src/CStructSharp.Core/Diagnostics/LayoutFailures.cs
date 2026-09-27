namespace CStructSharp.Diagnostics;

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

    /// <summary>A bitfield placed past the end of its storage unit.</summary>
    /// <param name="fieldName">The bitfield.</param>
    /// <returns>The message.</returns>
    public static string BitfieldExceedsUnit(string fieldName) => "Bitfield exceeds its storage unit: " + fieldName;
}
