namespace CStructSharp.Syntax;

/// <summary>The spelling of a constraint label in a layout's text, shared by the parser's reader and the printers.</summary>
internal static class ConstraintLabels
{
    /// <summary>Writes a label as a double-quoted string, escaping a quote and a backslash as the parser reads them.</summary>
    /// <param name="label">The label's text.</param>
    /// <returns>The quoted text, for example <c>"ext.magic"</c> with its quotes.</returns>
    public static string Quote(string label) => "\"" + label.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The text that follows a condition inside <c>@expect(...)</c> or <c>@supports(...)</c>: empty without a label, otherwise a comma and the quoted label.</summary>
    /// <param name="label">The label, or <see langword="null"/>.</param>
    /// <returns>The suffix to append after the condition text.</returns>
    public static string Suffix(string? label) => label is null ? string.Empty : ", " + Quote(label);
}
