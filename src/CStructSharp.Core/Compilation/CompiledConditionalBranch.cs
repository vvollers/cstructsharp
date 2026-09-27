namespace CStructSharp.Compilation;

/// <summary>One arm a compiled field sits in.</summary>
/// <param name="Group">The decision.</param>
/// <param name="Slot">The decision's index in its composite's per-instance array of selected arms.</param>
/// <param name="Arm">The arm: 1 or 0 for an <c>if</c>; a switch's case index, or -1 for <c>default</c>.</param>
internal readonly record struct CompiledConditionalBranch(CompiledConditionalGroup Group, int Slot, int Arm);
