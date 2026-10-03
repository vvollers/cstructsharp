namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The canonical small multidimensional fixture as a generated layout.</summary>
[CStructLayout("struct root { uint16 grid[16][16]; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class MaterializedSmallMatrix
{
}
