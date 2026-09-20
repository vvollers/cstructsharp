namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The <c>cond-if128</c> fixture as a generated layout.</summary>
[CStructLayout("struct entry { uint8 tag; if (tag == 1) { uint32 value; } else { uint16 small; } uint16 tail; }; struct root { entry items[128]; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class ConditionalLayout
{
}
