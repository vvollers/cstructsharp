namespace CStructSharp.Memory.Metadata;

/// <summary>One direct ISF field, preserving its original descriptor even for unsupported or unresolved referenced types.</summary>
/// <param name="Name">Recorded member name, possibly empty.</param>
/// <param name="Offset">Byte offset from the containing record's start.</param>
/// <param name="Kind">Recorded descriptor kind.</param>
/// <param name="TypeDescriptor">Original JSON type descriptor; shallow inspection does not validate its referenced types.</param>
public sealed record IsfMemberDescription(string Name, long Offset, string Kind, string TypeDescriptor);
