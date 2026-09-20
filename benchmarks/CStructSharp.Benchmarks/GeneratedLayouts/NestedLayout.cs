namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The <c>nested-x256</c> fixture as a generated layout.</summary>
[CStructLayout("struct leaf { uint8 kind; uint32 value; }; struct mid { leaf first; leaf second; uint16 tail; }; struct top { mid left; mid right; uint8 mark; }; struct root { top items[256]; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class NestedLayout
{
}
