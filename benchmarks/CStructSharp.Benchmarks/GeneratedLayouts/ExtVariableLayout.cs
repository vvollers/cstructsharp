namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>A constrained record with a runtime-sized array, so the generated reader checks its conditions member by member.</summary>
[CStructLayout("struct vx { uint32 inodes @expect(inodes > 0); uint16 magic @expect(magic == 0xEF53, \"vx.magic\"); uint8 n @expect(n <= 16); uint8 data[n]; uint32 tail @supports(tail <= 1) @expect(tail != 7); };", Root = "vx", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class ExtVariableLayout
{
}
