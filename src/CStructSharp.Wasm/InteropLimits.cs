namespace CStructSharpWeb.Wasm;

/// <summary>Numeric limits the browser bridge shares between input validation and JSON output.</summary>
/// <remarks>Only constants: the file is also compiled into the managed tests that exercise the JSON writer.</remarks>
internal static class InteropLimits
{
    /// <summary>
    ///     The largest integer a JavaScript <c>Number</c> represents exactly: 2^53 - 1, JavaScript's
    ///     <c>Number.MAX_SAFE_INTEGER</c>. JSON output writes an integer within ± this value as a number and a larger
    ///     one as a decimal string, so JavaScript never rounds it; byte-count options and JavaScript source sizes may
    ///     not exceed it.
    /// </summary>
    public const long MaximumSafeInteger = 9_007_199_254_740_991;

    /// <summary>
    ///     The most bytes of JSON text one export result may hold: 536,870,888 (2^29 - 24), the longest string V8 -
    ///     the engine of Chrome, Edge and Node.js - can create on 64-bit hosts. Firefox and Safari allow longer
    ///     strings, so a result within this length decodes into one string in every supported host. The bridge's JSON
    ///     is pure ASCII, so its byte count is its string length. A longer parse result fails with the
    ///     <c>read-budget</c> category instead of failing inside the JavaScript runtime.
    /// </summary>
    public const int MaximumResultLength = 536_870_888;
}
