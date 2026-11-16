namespace CStructSharp.Codecs;

/// <summary>What the compiler needs to know about a custom codec: its name, fixed size (null when variable), and alignment.</summary>
/// <param name="Name">The type name layouts use for the codec.</param>
/// <param name="FixedSize">The encoded size in bytes, or <see langword="null"/> when each value determines its own size.</param>
/// <param name="Alignment">The codec's alignment in bytes.</param>
internal readonly record struct CustomCodecDescriptor(string Name, int? FixedSize, int Alignment);
