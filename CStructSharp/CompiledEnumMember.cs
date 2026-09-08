namespace CStructSharp;

/// <summary>Stores one validated symbolic enum member in its exact mathematical domain.</summary>
internal readonly record struct CompiledEnumMember(string Name, ulong RawBits);
