namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>A larger matrix that exposes temporary payload storage and copying.</summary>
[CStructLayout("struct root { uint16 grid[256][256]; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class MaterializedLargeMatrix
{
}
