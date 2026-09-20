namespace CStructSharp.Fuzzing;

using CStructSharp;

/// <summary>The path target's layout as a generated class, for the generated-vs-runtime differential.</summary>
[CStructLayout("struct leaf { byte value; }; union choice { uint16 number; byte raw[2]; }; struct root { byte count; uint16 values[4]; leaf nested; choice selected; uint16 *link; char name[]; };", Root = "root", PointerSize = 2)]
internal static partial class PathLayout
{
}
