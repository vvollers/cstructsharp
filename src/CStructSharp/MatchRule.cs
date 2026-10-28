namespace CStructSharp;

/// <summary>One recognition rule and whether a prepared matcher checks it.</summary>
/// <param name="Field">Declaring field name.</param>
/// <param name="Kind">The declaration keyword: magic, expect or supports.</param>
/// <param name="Label">Optional authored label.</param>
/// <param name="Checked">Whether recognition evaluates this rule.</param>
/// <param name="Reason">Why the rule is checked or excluded; recognition never promises successful parsing.</param>
public sealed record MatchRule(string Field, string Kind, string? Label, bool Checked, string Reason);
