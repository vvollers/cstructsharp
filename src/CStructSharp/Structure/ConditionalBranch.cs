namespace CStructSharp.Structure;

/// <summary>A field's membership in an outer-to-inner sequence of conditional arms.</summary>
internal readonly record struct ConditionalBranch(ConditionalGroup Group, int Arm);
