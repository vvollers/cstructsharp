namespace CStructSharp.Codecs;

/// <summary>What the compiler needs to know about a custom codec: its name, fixed size (null when variable), and alignment.</summary>
internal readonly record struct CustomCodecDescriptor(string Name, int? FixedSize, int Alignment);
