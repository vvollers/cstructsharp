namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>Fixed text forms absent from the terminated-string benchmark fixture.</summary>
[CStructLayout("struct root { char name[1024]; utf8 utf[1024]; cp437 oem[1024]; wchar< wide[512]; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class MaterializedText
{
}
