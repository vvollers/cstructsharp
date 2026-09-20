namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The <c>prim-le-record</c> fixture as a generated layout.</summary>
[CStructLayout("struct root { uint8 a; int16 b; uint32 c; int64 d; float32 e; float64 f; bool g; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class PrimRecordLayout
{
}
