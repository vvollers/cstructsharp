namespace CStructSharp.Generated.Parity;

/// <summary>Generates a fixed struct with nested struct members, whose classes keep their implicit constructors.</summary>
[CStructLayout("struct inner { uint16 a; uint16 b; }; struct outer { uint8 tag; inner first; inner second; };", Root = "outer")]
public static partial class NestedValueLayout
{
}
