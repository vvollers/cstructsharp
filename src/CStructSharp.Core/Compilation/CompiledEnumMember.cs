namespace CStructSharp.Compilation;

/// <summary>Stores one validated symbolic enum member in its exact mathematical domain.</summary>
/// <param name="Name">The member's declared name.</param>
/// <param name="RawBits">The member's value as two's-complement storage bits in the low bits of the enum's declared width; higher bits are zero.</param>
internal readonly record struct CompiledEnumMember(string Name, ulong RawBits);
