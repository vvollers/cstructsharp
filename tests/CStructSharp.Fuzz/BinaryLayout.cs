namespace CStructSharp.Fuzzing;

using CStructSharp;

/// <summary>The binary-roundtrip target's layout as a generated class, for the generated-vs-runtime differential.</summary>
[CStructLayout("struct root { byte count; uint16 values[count]; char name[]; };", PointerSize = 2)]
internal static partial class BinaryLayout
{
}
