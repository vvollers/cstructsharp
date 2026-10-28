namespace CStructSharp.Memory.Metadata;

/// <summary>A user type's recorded ISF shape without recursive import or validation of dependencies.</summary>
/// <param name="Name">Exact user_types table identity.</param>
/// <param name="Kind">Recorded ISF kind, retaining unsupported kind names.</param>
/// <param name="Size">Recorded storage size in bytes.</param>
/// <param name="Members">Direct members in declaration order; referenced types are not resolved.</param>
public sealed record IsfTypeDescription(string Name, string Kind, int Size, IReadOnlyList<IsfMemberDescription> Members);
