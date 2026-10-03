namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The larger matrix with explicit opposite byte order.</summary>
[CStructLayout("struct root { uint16> grid[256][256]; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class MaterializedBigMatrix
{
}
