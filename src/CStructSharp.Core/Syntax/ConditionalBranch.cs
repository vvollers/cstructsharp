namespace CStructSharp.Syntax;

/// <summary>A field's membership in an outer-to-inner sequence of conditional arms.</summary>
/// <param name="Group">The <c>if</c> or <c>switch</c> the field sits inside.</param>
/// <param name="Arm">The zero-based arm of <paramref name="Group"/> that contains the field, in source order.</param>
internal readonly record struct ConditionalBranch(ConditionalGroup Group, int Arm);
