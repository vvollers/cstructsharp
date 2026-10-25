namespace CStructSharp.Syntax;

/// <summary>The spelling of a constraint label in a layout's text, shared by the parser's reader and the printers.</summary>
internal static class ConstraintLabels
{
    /// <summary>The suffix keyword of a constraint, without the <c>@</c>.</summary>
    /// <param name="kind">What a failure means.</param>
    /// <param name="isMagic">Whether the constraint was written <c>@magic</c> (its kind is then <see cref="CStructSharp.Diagnostics.ConstraintKind.Unsupported"/>).</param>
    /// <returns><c>magic</c>, <c>expect</c> or <c>supports</c>.</returns>
    public static string Keyword(CStructSharp.Diagnostics.ConstraintKind kind, bool isMagic)
        => isMagic ? "magic" : kind == CStructSharp.Diagnostics.ConstraintKind.Malformed ? "expect" : "supports";

    /// <summary>Writes a label as a double-quoted string, escaping a quote and a backslash as the parser reads them.</summary>
    /// <param name="label">The label's text.</param>
    /// <returns>The quoted text, for example <c>"ext.magic"</c> with its quotes.</returns>
    public static string Quote(string label) => "\"" + label.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The text that follows a condition inside <c>@expect(...)</c> or <c>@supports(...)</c>: empty without a label, otherwise a comma and the quoted label, then the quoted message when there is one.</summary>
    /// <param name="label">The label, or <see langword="null"/>.</param>
    /// <param name="description">The message, or <see langword="null"/>; it is only written after a label.</param>
    /// <returns>The suffix to append after the condition text.</returns>
    public static string Suffix(string? label, string? description = null)
        => label is null ? string.Empty : ", " + Quote(label) + (description is null ? string.Empty : ", " + Quote(description));
}
