namespace CStructSharpWeb.Wasm;

/// <summary>Numeric limits the browser bridge shares between input validation and JSON output.</summary>
internal static class InteropLimits
{
    /// <summary>
    ///     The largest integer a JavaScript <c>Number</c> represents exactly: 2^53 - 1, JavaScript's
    ///     <c>Number.MAX_SAFE_INTEGER</c>. JSON output writes an integer within ± this value as a number and a larger
    ///     one as a decimal string, so JavaScript never rounds it; byte-count options and JavaScript source sizes may
    ///     not exceed it.
    /// </summary>
    public const long MaximumSafeInteger = 9_007_199_254_740_991;
}
