namespace CStructSharp;

/// <summary>Chooses what a read does with text that is not valid in its encoding.</summary>
/// <remarks>
///     <para>
///         The choice applies to every text type that can hold invalid data: <c>utf8</c> and <c>utf16le</c>/<c>utf16be</c> buffers,
///         <c>wchar[N]</c> arrays, and terminated strings (<c>cstring</c>, <c>utf8_string_zero</c>, <c>string</c>, ...).
///         <c>latin1</c>, <c>cp437</c>, and <c>char[N]</c> map every byte to a character, so they never fail.
///     </para>
///     <para>
///         <see cref="Replace"/> loses the original bytes: two different invalid sequences read as the same replacement
///         character. A generated view keeps them reachable, because each text member also exposes its bytes.
///     </para>
/// </remarks>
public enum InvalidTextHandling
{
    /// <summary>The read fails with <see cref="Diagnostics.CStructReadException"/> naming the field; this is the default.</summary>
    Fail = 0,

    /// <summary>
    ///     Each invalid byte sequence (UTF-8), unpaired surrogate (UTF-16), or non-ASCII byte (<c>cstring</c> and the other
    ///     ASCII strings) becomes U+FFFD, the Unicode replacement character, and the read continues. Terminators, lengths and
    ///     read budgets are unaffected.
    /// </summary>
    Replace = 1,
}
