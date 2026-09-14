namespace CStructSharp;

using CStructSharp.Structure;

/// <summary>Indexes one field's arm membership in its containing composite's operation-local decision array.</summary>
internal readonly record struct CompiledConditionalBranch(ConditionalGroup Group, int Slot, int Arm);
