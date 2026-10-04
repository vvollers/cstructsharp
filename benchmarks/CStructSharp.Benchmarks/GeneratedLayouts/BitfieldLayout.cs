namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The unchanged <c>bitfield-x1k</c> fixture as a generated layout.</summary>
[CStructLayout("struct rec { uint8 a:3; uint8 b:5; uint16 c:4; uint16 :4; uint16 d:8; uint32 e:12; uint32 f:20; }; struct root { rec items[1024]; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class BitfieldLayout
{
}
